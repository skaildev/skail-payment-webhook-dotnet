using System.Text.Json;
using System.Text.Json.Serialization;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Web.Skail;
using SkailPaymentWebhook.Web.Store;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<PaymentStore>();
builder.Services.AddSingleton(SkailOptions.FromEnvironment());
builder.Services.AddHttpClient<ISkailGateway, SkailApiGateway>(c => c.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache" });

app.MapGet("/healthz", () => Results.Ok("ok"));

// The endpoint the payment gateway calls (the page calls this same one). The event id becomes the execution id.
app.MapPost("/webhooks/payments", async (PaymentConfirmed? payment, PaymentStore store, ISkailGateway skail, ILogger<Program> log, CancellationToken ct) =>
{
    // In production, first validate the signature the gateway sends in the header.
    if (payment is null || string.IsNullOrWhiteSpace(payment.EventId) || string.IsNullOrWhiteSpace(payment.OrderId))
        return Results.BadRequest(new { error = "eventId and orderId are required" });

    var taskId = ExecutionIds.FromEventId(payment.EventId);   // same event => same execution id
    store.RecordDelivery(payment, taskId);
    try
    {
        await skail.TriggerAsync(taskId, payment, ct);
    }
    catch (SkailGatewayException ex)
    {
        log.LogWarning(ex, "skail did not accept the trigger for event {EventId}", payment.EventId);
        // If skail did not accept it, answer with an error: the gateway resends later and the resend lands on the same execution.
        store.MarkNotAccepted(payment.EventId);
        return Results.Json(new { taskId, error = $"skail unavailable, resend ({ex.Message})" }, statusCode: StatusCodes.Status502BadGateway);
    }

    store.MarkAccepted(payment.EventId);
    return Results.Ok(new { taskId });
});

app.MapGet("/api/state", (PaymentStore store) => Results.Ok(store.Snapshot()));

app.MapPost("/api/reset", (PaymentStore store) =>
{
    store.Reset();
    return Results.NoContent();
});

app.MapGet("/api/stream", async (HttpContext http, PaymentStore store, CancellationToken ct) =>
{
    using var subscription = store.Subscribe();   // subscribe before reading the snapshot: no update is lost

    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    async Task Send(StateView state)
    {
        var json = JsonSerializer.Serialize(state, Json.Options);
        await http.Response.WriteAsync($"data: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        await Send(store.Snapshot());
        await foreach (var state in subscription.Reader.ReadAllAsync(ct)) await Send(state);
    }
    catch (OperationCanceledException)
    {
        // client disconnected
    }
    return Results.Empty;
});

// ---- called by the worker: the demo "database" -----------------------------------------------------------------

app.MapGet("/internal/invoices/{orderId}", (string orderId, PaymentStore store) =>
    store.GetInvoice(orderId) is { } invoice ? Results.Ok(invoice) : Results.NotFound());

app.MapPost("/internal/invoices/{orderId}/paid", (string orderId, MarkPaidRequest request, PaymentStore store) =>
    store.MarkPaid(orderId, request.EventId) switch
    {
        MarkPaidResult.Ok => Results.NoContent(),
        MarkPaidResult.NotFound => Results.NotFound(),
        _ => Results.Conflict(new { error = "the invoice was already paid by another payment" }),
    });

app.MapPost("/internal/orders/{orderId}/release", (string orderId, ReleaseRequest request, PaymentStore store) =>
{
    store.Release(orderId, request.EventId);
    return Results.NoContent();
});

app.MapPost("/internal/reviews", (ReviewRequest review, PaymentStore store) =>
{
    store.AddReview(review);
    return Results.NoContent();
});

app.MapPost("/internal/events/{eventId}/status", (string eventId, EventStatus status, PaymentStore store) =>
{
    store.ApplyStatus(status with { EventId = eventId });
    return Results.NoContent();
});

app.Run();

public partial class Program;
