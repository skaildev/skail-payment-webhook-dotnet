using SkailPaymentWebhook.Contracts;

namespace SkailPaymentWebhook.Worker.Store;

/// <summary>The demo "database" (invoices, deliveries, finance reviews). In the demo it is the web app.</summary>
public interface IInvoiceStore
{
    Task<Invoice?> GetAsync(string orderId, CancellationToken ct = default);

    /// <summary>Marks the invoice as paid by the event. Idempotent for the same event; false when another event paid it.</summary>
    Task<bool> MarkPaidAsync(string orderId, string eventId, CancellationToken ct = default);

    /// <summary>Idempotent by event id.</summary>
    Task ReleaseAsync(string orderId, string eventId, CancellationToken ct = default);

    /// <summary>Idempotent by event id.</summary>
    Task AddReviewAsync(ReviewRequest review, CancellationToken ct = default);
}
