using ExitInterviewAgent.InterviewService.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The ledger rules on the InMemory store (W3): a purchase is applied once per provider event, a session takes one credit once,
/// a failed session gives it back once, and parallel requests cannot spend the last credit twice. The PostgreSQL run of the same
/// races, with the database's own unique indexes, is in <see cref="PostgresCreditTests"/>.
/// </summary>
public sealed class CreditLedgerTests : IDisposable
{
    private readonly BillingHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<CreditLedger> LedgerAsync()
    {
        await _host.WaitReadyAsync();
        return _host.Services.GetRequiredService<CreditLedger>();
    }

    internal static PaymentEvent Purchase(string eventId, string account, int quantity) =>
        new(eventId, PaymentEventKind.Purchase, account, quantity, 1000L * quantity, "pln");

    [Fact]
    public async Task A_purchase_event_is_applied_once_however_often_it_is_delivered()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();
        var evt = Purchase(BillingTestIds.EventId(), account, 3);

        var first = await ledger.RecordPurchaseAsync(evt, CancellationToken.None);
        var second = await ledger.RecordPurchaseAsync(evt, CancellationToken.None);
        var third = await ledger.RecordPurchaseAsync(evt, CancellationToken.None);

        Assert.Equal(PurchaseOutcome.Applied, first);
        Assert.Equal(PurchaseOutcome.Replay, second);
        Assert.Equal(PurchaseOutcome.Replay, third);
        Assert.Equal(3, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task With_no_credit_a_consume_is_refused_and_nothing_is_written()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();

        Assert.False(await ledger.ConsumeAsync(account, "sess-none", CancellationToken.None));
        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task A_session_takes_one_credit_and_a_second_consume_for_the_same_session_takes_nothing_more()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();
        await ledger.RecordPurchaseAsync(Purchase(BillingTestIds.EventId(), account, 2), CancellationToken.None);

        Assert.True(await ledger.ConsumeAsync(account, "sess-a", CancellationToken.None));
        Assert.False(await ledger.ConsumeAsync(account, "sess-a", CancellationToken.None));

        Assert.Equal(1, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task A_refund_returns_the_credit_once_and_only_for_a_session_that_consumed_one()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();
        await ledger.RecordPurchaseAsync(Purchase(BillingTestIds.EventId(), account, 1), CancellationToken.None);
        Assert.True(await ledger.ConsumeAsync(account, "sess-r", CancellationToken.None));

        await ledger.RefundAsync(account, "sess-r", CancellationToken.None);
        await ledger.RefundAsync(account, "sess-r", CancellationToken.None);
        await ledger.RefundAsync(account, "sess-never-consumed", CancellationToken.None);

        Assert.Equal(1, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task Parallel_consumes_of_the_last_credit_yield_exactly_one_success()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();
        await ledger.RecordPurchaseAsync(Purchase(BillingTestIds.EventId(), account, 1), CancellationToken.None);

        var results = await Race(20, i => ledger.ConsumeAsync(account, "sess-race-" + i, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task Parallel_deliveries_of_one_purchase_add_its_credits_once()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();
        var evt = Purchase(BillingTestIds.EventId(), account, 2);

        var outcomes = await Race(12, _ => ledger.RecordPurchaseAsync(evt, CancellationToken.None));

        Assert.Equal(1, outcomes.Count(o => o == PurchaseOutcome.Applied));
        Assert.Equal(2, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task An_account_sees_only_its_own_balance()
    {
        var ledger = await LedgerAsync();
        var mine = BillingTestIds.Account();
        var other = BillingTestIds.Account();
        await ledger.RecordPurchaseAsync(Purchase(BillingTestIds.EventId(), other, 5), CancellationToken.None);

        Assert.Equal(0, await ledger.BalanceAsync(mine, CancellationToken.None));
        Assert.Equal(5, await ledger.BalanceAsync(other, CancellationToken.None));
    }

    private static async Task<T[]> Race<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await start.Task; return await work(i); })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
