using System.Threading.Channels;
using SkailPaymentWebhook.Contracts;

namespace SkailPaymentWebhook.Web.Store;

public enum EventState
{
    Received,
    NotAccepted,
    Running,
    Released,
    Pending,
    Failed,
}

public enum MarkPaidResult
{
    Ok,
    NotFound,
    PaidByAnother,
}

public sealed record InvoiceView(string OrderId, decimal Amount, string? PaidByEvent, int Releases);

public sealed record EventView(
    string EventId, string OrderId, decimal Amount, Guid TaskId, int Deliveries, EventState State, string? Result);

public sealed record ReviewView(string EventId, string OrderId, string Reason);

public sealed record StateView(
    IReadOnlyList<InvoiceView> Invoices, IReadOnlyList<EventView> Events, IReadOnlyList<ReviewView> Reviews);

/// <summary>
/// In-memory "database" of the demo (invoices, deliveries, finance reviews) and the view of the received webhooks.
/// The durable state lives in skail; this is what the page shows. Every write that comes from the worker is idempotent
/// by event id, because a command that is retried runs again.
/// </summary>
public sealed class PaymentStore
{
    private sealed class EventRow(PaymentConfirmed payment, Guid taskId)
    {
        public PaymentConfirmed Payment { get; } = payment;
        public Guid TaskId { get; } = taskId;
        public int Deliveries { get; set; }
        public bool Accepted { get; set; }
        public EventState State { get; set; } = EventState.Received;
        public string? Result { get; set; }
        public bool IsTerminal => State is EventState.Released or EventState.Pending or EventState.Failed;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Invoice> _invoices = Seed();
    private readonly Dictionary<string, HashSet<string>> _releases = [];
    private readonly List<EventRow> _events = [];
    private readonly Dictionary<string, ReviewView> _reviews = [];
    private readonly List<Channel<StateView>> _subscribers = [];

    /// <summary>P-1 and P-2 are open; P-3 was already paid by another event.</summary>
    private static Dictionary<string, Invoice> Seed() => new()
    {
        ["P-1"] = new("P-1", 99.90m, null),
        ["P-2"] = new("P-2", 250.00m, null),
        ["P-3"] = new("P-3", 80.00m, "evt_old"),
    };

    // --- the webhook -----------------------------------------------------------------------------------------------

    /// <summary>A webhook delivery arrived: new event, or one more delivery of an event already known.</summary>
    public void RecordDelivery(PaymentConfirmed payment, Guid taskId)
    {
        lock (_gate)
        {
            var row = _events.FirstOrDefault(e => e.Payment.EventId == payment.EventId);
            if (row is null) _events.Add(row = new EventRow(payment, taskId));
            row.Deliveries++;
            Publish();
        }
    }

    /// <summary>skail accepted the trigger.</summary>
    public void MarkAccepted(string eventId)
    {
        lock (_gate)
        {
            if (Find(eventId) is not { } row) return;
            row.Accepted = true;
            if (row.State == EventState.NotAccepted) row.State = EventState.Received;
            Publish();
        }
    }

    /// <summary>skail did not accept the trigger (the gateway will resend).</summary>
    public void MarkNotAccepted(string eventId)
    {
        lock (_gate)
        {
            if (Find(eventId) is not { } row || row.Accepted || row.State != EventState.Received) return;
            row.State = EventState.NotAccepted;
            Publish();
        }
    }

    // --- the demo "database" (called by the worker) ----------------------------------------------------------------

    public Invoice? GetInvoice(string orderId)
    {
        lock (_gate) return _invoices.GetValueOrDefault(orderId);
    }

    /// <summary>Marks the invoice paid by the event: idempotent for the same event, refused when another event paid it.</summary>
    public MarkPaidResult MarkPaid(string orderId, string eventId)
    {
        lock (_gate)
        {
            if (!_invoices.TryGetValue(orderId, out var invoice)) return MarkPaidResult.NotFound;
            if (invoice.PaidByEvent is not null && invoice.PaidByEvent != eventId) return MarkPaidResult.PaidByAnother;
            if (invoice.PaidByEvent is null)
            {
                _invoices[orderId] = invoice with { PaidByEvent = eventId };
                Publish();
            }
            return MarkPaidResult.Ok;
        }
    }

    /// <summary>Idempotent by event id: a retried command does not release the order twice.</summary>
    public void Release(string orderId, string eventId)
    {
        lock (_gate)
        {
            if (!_releases.TryGetValue(orderId, out var events)) _releases[orderId] = events = [];
            if (events.Add(eventId)) Publish();
        }
    }

    /// <summary>Idempotent by event id.</summary>
    public void AddReview(ReviewRequest review)
    {
        lock (_gate)
        {
            if (_reviews.TryAdd(review.EventId, new ReviewView(review.EventId, review.OrderId, review.Reason)))
                Publish();
        }
    }

    // --- progress of the execution (called by the worker) ----------------------------------------------------------

    /// <returns>true when the status moved the event; false when ignored (unknown or finished event, or a repeat).</returns>
    public bool ApplyStatus(EventStatus status)
    {
        lock (_gate)
        {
            if (Find(status.EventId) is not { } row || row.IsTerminal) return false;

            switch (status.Stage)
            {
                case EventStage.Started:
                    row.State = EventState.Running;
                    break;
                case EventStage.Completed:
                    row.State = status.Detail?.StartsWith(PaymentResults.PendingPrefix, StringComparison.Ordinal) == true
                        ? EventState.Pending
                        : EventState.Released;
                    row.Result = status.Detail;
                    break;
                case EventStage.Failed:
                    row.State = EventState.Failed;
                    row.Result = status.Detail;
                    break;
            }
            Publish();
            return true;
        }
    }

    // --- the page --------------------------------------------------------------------------------------------------

    public StateView Snapshot()
    {
        lock (_gate) return BuildView();
    }

    /// <summary>Back to the seed: open invoices, no events, no reviews. For running the demo again.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _invoices.Clear();
            foreach (var (key, value) in Seed()) _invoices[key] = value;
            _releases.Clear();
            _events.Clear();
            _reviews.Clear();
            Publish();
        }
    }

    public Subscription Subscribe()
    {
        // only the latest snapshot matters, so a slow reader just skips the older ones
        var channel = Channel.CreateBounded<StateView>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_gate) _subscribers.Add(channel);
        return new Subscription(channel.Reader, () =>
        {
            lock (_gate) _subscribers.Remove(channel);
        });
    }

    // must be called with _gate held
    private EventRow? Find(string eventId) => _events.FirstOrDefault(e => e.Payment.EventId == eventId);

    private StateView BuildView() => new(
        _invoices.Values.OrderBy(i => i.OrderId)
            .Select(i => new InvoiceView(i.OrderId, i.Amount, i.PaidByEvent, _releases.GetValueOrDefault(i.OrderId)?.Count ?? 0))
            .ToList(),
        _events.Select(e => new EventView(
            e.Payment.EventId, e.Payment.OrderId, e.Payment.Amount, e.TaskId, e.Deliveries, e.State, e.Result)).ToList(),
        _reviews.Values.ToList());

    // must be called with _gate held
    private void Publish()
    {
        var view = BuildView();
        foreach (var channel in _subscribers) channel.Writer.TryWrite(view);
    }

    public sealed class Subscription(ChannelReader<StateView> reader, Action onDispose) : IDisposable
    {
        public ChannelReader<StateView> Reader { get; } = reader;
        public void Dispose() => onDispose();
    }
}
