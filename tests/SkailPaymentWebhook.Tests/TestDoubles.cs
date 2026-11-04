using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Web.Skail;
using SkailPaymentWebhook.Worker.Status;
using SkailPaymentWebhook.Worker.Store;

namespace SkailPaymentWebhook.Tests;

/// <summary>In-memory invoices for the worker rules; records what the commands wrote.</summary>
public sealed class FakeInvoiceStore(params Invoice[] invoices) : IInvoiceStore
{
    private readonly Dictionary<string, Invoice> _invoices = invoices.ToDictionary(i => i.OrderId);

    public List<(string OrderId, string EventId)> Paid { get; } = [];
    public List<(string OrderId, string EventId)> Released { get; } = [];
    public List<ReviewRequest> Reviews { get; } = [];

    /// <summary>Simulates another execution paying the invoice between the read and the write.</summary>
    public bool RefuseMarkPaid { get; init; }

    public Task<Invoice?> GetAsync(string orderId, CancellationToken ct = default) =>
        Task.FromResult(_invoices.GetValueOrDefault(orderId));

    public Task<bool> MarkPaidAsync(string orderId, string eventId, CancellationToken ct = default)
    {
        if (RefuseMarkPaid) return Task.FromResult(false);
        Paid.Add((orderId, eventId));
        return Task.FromResult(true);
    }

    public Task ReleaseAsync(string orderId, string eventId, CancellationToken ct = default)
    {
        Released.Add((orderId, eventId));
        return Task.CompletedTask;
    }

    public Task AddReviewAsync(ReviewRequest review, CancellationToken ct = default)
    {
        Reviews.Add(review);
        return Task.CompletedTask;
    }
}

public sealed class FakeStatusNotifier : IStatusNotifier
{
    public List<EventStatus> Sent { get; } = [];

    public Task NotifyAsync(EventStatus status, CancellationToken ct = default)
    {
        Sent.Add(status);
        return Task.CompletedTask;
    }
}

/// <summary>Stands in for skail in the web tests: records the triggers, or fails like an unavailable server.</summary>
public sealed class FakeSkailGateway : ISkailGateway
{
    public List<(Guid TaskId, PaymentConfirmed Payment)> Triggers { get; } = [];
    public bool Fail { get; set; }

    public Task TriggerAsync(Guid taskId, PaymentConfirmed payment, CancellationToken ct = default)
    {
        if (Fail) throw new SkailGatewayException("skail answered 503");
        Triggers.Add((taskId, payment));
        return Task.CompletedTask;
    }
}
