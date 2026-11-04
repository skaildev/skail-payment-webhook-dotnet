using SkailPaymentWebhook.Contracts;
using SkailPaymentWebhook.Worker;

namespace SkailPaymentWebhook.Tests;

public class InvoiceSettlerTests
{
    private static readonly Invoice Open = new("P-1", 99.90m, null);

    private static Task<SettleResult> Settle(FakeInvoiceStore store, PaymentConfirmed payment) =>
        new InvoiceSettler(store).SettleAsync(payment);

    [Fact]
    public async Task An_open_invoice_with_the_same_amount_is_settled_and_marked_as_paid_by_the_event()
    {
        var store = new FakeInvoiceStore(Open);

        var result = await Settle(store, new("evt_1", "P-1", 99.90m));

        Assert.Equal(new SettleResult(true, null), result);
        Assert.Equal(("P-1", "evt_1"), Assert.Single(store.Paid));
    }

    [Fact]
    public async Task A_different_amount_is_a_result_and_nothing_is_marked_as_paid()
    {
        var store = new FakeInvoiceStore(new Invoice("P-2", 250.00m, null));

        var result = await Settle(store, new("evt_2", "P-2", 200m));

        Assert.False(result.Done);
        Assert.Equal("paid R$ 200.00, invoice R$ 250.00", result.Reason);
        Assert.Empty(store.Paid);
    }

    [Fact]
    public async Task A_missing_invoice_is_a_result()
    {
        var store = new FakeInvoiceStore();

        var result = await Settle(store, new("evt_9", "P-9", 10m));

        Assert.False(result.Done);
        Assert.Equal("no invoice for order P-9", result.Reason);
        Assert.Empty(store.Paid);
    }

    [Fact]
    public async Task An_invoice_paid_by_another_event_is_a_result()
    {
        var store = new FakeInvoiceStore(new Invoice("P-3", 80m, "evt_old"));

        var result = await Settle(store, new("evt_3", "P-3", 80m));

        Assert.False(result.Done);
        Assert.Equal(InvoiceSettler.AlreadyPaidByAnother, result.Reason);
        Assert.Empty(store.Paid);
    }

    [Fact]
    public async Task A_retry_of_the_same_settlement_succeeds_without_writing_again()
    {
        // the first attempt wrote the payment but the command did not finish: the invoice is already paid by this event
        var store = new FakeInvoiceStore(new Invoice("P-1", 99.90m, "evt_1"));

        var result = await Settle(store, new("evt_1", "P-1", 99.90m));

        Assert.Equal(new SettleResult(true, null), result);
    }

    [Fact]
    public async Task A_retry_succeeds_even_if_the_amount_was_different_from_the_invoice()
    {
        // already settled by this event: the amount was checked on the first attempt
        var store = new FakeInvoiceStore(new Invoice("P-1", 99.90m, "evt_1"));

        var result = await Settle(store, new("evt_1", "P-1", 1m));

        Assert.True(result.Done);
    }

    [Fact]
    public async Task When_the_store_refuses_the_write_because_another_event_got_there_first_it_is_a_result()
    {
        var store = new FakeInvoiceStore(Open) { RefuseMarkPaid = true };

        var result = await Settle(store, new("evt_1", "P-1", 99.90m));

        Assert.False(result.Done);
        Assert.Equal(InvoiceSettler.AlreadyPaidByAnother, result.Reason);
    }

    [Fact]
    public async Task The_amount_is_formatted_the_same_way_in_any_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var result = await Settle(new FakeInvoiceStore(new Invoice("P-2", 1250.00m, null)), new("evt_2", "P-2", 200m));

            Assert.Equal("paid R$ 200.00, invoice R$ 1,250.00", result.Reason);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
