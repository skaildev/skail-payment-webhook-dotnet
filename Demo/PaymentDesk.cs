using SkailPaymentWebhook.Skail;

namespace SkailPaymentWebhook.Demo;

/// <summary>
/// Plays the payment gateway and the webhook endpoint for the page, one scenario per card: each delivery of an event
/// starts ReleaseOrder with the run id derived from the event id. Resending the event of a card uses the same event
/// id, so skail does not start a second run.
/// </summary>
public sealed class PaymentDesk(SimulatedStore store, SkailApiClient skail, Timeline timeline)
{
    private sealed record Sent(PaymentConfirmed Payment, string RunId)
    {
        public int Deliveries { get; set; }
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Sent> sent = []; // by scenario

    /// <summary>Sends the payment of a scenario, once: a new event id, so it does not land on a previous run.</summary>
    public async Task<bool> SendAsync(string path, CancellationToken ct)
    {
        if (DemoPayments.Find(path) is not { } demo) return false;

        var payment = new PaymentConfirmed($"evt_{Guid.NewGuid().ToString("N")[..8]}", demo.OrderId, demo.Amount);
        var webhook = new Sent(payment, ExecutionIds.FromEventId(payment.EventId).ToString());
        lock (gate)
            if (!sent.TryAdd(demo.Path, webhook)) return false;
        await DeliverAsync(webhook, ct);
        return true;
    }

    /// <summary>The gateway sends the event of the scenario again, with the same event id: it lands on the same run.</summary>
    public async Task<bool> SendAgainAsync(string path, CancellationToken ct)
    {
        Sent? webhook;
        lock (gate) sent.TryGetValue(path, out webhook);
        if (webhook is null) return false;
        await DeliverAsync(webhook, ct);
        return true;
    }

    /// <summary>Back to the start: the invoices as they were, and no payment sent.</summary>
    public void Reset()
    {
        lock (gate) sent.Clear();
        store.Reset();
        timeline.Clear();
    }

    private async Task DeliverAsync(Sent webhook, CancellationToken ct)
    {
        await skail.TriggerAsync(webhook.RunId, webhook.Payment, ct);
        int deliveries;
        lock (gate) deliveries = ++webhook.Deliveries;
        var p = webhook.Payment;
        timeline.Add(p.EventId, deliveries == 1
            ? $"webhook: order {p.OrderId}, {SimulatedStore.Money(p.Amount)} → run {webhook.RunId[..8]}"
            : $"webhook resent ({deliveries}×) → run {webhook.RunId[..8]}, the same run");
    }

    /// <summary>What the page shows: each scenario with its invoice and, once sent, what happened to its event.</summary>
    public object View()
    {
        var entries = timeline.All();
        int Count(string eventId, string step) => entries.Count(e => e.RunId == eventId && e.Step == step);

        lock (gate)
        {
            return new
            {
                scenarios = DemoPayments.All.Select(d => new
                {
                    d.Path, d.OrderId, d.Amount,
                    invoice = store.InvoiceOf(d.OrderId),
                    sent = sent.TryGetValue(d.Path, out var s)
                        ? new
                        {
                            s.Payment.EventId, s.RunId, s.Deliveries,
                            released = Count(s.Payment.EventId, "released"),
                            reviews = Count(s.Payment.EventId, "review"),
                            result = entries.LastOrDefault(e => e.RunId == s.Payment.EventId && e.Step?.StartsWith("end:") == true)
                                ?.Step?[4..],
                        }
                        : null,
                }),
                timeline = entries.Select(t => new { at = t.At, @event = t.RunId, text = t.Text }),
            };
        }
    }
}
