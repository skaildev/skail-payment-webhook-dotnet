using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Web.Skail;
using SkailPaymentWebhook.Web.Store;

namespace SkailPaymentWebhook.Tests;

public class WebApiTests : IDisposable
{
    private readonly FakeSkailGateway _skail = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WebApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<ISkailGateway>();
            services.AddSingleton<ISkailGateway>(_skail);
        }));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private PaymentStore Store => _factory.Services.GetRequiredService<PaymentStore>();

    private static object Webhook(string eventId = "evt_1", string orderId = "P-1", decimal amount = 99.90m) =>
        new { eventId, orderId, amount };

    private async Task<StateView> State() => (await _client.GetFromJsonAsync<StateView>("/api/state", Json.Options))!;

    // ---- the webhook ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "P-1")]
    [InlineData("evt_1", "")]
    [InlineData("  ", "P-1")]
    public async Task The_webhook_refuses_a_notice_without_event_id_or_order_id(string eventId, string orderId)
    {
        var response = await _client.PostAsJsonAsync("/webhooks/payments", Webhook(eventId, orderId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_skail.Triggers);
        Assert.Empty((await State()).Events);
    }

    [Fact]
    public async Task The_webhook_refuses_an_empty_body()
    {
        var response = await _client.PostAsync("/webhooks/payments", new StringContent("", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_webhook_triggers_the_function_with_the_event_id_as_the_execution_id()
    {
        var response = await _client.PostAsJsonAsync("/webhooks/payments", Webhook());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        var (taskId, payment) = Assert.Single(_skail.Triggers);
        Assert.Equal(ExecutionIds.FromEventId("evt_1"), taskId);
        Assert.Equal(taskId, body!["taskId"]);
        Assert.Equal(new PaymentConfirmed("evt_1", "P-1", 99.90m), payment);
    }

    [Fact]
    public async Task The_same_notice_twice_gives_the_same_task_id_and_counts_two_deliveries_of_one_event()
    {
        var first = await (await _client.PostAsJsonAsync("/webhooks/payments", Webhook())).Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        var second = await (await _client.PostAsJsonAsync("/webhooks/payments", Webhook())).Content.ReadFromJsonAsync<Dictionary<string, Guid>>();

        Assert.Equal(first!["taskId"], second!["taskId"]);
        Assert.Equal(2, _skail.Triggers.Count);
        Assert.Single(_skail.Triggers.Select(t => t.TaskId).Distinct());
        var row = Assert.Single((await State()).Events);
        Assert.Equal(2, row.Deliveries);
    }

    [Fact]
    public async Task Different_events_get_different_task_ids()
    {
        await _client.PostAsJsonAsync("/webhooks/payments", Webhook("evt_1"));
        await _client.PostAsJsonAsync("/webhooks/payments", Webhook("evt_2", "P-2", 200m));

        Assert.Equal(2, _skail.Triggers.Select(t => t.TaskId).Distinct().Count());
    }

    [Fact]
    public async Task When_skail_does_not_accept_the_trigger_the_webhook_answers_502_so_the_gateway_resends()
    {
        _skail.Fail = true;

        var response = await _client.PostAsJsonAsync("/webhooks/payments", Webhook());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(EventState.NotAccepted, Assert.Single((await State()).Events).State);

        // the resend (same event id) works once skail is back
        _skail.Fail = false;
        var resend = await _client.PostAsJsonAsync("/webhooks/payments", Webhook());

        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var row = Assert.Single((await State()).Events);
        Assert.Equal((EventState.Received, 2), (row.State, row.Deliveries));
    }

    // ---- state, reset ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task State_has_the_seeded_invoices_and_no_events()
    {
        var state = await State();

        Assert.Equal(3, state.Invoices.Count);
        Assert.Empty(state.Events);
        Assert.Empty(state.Reviews);
    }

    [Fact]
    public async Task Reset_clears_the_events_and_reopens_the_invoices()
    {
        await _client.PostAsJsonAsync("/webhooks/payments", Webhook());
        await _client.PostAsJsonAsync("/internal/invoices/P-1/paid", new MarkPaidRequest("evt_1"));

        var response = await _client.PostAsync("/api/reset", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var state = await State();
        Assert.Empty(state.Events);
        Assert.Null(state.Invoices.Single(i => i.OrderId == "P-1").PaidByEvent);
    }

    // ---- the routes the worker calls ------------------------------------------------------------------------------

    [Fact]
    public async Task Internal_invoice_lookup_returns_the_invoice_or_404()
    {
        var found = await _client.GetFromJsonAsync<Invoice>("/internal/invoices/P-2", Json.Options);
        var missing = await _client.GetAsync("/internal/invoices/P-9");

        Assert.Equal(new Invoice("P-2", 250.00m, null), found);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Internal_paid_is_idempotent_for_the_same_event_and_409_for_another()
    {
        var first = await _client.PostAsJsonAsync("/internal/invoices/P-1/paid", new MarkPaidRequest("evt_1"));
        var again = await _client.PostAsJsonAsync("/internal/invoices/P-1/paid", new MarkPaidRequest("evt_1"));
        var other = await _client.PostAsJsonAsync("/internal/invoices/P-1/paid", new MarkPaidRequest("evt_2"));
        var missing = await _client.PostAsJsonAsync("/internal/invoices/P-9/paid", new MarkPaidRequest("evt_1"));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Internal_release_twice_for_the_same_event_releases_the_order_once()
    {
        await _client.PostAsJsonAsync("/internal/orders/P-1/release", new ReleaseRequest("evt_1"));
        await _client.PostAsJsonAsync("/internal/orders/P-1/release", new ReleaseRequest("evt_1"));

        Assert.Equal(1, (await State()).Invoices.Single(i => i.OrderId == "P-1").Releases);
    }

    [Fact]
    public async Task Internal_review_shows_on_the_state()
    {
        await _client.PostAsJsonAsync("/internal/reviews", new ReviewRequest("evt_2", "P-2", "paid R$ 200.00, invoice R$ 250.00"));

        Assert.Equal(new ReviewView("evt_2", "P-2", "paid R$ 200.00, invoice R$ 250.00"), Assert.Single((await State()).Reviews));
    }

    [Fact]
    public async Task Internal_status_updates_the_event_using_the_id_of_the_route()
    {
        await _client.PostAsJsonAsync("/webhooks/payments", Webhook());

        await _client.PostAsJsonAsync("/internal/events/evt_1/status", new EventStatus("ignored", EventStage.Completed, PaymentResults.Released));

        var row = Assert.Single((await State()).Events);
        Assert.Equal((EventState.Released, PaymentResults.Released), (row.State, row.Result));
    }

    // ---- stream and page ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_stream_sends_the_current_state_first_and_then_each_change()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _client.GetAsync("/api/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        var first = await ReadEvent(reader);
        Assert.Empty(first.Events);

        await _client.PostAsJsonAsync("/webhooks/payments", Webhook());
        var next = await ReadEvent(reader);
        Assert.Equal("evt_1", Assert.Single(next.Events).EventId);
    }

    [Fact]
    public async Task The_page_has_the_simulator_buttons()
    {
        var html = await _client.GetStringAsync("/");

        Assert.Contains("Send again", html);
        Assert.Contains("Reset demo", html);
        Assert.Contains("data-order=\"P-1\"", html);
        Assert.Contains("data-order=\"P-2\"", html);
    }

    [Fact]
    public async Task Healthz_answers_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/healthz")).StatusCode);
    }

    private static async Task<StateView> ReadEvent(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                return System.Text.Json.JsonSerializer.Deserialize<StateView>(line["data: ".Length..], Json.Options)!;
        throw new InvalidOperationException("the stream ended");
    }
}
