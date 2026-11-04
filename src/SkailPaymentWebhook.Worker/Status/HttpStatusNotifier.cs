using System.Net.Http.Json;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Worker.Store;

namespace SkailPaymentWebhook.Worker.Status;

/// <summary>Reports progress to the web app: POST {web}/internal/events/{id}/status.</summary>
public sealed class HttpStatusNotifier(HttpClient http) : IStatusNotifier
{
    public async Task NotifyAsync(EventStatus status, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"internal/events/{Uri.EscapeDataString(status.EventId)}/status", status, Json.Options, ct);
        WebAnswers.EnsureSuccess(response, "status");
    }
}
