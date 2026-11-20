using SkailPaymentWebhook.Skail;

namespace SkailPaymentWebhook.Demo;

/// <summary>
/// Plays the payment gateway and the webhook endpoint at once: each delivery of an event starts ReleaseOrder with the
/// run id derived from the event id. A resend uses the same id, so skail does not start a second run.
/// </summary>
public sealed class PaymentGateway(IReadOnlyList<DemoPayment> payments, SkailApiClient skail)
    : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var demo in payments)
        {
            // a new event id per run of the app, so a new demo does not land on the runs of the previous one
            var payment = new PaymentConfirmed($"evt_{Guid.NewGuid().ToString("N")[..8]}", demo.OrderId, demo.Amount);
            var runId = ExecutionIds.FromEventId(payment.EventId).ToString();

            for (var send = 1; send <= demo.Sends; send++)
            {
                if (send > 1) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);   // the gateway resends later
                await skail.TriggerAsync(runId, payment, stoppingToken);
                Console.WriteLine(send == 1
                    ? $"[{payment.EventId}] webhook: order {payment.OrderId}, {SimulatedStore.Money(payment.Amount)} " +
                      $"→ run {runId[..8]} (expected: {demo.Expected})"
                    : $"[{payment.EventId}] webhook resent ({send}×) → run {runId[..8]}, the same run");
            }
        }

        Console.WriteLine("Press Ctrl+C to exit.");
    }
}
