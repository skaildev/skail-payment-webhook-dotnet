using System.Net;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Worker.Status;
using SkailPaymentWebhook.Worker.Store;

namespace SkailPaymentWebhook.Tests;

/// <summary>The worker's HTTP clients to the web app: routes, bodies, and how failures surface.</summary>
public class WorkerHttpTests
{
    private static HttpClient Client(StubHandler handler) => new(handler) { BaseAddress = new Uri("http://web:8080/") };

    [Fact]
    public async Task GetAsync_reads_the_invoice()
    {
        var handler = StubHandler.Json("""{"orderId":"P-1","amount":99.9,"paidByEvent":null}""");

        var invoice = await new HttpInvoiceStore(Client(handler)).GetAsync("P-1");

        Assert.Equal(new Invoice("P-1", 99.9m, null), invoice);
        var (request, _) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://web:8080/internal/invoices/P-1", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAsync_returns_null_when_there_is_no_invoice()
    {
        var invoice = await new HttpInvoiceStore(Client(StubHandler.Status(HttpStatusCode.NotFound))).GetAsync("P-9");

        Assert.Null(invoice);
    }

    [Fact]
    public async Task GetAsync_throws_on_a_technical_error_so_skail_retries_the_command()
    {
        var store = new HttpInvoiceStore(Client(StubHandler.Status(HttpStatusCode.InternalServerError)));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => store.GetAsync("P-1"));
        Assert.Equal("Web app answered 500 to the invoice read", error.Message);   // what the Monitor shows
    }

    [Fact]
    public async Task MarkPaidAsync_posts_the_event_id()
    {
        var handler = StubHandler.Status(HttpStatusCode.NoContent);

        Assert.True(await new HttpInvoiceStore(Client(handler)).MarkPaidAsync("P-1", "evt_1"));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("http://web:8080/internal/invoices/P-1/paid", request.RequestUri!.ToString());
        Assert.Equal("""{"eventId":"evt_1"}""", body);
    }

    [Fact]
    public async Task MarkPaidAsync_is_false_on_conflict_and_throws_on_other_errors()
    {
        Assert.False(await new HttpInvoiceStore(Client(StubHandler.Status(HttpStatusCode.Conflict))).MarkPaidAsync("P-3", "evt_1"));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new HttpInvoiceStore(Client(StubHandler.Status(HttpStatusCode.BadGateway))).MarkPaidAsync("P-3", "evt_1"));
    }

    [Fact]
    public async Task ReleaseAsync_and_AddReviewAsync_post_to_their_routes()
    {
        var handler = StubHandler.Status(HttpStatusCode.NoContent);
        var store = new HttpInvoiceStore(Client(handler));

        await store.ReleaseAsync("P-1", "evt_1");
        await store.AddReviewAsync(new ReviewRequest("evt_2", "P-2", "paid R$ 200.00, invoice R$ 250.00"));

        Assert.Equal("http://web:8080/internal/orders/P-1/release", handler.Calls[0].Request.RequestUri!.ToString());
        Assert.Equal("""{"eventId":"evt_1"}""", handler.Calls[0].Body);
        Assert.Equal("http://web:8080/internal/reviews", handler.Calls[1].Request.RequestUri!.ToString());
        Assert.Equal("""{"eventId":"evt_2","orderId":"P-2","reason":"paid R$ 200.00, invoice R$ 250.00"}""", handler.Calls[1].Body);
    }

    [Fact]
    public async Task The_notifier_posts_the_status_with_the_stage_as_a_string()
    {
        var handler = StubHandler.Status(HttpStatusCode.NoContent);

        await new HttpStatusNotifier(Client(handler)).NotifyAsync(new EventStatus("evt_1", EventStage.Completed, "released for delivery"));

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("http://web:8080/internal/events/evt_1/status", request.RequestUri!.ToString());
        Assert.Equal("""{"eventId":"evt_1","stage":"Completed","detail":"released for delivery"}""", body);
    }

    [Fact]
    public async Task The_notifier_throws_on_failure_so_skail_retries_the_command()
    {
        var notifier = new HttpStatusNotifier(Client(StubHandler.Status(HttpStatusCode.ServiceUnavailable)));

        await Assert.ThrowsAsync<HttpRequestException>(() => notifier.NotifyAsync(new EventStatus("evt_1", EventStage.Started)));
    }
}
