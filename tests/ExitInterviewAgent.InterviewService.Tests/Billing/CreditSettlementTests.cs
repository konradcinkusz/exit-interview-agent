using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The settlement row (W11, ADR-0077 implementation notes): one row per session, written when the session ends. Its unique
/// session id decides which writer settles it; a failed or lost session gets its credit back in the same transaction; any other
/// outcome keeps the credit. The InMemory store is shared by name in one process, so these tests run in the billing collection.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class CreditSettlementTests : IDisposable
{
    private readonly BillingHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<CreditLedger> LedgerAsync()
    {
        await _host.WaitReadyAsync();
        return _host.Services.GetRequiredService<CreditLedger>();
    }

    private static async Task<(string Account, string Session)> ConsumedAsync(CreditLedger ledger)
    {
        var account = BillingTestIds.Account();
        var session = "sess-" + Guid.NewGuid().ToString("N");
        await ledger.RecordPurchaseAsync(CreditLedgerTests.Purchase(BillingTestIds.EventId(), account, 1), CancellationToken.None);
        Assert.True(await ledger.ConsumeAsync(account, session, CancellationToken.None));
        return (account, session);
    }

    [Fact]
    public async Task A_completed_session_is_settled_once_and_keeps_its_credit()
    {
        var ledger = await LedgerAsync();
        var (account, session) = await ConsumedAsync(ledger);

        Assert.True(await ledger.SettleAsync(account, session, SessionOutcome.Completed, CancellationToken.None));
        Assert.False(await ledger.SettleAsync(account, session, SessionOutcome.Completed, CancellationToken.None));

        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Theory]
    [InlineData(SessionOutcome.Withdrawn)]
    [InlineData(SessionOutcome.Stopped)]
    public async Task A_withdrawn_or_stopped_session_keeps_its_credit(SessionOutcome outcome)
    {
        var ledger = await LedgerAsync();
        var (account, session) = await ConsumedAsync(ledger);

        Assert.True(await ledger.SettleAsync(account, session, outcome, CancellationToken.None));

        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_session_gets_its_credit_back_once_and_a_later_settlement_adds_nothing()
    {
        var ledger = await LedgerAsync();
        var (account, session) = await ConsumedAsync(ledger);

        Assert.True(await ledger.SettleAsync(account, session, SessionOutcome.Failed, CancellationToken.None));
        Assert.False(await ledger.SettleAsync(account, session, SessionOutcome.Failed, CancellationToken.None));
        Assert.False(await ledger.SettleAsync(account, session, SessionOutcome.Lost, CancellationToken.None));

        Assert.Equal(1, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task A_settlement_of_a_session_that_never_took_a_credit_writes_no_refund()
    {
        var ledger = await LedgerAsync();
        var account = BillingTestIds.Account();

        Assert.True(await ledger.SettleAsync(account, "sess-never-consumed-" + Guid.NewGuid().ToString("N"), SessionOutcome.Failed, CancellationToken.None));

        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task Parallel_settlements_of_one_session_write_exactly_one_settlement_and_at_most_one_refund()
    {
        var ledger = await LedgerAsync();
        var (account, session) = await ConsumedAsync(ledger);

        var results = await Race(12, _ => ledger.SettleAsync(account, session, SessionOutcome.Failed, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task The_unsettled_query_lists_only_consumes_older_than_the_cutoff_and_not_excluded()
    {
        var ledger = await LedgerAsync();
        var (account, session) = await ConsumedAsync(ledger);
        var future = DateTimeOffset.UtcNow.AddYears(1);
        var past = DateTimeOffset.UtcNow.AddYears(-1);

        var notYetOld = await ledger.UnsettledConsumesAsync(past, [], 100, CancellationToken.None);
        var old = await ledger.UnsettledConsumesAsync(future, [], 100, CancellationToken.None);
        var excluded = await ledger.UnsettledConsumesAsync(future, [session], 100, CancellationToken.None);

        Assert.DoesNotContain(notYetOld, c => c.SessionId == session);
        Assert.Contains(old, c => c.SessionId == session && c.AccountRef == account);
        Assert.DoesNotContain(excluded, c => c.SessionId == session);
    }

    private static async Task<T[]> Race<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await start.Task; return await work(i); })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}

/// <summary>
/// The one xUnit collection for the tests that share the InMemory interview database by name (the host cannot give it another
/// name). Its members run one at a time, so a sweep with a moved clock cannot refund another test's open session.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BillingCollection
{
    public const string Name = "InMemory interview database";
}
