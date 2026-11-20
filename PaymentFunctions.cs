using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Threading;
using SkailPaymentWebhook.Demo;

namespace SkailPaymentWebhook;

public class PaymentFunctions(SimulatedStore store, Timeline timeline)
{
    /// <summary>
    /// The gateway says a payment came in, sometimes two or three times. The run id comes from the event id, so the
    /// resends land on this same run. The order only leaves when the invoice is settled; when something does not
    /// match, finance reviews the payment and the order stays put.
    /// The name and the parameter are the trigger contract: [{"EventId", "OrderId", "Amount"}].
    /// </summary>
    [SkailFunction]
    public async SkailTask<string> ReleaseOrder(PaymentConfirmed payment)
    {
        var settlement = await SettleInvoice(payment);
        if (!settlement.Done)
        {
            // the money came in, but something does not match: finance reviews it before the order leaves
            await OpenFinanceReview(payment, settlement.Reason!);
            return await ReportResult(payment.EventId, PaymentResults.Pending(settlement.Reason!));
        }

        await ReleaseForDelivery(payment.OrderId, payment.EventId);
        return await ReportResult(payment.EventId, PaymentResults.Released);
    }

    // The commands are the I/O, one operation each. skail never runs a completed command again, but a retry runs the
    // whole command again, so every write uses the event id as its idempotency key.

    /// <summary>Settles the invoice of the order. Not matching is a result (Done = false), not an error.</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask<SettleResult> SettleInvoice(PaymentConfirmed payment) =>
        await Task.FromResult(store.Settle(payment.EventId, payment.OrderId, payment.Amount));

    [SkailCommand(retryCount: 3)]
    public async SkailTask OpenFinanceReview(PaymentConfirmed payment, string reason)
    {
        store.OpenReview(payment.EventId, payment.OrderId, reason);
        await Task.CompletedTask;
    }

    [SkailCommand(retryCount: 3)]
    public async SkailTask ReleaseForDelivery(string orderId, string eventId)
    {
        store.Release(orderId, eventId);
        await Task.CompletedTask;
    }

    /// <summary>Reports how the payment ended, to the console and the page, and returns that result to the function.</summary>
    [SkailCommand]
    public async SkailTask<string> ReportResult(string eventId, string result)
    {
        timeline.Add(eventId, $"result: {result}", $"end:{result}");
        return await Task.FromResult(result);
    }
}
