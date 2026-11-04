using System.Text.Json;
using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Worker;

namespace SkailPaymentWebhook.Tests;

public class ContractsTests
{
    [Fact]
    public void The_same_event_id_always_gives_the_same_execution_id()
    {
        Assert.Equal(ExecutionIds.FromEventId("evt_1"), ExecutionIds.FromEventId("evt_1"));
    }

    [Fact]
    public void Different_event_ids_give_different_execution_ids()
    {
        Assert.NotEqual(ExecutionIds.FromEventId("evt_1"), ExecutionIds.FromEventId("evt_2"));
    }

    [Theory]
    [InlineData("evt_1", "25679aad-4e35-5329-a02b-5223f8bc34f2")]
    [InlineData("evt_ação", "a39cdf55-66a3-5499-9c96-a3c2916d3c7b")]
    public void Execution_id_is_the_UUID_v5_of_the_event_id_in_the_application_namespace(string eventId, string expected)
    {
        Assert.Equal(Guid.Parse(expected), ExecutionIds.FromEventId(eventId));
    }

    [Fact]
    public void Trigger_arguments_are_written_in_PascalCase_for_the_skail_runtime()
    {
        var json = JsonSerializer.Serialize(new object[] { new PaymentConfirmed("evt_1", "P-1", 99.9m) }, Json.SkailArguments);

        Assert.Equal("""[{"EventId":"evt_1","OrderId":"P-1","Amount":99.9}]""", json);
    }

    [Fact]
    public void The_gateway_payload_is_read_in_camelCase()
    {
        var payment = JsonSerializer.Deserialize<PaymentConfirmed>(
            """{"eventId":"evt_1","orderId":"P-1","amount":99.9}""", Json.Options);

        Assert.Equal(new PaymentConfirmed("evt_1", "P-1", 99.9m), payment);
    }

    [Fact]
    public void Enums_travel_as_strings_between_web_and_worker()
    {
        var json = JsonSerializer.Serialize(new EventStatus("evt_1", EventStage.Completed, "x"), Json.Options);

        Assert.Contains("\"stage\":\"Completed\"", json);
    }

    [Fact]
    public void The_trigger_function_name_is_the_name_of_the_worker_method()
    {
        Assert.Equal(nameof(PaymentFunctions.ReleaseOrder), SkailFunctions.ReleaseOrder);
    }
}
