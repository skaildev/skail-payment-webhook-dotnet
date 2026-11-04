using System.Net;
using System.Text.Json;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Web.Skail;

namespace SkailPaymentWebhook.Tests;

public class GatewayTests
{
    private static readonly SkailOptions Opts = new()
    {
        BaseUrl = "https://skail.example.test/",
        Namespace = "tenant-1/env-1",
        Key = "env-key",
        Workload = "payment-webhook:v0.0.1",
    };

    private static readonly PaymentConfirmed Payment = new("evt_1", "P-1", 99.90m);
    private static readonly Guid TaskId = ExecutionIds.FromEventId("evt_1");

    private static SkailApiGateway Gateway(StubHandler handler, SkailOptions? options = null) =>
        new(new HttpClient(handler), options ?? Opts, retryDelay: _ => TimeSpan.Zero);

    [Fact]
    public async Task Trigger_posts_the_payment_to_the_function_route_with_the_task_id()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);

        await Gateway(handler).TriggerAsync(TaskId, Payment);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://skail.example.test/api/v1/trigger/payment-webhook/v0.0.1/ReleaseOrder/{TaskId}", request.RequestUri!.ToString());
        Assert.Equal("env-key", request.Headers.GetValues("skail-key").Single());
        Assert.Equal("Bearer env-key", request.Headers.GetValues("Authorization").Single());
        Assert.Equal("tenant-1/env-1", request.Headers.GetValues("Skail-namespace").Single());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
        Assert.Equal("""[{"EventId":"evt_1","OrderId":"P-1","Amount":99.90}]""", body);
    }

    [Fact]
    public async Task The_body_is_an_array_with_one_argument_in_PascalCase()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);

        await Gateway(handler).TriggerAsync(TaskId, Payment);

        using var doc = JsonDocument.Parse(Assert.Single(handler.Calls).Body);
        var argument = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("evt_1", argument.GetProperty("EventId").GetString());
        Assert.False(argument.TryGetProperty("eventId", out _));
    }

    [Fact]
    public void Options_read_the_api_base_url_from_SKAIL_BASE_URL()
    {
        var names = new[] { "SKAIL_BASE_URL", "SKAIL_NAMESPACE", "SKAIL_KEY", "SKAIL_WORKLOAD" };
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("SKAIL_BASE_URL", "https://env.example.test");
            Environment.SetEnvironmentVariable("SKAIL_NAMESPACE", "t/e");
            Environment.SetEnvironmentVariable("SKAIL_KEY", "k");
            Environment.SetEnvironmentVariable("SKAIL_WORKLOAD", "w:v1");

            var options = SkailOptions.FromEnvironment();

            Assert.Equal("https://env.example.test", options.BaseUrl);
            Assert.Equal(("t/e", "k", "w:v1"), (options.Namespace, options.Key, options.Workload));
        }
        finally
        {
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Transient_failures_are_retried_with_the_same_id_and_body(HttpStatusCode failure)
    {
        var handler = new StubHandler((_, n) => new HttpResponseMessage(n < 3 ? failure : HttpStatusCode.OK));

        await Gateway(handler).TriggerAsync(TaskId, Payment);

        Assert.Equal(3, handler.Calls.Count);
        Assert.Single(handler.Calls.Select(c => c.Request.RequestUri).Distinct());
        Assert.Single(handler.Calls.Select(c => c.Body).Distinct());
    }

    [Fact]
    public async Task A_network_error_is_retried()
    {
        var handler = new StubHandler((_, n) => n < 2 ? throw new HttpRequestException("connection refused") : new HttpResponseMessage(HttpStatusCode.OK));

        await Gateway(handler).TriggerAsync(TaskId, Payment);

        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task Gives_up_after_the_retry_budget_and_throws()
    {
        var handler = StubHandler.Status(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<SkailGatewayException>(() => Gateway(handler).TriggerAsync(TaskId, Payment));

        Assert.Equal(SkailApiGateway.MaxAttempts, handler.Calls.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Auth_failures_are_not_retried_and_say_what_to_check(HttpStatusCode status)
    {
        var handler = StubHandler.Status(status);

        var error = await Assert.ThrowsAsync<SkailGatewayException>(() => Gateway(handler).TriggerAsync(TaskId, Payment));

        Assert.Single(handler.Calls);
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.Contains("SKAIL_KEY", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("payment-webhook")]
    [InlineData(":v1")]
    [InlineData("payment-webhook:")]
    public async Task Invalid_workload_fails_before_any_call(string workload)
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);
        var gateway = Gateway(handler, new SkailOptions { BaseUrl = Opts.BaseUrl, Namespace = "n", Key = "k", Workload = workload });

        var error = await Assert.ThrowsAsync<SkailGatewayException>(() => gateway.TriggerAsync(TaskId, Payment));

        Assert.Contains("SKAIL_WORKLOAD", error.Message);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Missing_base_url_fails_before_any_call()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);
        var gateway = Gateway(handler, new SkailOptions { Namespace = "n", Key = "k", Workload = "a:b" });

        var error = await Assert.ThrowsAsync<SkailGatewayException>(() => gateway.TriggerAsync(TaskId, Payment));

        Assert.Contains("SKAIL_BASE_URL", error.Message);
        Assert.Empty(handler.Calls);
    }
}
