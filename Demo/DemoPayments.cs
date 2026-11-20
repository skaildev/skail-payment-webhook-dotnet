namespace SkailPaymentWebhook.Demo;

/// <summary>One payment per path, set up for the invoices of the store. Sends is how many times the gateway sends it.</summary>
public sealed record DemoPayment(string Path, string OrderId, decimal Amount, int Sends, string Expected);

public static class DemoPayments
{
    public static readonly IReadOnlyList<DemoPayment> All =
    [
        new("released", "P-1", 99.90m, 3, "released once, however many times it is sent"),
        new("amount", "P-2", 200.00m, 1, "finance review: the amount does not match"),
        new("paid", "P-3", 80.00m, 1, "finance review: the invoice was already paid"),
        new("missing", "P-9", 10.00m, 1, "finance review: there is no invoice"),
    ];

    public static DemoPayment? Find(string path) =>
        All.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
}
