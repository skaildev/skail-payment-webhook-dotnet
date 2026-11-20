namespace SkailPaymentWebhook.Demo;

/// <summary>One payment per path, set up for the invoices of the store.</summary>
public sealed record DemoPayment(string Path, string OrderId, decimal Amount, string Expected);

public static class DemoPayments
{
    public static readonly IReadOnlyList<DemoPayment> All =
    [
        new("released", "P-1", 99.90m, "released once, however many times it is sent"),
        new("amount", "P-2", 200.00m, "finance review: the amount does not match"),
        new("paid", "P-3", 80.00m, "finance review: the invoice was already paid"),
        new("missing", "P-9", 10.00m, "finance review: there is no invoice"),
    ];

    public static DemoPayment? Find(string path) =>
        All.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
}
