using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Threading;
using SkailPaymentWebhook.Contracts;

namespace SkailPaymentWebhook.Worker;

public class PaymentFunctions(PaymentCommands commands)
{
    /// <summary>
    /// The gateway says a payment came in, sometimes two or three times. The event id is the execution id, so the resends
    /// land on this same execution. The order only leaves when the invoice is settled; when something does not match,
    /// finance reviews the payment first and the order stays put.
    /// </summary>
    [SkailFunction]
    public async SkailTask<string> ReleaseOrder(PaymentConfirmed payment)
    {
        await commands.ReportStatus(payment.EventId, EventStage.Started, null);

        string result;
        try
        {
            var settlement = await commands.SettleInvoice(payment);
            if (!settlement.Done)
            {
                // The money came in, but something does not match: finance reviews it before the order leaves.
                await commands.OpenFinanceReview(payment, settlement.Reason!);
                result = PaymentResults.Pending(settlement.Reason!);
            }
            else
            {
                await commands.ReleaseForDelivery(payment.OrderId, payment.EventId);   // only runs with the invoice settled
                result = PaymentResults.Released;
            }
        }
        catch (Exception ex) when (ex is not SkailNonDeterministicException)   // that one means the code and the run history diverged
        {
            // Every attempt of a command failed (a technical error, not a mismatch): say so on the page and rethrow, so the
            // run shows up as failed in the Monitor.
            await commands.ReportStatus(payment.EventId, EventStage.Failed, ex.Message);
            throw;
        }

        await commands.ReportStatus(payment.EventId, EventStage.Completed, result);
        return result;
    }
}
