using SkailPaymentWebhook.Contracts;

namespace SkailPaymentWebhook.Worker.Status;

public interface IStatusNotifier
{
    Task NotifyAsync(EventStatus status, CancellationToken ct = default);
}
