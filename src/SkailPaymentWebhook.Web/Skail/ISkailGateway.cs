using SkailPaymentWebhook.Contracts;

namespace SkailPaymentWebhook.Web.Skail;

public sealed class SkailGatewayException(string message, Exception? inner = null) : Exception(message, inner);

public interface ISkailGateway
{
    /// <summary>Starts the ReleaseOrder function; <paramref name="taskId"/> is the execution id.</summary>
    Task TriggerAsync(Guid taskId, PaymentConfirmed payment, CancellationToken ct = default);
}
