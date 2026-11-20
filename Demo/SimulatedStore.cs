using System.Globalization;

namespace SkailPaymentWebhook.Demo;

/// <summary>
/// The demo "database": the invoices, the deliveries and the finance reviews. In your system these would be your tables;
/// here they live in memory and report each change to the timeline (console and page). Every write is idempotent by event id, because a
/// retried command runs again.
/// </summary>
public sealed class SimulatedStore(Timeline timeline)
{
    public const string AlreadyPaidByAnother = "the invoice was already paid by another payment";

    private readonly object gate = new();

    /// <summary>The invoices by order id, and the event that paid each one.</summary>
    private Dictionary<string, Invoice> invoices = OpenInvoices();

    private readonly HashSet<string> released = [];   // order ids
    private readonly HashSet<string> reviews = [];    // event ids

    /// <summary>May this payment settle the invoice? Everything that does not match comes back as a result.</summary>
    public SettleResult Settle(string eventId, string orderId, decimal amount)
    {
        lock (gate)
        {
            if (!invoices.TryGetValue(orderId, out var invoice))
                return new(false, $"no invoice for order {orderId}");
            if (invoice.PaidByEvent == eventId)
                return new(true, null);   // a retry of this same settlement
            if (invoice.PaidByEvent is not null)
                return new(false, AlreadyPaidByAnother);
            if (invoice.Amount != amount)
                return new(false, $"paid {Money(amount)}, invoice {Money(invoice.Amount)}");

            invoices[orderId] = invoice with { PaidByEvent = eventId };
            Print(eventId, $"invoice {orderId}: paid, {Money(amount)}");
            return new(true, null);
        }
    }

    public void Release(string orderId, string eventId)
    {
        lock (gate)
        {
            if (released.Add(orderId)) timeline.Add(eventId, $"delivery: order {orderId} released", "released");
        }
    }

    public void OpenReview(string eventId, string orderId, string reason)
    {
        lock (gate)
        {
            if (reviews.Add(eventId))
                timeline.Add(eventId, $"finance: review opened for order {orderId} ({reason})", "review");
        }
    }

    public static string Money(decimal value) => "R$ " + value.ToString("N2", CultureInfo.InvariantCulture);

    private void Print(string eventId, string message) => timeline.Add(eventId, message);

    /// <summary>The invoice of the order, for the page: its amount and the event that paid it, if any.</summary>
    public object? InvoiceOf(string orderId)
    {
        lock (gate)
            return invoices.TryGetValue(orderId, out var i) ? new { amount = i.Amount, paidBy = i.PaidByEvent } : null;
    }

    /// <summary>Back to the invoices of the start, with nothing released nor reviewed (the Start over button).</summary>
    public void Reset()
    {
        lock (gate)
        {
            invoices = OpenInvoices();
            released.Clear();
            reviews.Clear();
        }
    }

    /// <summary>The invoices of the store, by order id. P-3 was already paid by an older event.</summary>
    private static Dictionary<string, Invoice> OpenInvoices() => new()
    {
        ["P-1"] = new(99.90m, null),
        ["P-2"] = new(250.00m, null),
        ["P-3"] = new(80.00m, "evt_old"),
    };

    private sealed record Invoice(decimal Amount, string? PaidByEvent);
}
