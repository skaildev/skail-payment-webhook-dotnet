using System.Globalization;

namespace SkailPaymentWebhook.Demo;

/// <summary>
/// The demo "database": the invoices, the deliveries and the finance reviews. In your system these would be your tables;
/// here they live in memory and print each change to the console. Every write is idempotent by event id, because a
/// retried command runs again.
/// </summary>
public sealed class SimulatedStore
{
    public const string AlreadyPaidByAnother = "the invoice was already paid by another payment";

    private readonly object gate = new();

    /// <summary>Open invoices by order id, and who paid them. P-3 was already paid by an older event.</summary>
    private readonly Dictionary<string, Invoice> invoices = new()
    {
        ["P-1"] = new(99.90m, null),
        ["P-2"] = new(250.00m, null),
        ["P-3"] = new(80.00m, "evt_old"),
    };

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
            if (released.Add(orderId)) Print(eventId, $"delivery: order {orderId} released");
        }
    }

    public void OpenReview(string eventId, string orderId, string reason)
    {
        lock (gate)
        {
            if (reviews.Add(eventId)) Print(eventId, $"finance: review opened for order {orderId} ({reason})");
        }
    }

    public static string Money(decimal value) => "R$ " + value.ToString("N2", CultureInfo.InvariantCulture);

    private static void Print(string eventId, string message) => Console.WriteLine($"[{eventId}] {message}");

    private sealed record Invoice(decimal Amount, string? PaidByEvent);
}
