using System.Net;
using System.Net.Http.Json;
using SkailPaymentWebhook.Contracts;

namespace SkailPaymentWebhook.Worker.Store;

/// <summary>The invoices live in the web app: GET/POST {web}/internal/...</summary>
public sealed class HttpInvoiceStore(HttpClient http) : IInvoiceStore
{
    public async Task<Invoice?> GetAsync(string orderId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"internal/invoices/{Uri.EscapeDataString(orderId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        WebAnswers.EnsureSuccess(response, "invoice read");
        return await response.Content.ReadFromJsonAsync<Invoice>(Json.Options, ct);
    }

    public async Task<bool> MarkPaidAsync(string orderId, string eventId, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"internal/invoices/{Uri.EscapeDataString(orderId)}/paid", new MarkPaidRequest(eventId), Json.Options, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        WebAnswers.EnsureSuccess(response, "invoice payment");
        return true;
    }

    public async Task ReleaseAsync(string orderId, string eventId, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"internal/orders/{Uri.EscapeDataString(orderId)}/release", new ReleaseRequest(eventId), Json.Options, ct);
        WebAnswers.EnsureSuccess(response, "order release");
    }

    public async Task AddReviewAsync(ReviewRequest review, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("internal/reviews", review, Json.Options, ct);
        WebAnswers.EnsureSuccess(response, "review");
    }
}

internal static class WebAnswers
{
    // When a command fails, this message is what the skail Monitor shows.
    public static void EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Web app answered {(int)response.StatusCode} to the {what}", null, response.StatusCode);
    }
}
