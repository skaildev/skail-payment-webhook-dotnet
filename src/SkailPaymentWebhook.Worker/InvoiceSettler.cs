using System.Globalization;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Worker.Store;

namespace SkailPaymentWebhook.Worker;

/// <summary>
/// The rule of the SettleInvoice command: may this payment settle the invoice? Everything that does not match comes back
/// as a result (the money came in, so a person checks it), never as an exception. Kept out of the command so it can be
/// tested without the skail runtime.
/// </summary>
public sealed class InvoiceSettler(IInvoiceStore store)
{
    public const string AlreadyPaidByAnother = "the invoice was already paid by another payment";

    public async Task<SettleResult> SettleAsync(PaymentConfirmed payment, CancellationToken ct = default)
    {
        var invoice = await store.GetAsync(payment.OrderId, ct);
        var result =
            invoice is null ? new SettleResult(false, $"no invoice for order {payment.OrderId}") :
            invoice.PaidByEvent == payment.EventId ? new SettleResult(true, null) :   // a retry of this same settlement
            invoice.PaidByEvent is not null ? new SettleResult(false, AlreadyPaidByAnother) :
            invoice.Amount != payment.Amount ? new SettleResult(false, $"paid R$ {Money(payment.Amount)}, invoice R$ {Money(invoice.Amount)}") :
            new SettleResult(true, null);

        // the store refuses when another event got there first (two executions for the same invoice at once)
        if (result.Done && !await store.MarkPaidAsync(payment.OrderId, payment.EventId, ct))
            result = new SettleResult(false, AlreadyPaidByAnother);

        return result;
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
