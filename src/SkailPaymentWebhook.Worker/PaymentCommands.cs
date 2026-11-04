using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Threading;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Worker.Status;
using SkailPaymentWebhook.Worker.Store;

namespace SkailPaymentWebhook.Worker;

/// <summary>
/// The I/O of the function, one operation per command. skail never runs a finished command again, but a retry runs the
/// whole command again, so every command is idempotent by event id.
/// </summary>
public class PaymentCommands(InvoiceSettler settler, IInvoiceStore store, IStatusNotifier notifier)
{
    /// <summary>Settles the invoice of the order. Not matching is a result (Done = false), not an error.</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask<SettleResult> SettleInvoice(PaymentConfirmed payment)
    {
        return await settler.SettleAsync(payment);
    }

    [SkailCommand(retryCount: 3)]
    public async SkailTask OpenFinanceReview(PaymentConfirmed payment, string reason)
    {
        await store.AddReviewAsync(new ReviewRequest(payment.EventId, payment.OrderId, reason));
    }

    [SkailCommand(retryCount: 3)]
    public async SkailTask ReleaseForDelivery(string orderId, string eventId)
    {
        await store.ReleaseAsync(orderId, eventId);
    }

    /// <summary>Tells the page how the run is going. Idempotent: the web app ignores a status that does not move the event forward.</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask ReportStatus(string eventId, EventStage stage, string? detail)
    {
        await notifier.NotifyAsync(new EventStatus(eventId, stage, detail));
    }
}
