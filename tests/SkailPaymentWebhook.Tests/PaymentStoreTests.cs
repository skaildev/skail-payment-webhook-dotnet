using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Web.Store;

namespace SkailPaymentWebhook.Tests;

public class PaymentStoreTests
{
    private static readonly PaymentConfirmed Payment = new("evt_1", "P-1", 99.90m);
    private static readonly Guid TaskId = ExecutionIds.FromEventId("evt_1");

    [Fact]
    public void Starts_with_the_seeded_invoices()
    {
        var invoices = new PaymentStore().Snapshot().Invoices;

        Assert.Equal(["P-1", "P-2", "P-3"], invoices.Select(i => i.OrderId));
        Assert.Equal("evt_old", invoices.Single(i => i.OrderId == "P-3").PaidByEvent);
        Assert.All(invoices, i => Assert.Equal(0, i.Releases));
    }

    [Fact]
    public void The_same_event_delivered_again_is_one_row_with_more_deliveries()
    {
        var store = new PaymentStore();

        store.RecordDelivery(Payment, TaskId);
        store.RecordDelivery(Payment, TaskId);

        var row = Assert.Single(store.Snapshot().Events);
        Assert.Equal(2, row.Deliveries);
        Assert.Equal(TaskId, row.TaskId);
    }

    [Fact]
    public void A_trigger_that_skail_did_not_accept_shows_as_not_accepted_until_a_resend_works()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);

        store.MarkNotAccepted("evt_1");
        Assert.Equal(EventState.NotAccepted, store.Snapshot().Events.Single().State);

        store.RecordDelivery(Payment, TaskId);
        store.MarkAccepted("evt_1");
        Assert.Equal(EventState.Received, store.Snapshot().Events.Single().State);
    }

    [Fact]
    public void A_failed_resend_does_not_undo_an_event_that_skail_already_accepted()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);
        store.MarkAccepted("evt_1");
        store.ApplyStatus(new EventStatus("evt_1", EventStage.Started));

        store.RecordDelivery(Payment, TaskId);
        store.MarkNotAccepted("evt_1");

        Assert.Equal(EventState.Running, store.Snapshot().Events.Single().State);
    }

    [Fact]
    public void MarkPaid_is_idempotent_for_the_same_event()
    {
        var store = new PaymentStore();

        Assert.Equal(MarkPaidResult.Ok, store.MarkPaid("P-1", "evt_1"));
        Assert.Equal(MarkPaidResult.Ok, store.MarkPaid("P-1", "evt_1"));
        Assert.Equal("evt_1", store.GetInvoice("P-1")!.PaidByEvent);
    }

    [Fact]
    public void MarkPaid_is_refused_when_another_event_paid_the_invoice()
    {
        var store = new PaymentStore();

        Assert.Equal(MarkPaidResult.PaidByAnother, store.MarkPaid("P-3", "evt_1"));
        Assert.Equal("evt_old", store.GetInvoice("P-3")!.PaidByEvent);
    }

    [Fact]
    public void MarkPaid_of_an_unknown_order_is_not_found()
    {
        Assert.Equal(MarkPaidResult.NotFound, new PaymentStore().MarkPaid("P-9", "evt_1"));
    }

    [Fact]
    public void Releasing_the_same_event_twice_counts_once()
    {
        var store = new PaymentStore();

        store.Release("P-1", "evt_1");
        store.Release("P-1", "evt_1");

        Assert.Equal(1, store.Snapshot().Invoices.Single(i => i.OrderId == "P-1").Releases);
    }

    [Fact]
    public void A_review_for_the_same_event_is_opened_once()
    {
        var store = new PaymentStore();
        var review = new ReviewRequest("evt_2", "P-2", "paid R$ 200.00, invoice R$ 250.00");

        store.AddReview(review);
        store.AddReview(review);

        Assert.Equal(new ReviewView("evt_2", "P-2", "paid R$ 200.00, invoice R$ 250.00"), Assert.Single(store.Snapshot().Reviews));
    }

    [Fact]
    public void Status_moves_the_event_from_received_to_running_to_released()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);

        store.ApplyStatus(new EventStatus("evt_1", EventStage.Started));
        Assert.Equal(EventState.Running, store.Snapshot().Events.Single().State);

        store.ApplyStatus(new EventStatus("evt_1", EventStage.Completed, PaymentResults.Released));
        var row = store.Snapshot().Events.Single();
        Assert.Equal(EventState.Released, row.State);
        Assert.Equal(PaymentResults.Released, row.Result);
    }

    [Fact]
    public void A_pending_result_shows_the_event_as_pending_with_the_reason()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);

        store.ApplyStatus(new EventStatus("evt_1", EventStage.Completed, PaymentResults.Pending("no invoice for order P-9")));

        var row = store.Snapshot().Events.Single();
        Assert.Equal(EventState.Pending, row.State);
        Assert.Equal("pending: no invoice for order P-9", row.Result);
    }

    [Fact]
    public void A_status_after_the_end_or_for_an_unknown_event_is_ignored()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);
        store.ApplyStatus(new EventStatus("evt_1", EventStage.Completed, PaymentResults.Released));

        Assert.False(store.ApplyStatus(new EventStatus("evt_1", EventStage.Started)));
        Assert.False(store.ApplyStatus(new EventStatus("evt_unknown", EventStage.Started)));
        Assert.Equal(EventState.Released, store.Snapshot().Events.Single().State);
    }

    [Fact]
    public void A_failed_execution_shows_the_error()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);

        store.ApplyStatus(new EventStatus("evt_1", EventStage.Failed, "boom"));

        var row = store.Snapshot().Events.Single();
        Assert.Equal((EventState.Failed, "boom"), (row.State, row.Result));
    }

    [Fact]
    public void Reset_goes_back_to_the_seed()
    {
        var store = new PaymentStore();
        store.RecordDelivery(Payment, TaskId);
        store.MarkPaid("P-1", "evt_1");
        store.Release("P-1", "evt_1");
        store.AddReview(new ReviewRequest("evt_2", "P-2", "x"));

        store.Reset();

        var view = store.Snapshot();
        Assert.Empty(view.Events);
        Assert.Empty(view.Reviews);
        Assert.Null(view.Invoices.Single(i => i.OrderId == "P-1").PaidByEvent);
        Assert.Equal(0, view.Invoices.Single(i => i.OrderId == "P-1").Releases);
    }

    [Fact]
    public async Task Subscribers_get_the_latest_snapshot_after_each_change()
    {
        var store = new PaymentStore();
        using var subscription = store.Subscribe();

        store.RecordDelivery(Payment, TaskId);
        var first = await subscription.Reader.ReadAsync();
        store.ApplyStatus(new EventStatus("evt_1", EventStage.Started));
        var second = await subscription.Reader.ReadAsync();

        Assert.Equal(EventState.Received, first.Events.Single().State);
        Assert.Equal(EventState.Running, second.Events.Single().State);
    }
}
