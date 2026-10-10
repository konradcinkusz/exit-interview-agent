using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The credit races against a real PostgreSQL server (TEST_POSTGRES_CONNECTION), where the database's own unique indexes and
/// serializable transactions decide, not only the in-process lock. Without the variable these are SKIPPED and reported as not run.
/// </summary>
public sealed class PostgresCreditTests
{
    [PostgresFact]
    public async Task Parallel_consumes_of_the_last_credit_yield_exactly_one_success_on_postgres()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new BillingHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var ledger = host.Services.GetRequiredService<CreditLedger>();
        var account = BillingTestIds.Account();
        await ledger.RecordPurchaseAsync(CreditLedgerTests.Purchase(BillingTestIds.EventId(), account, 1), CancellationToken.None);

        var results = await Race(24, i => ledger.ConsumeAsync(account, "sess-pg-" + i, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [PostgresFact]
    public async Task Parallel_deliveries_of_one_purchase_add_its_credits_once_on_postgres()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new BillingHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var ledger = host.Services.GetRequiredService<CreditLedger>();
        var account = BillingTestIds.Account();
        var evt = CreditLedgerTests.Purchase(BillingTestIds.EventId(), account, 4);

        var outcomes = await Race(16, _ => ledger.RecordPurchaseAsync(evt, CancellationToken.None));

        Assert.Equal(1, outcomes.Count(o => o == PurchaseOutcome.Applied));
        Assert.Equal(4, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [PostgresFact]
    public async Task The_unique_indexes_refuse_a_second_purchase_of_one_event_and_a_second_refund_of_one_session()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new BillingHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        var account = BillingTestIds.Account();
        var eventId = BillingTestIds.EventId();
        var week = new DateOnly(2026, 10, 5);

        db.PaymentEvents.Add(new PaymentEventRow { Id = Guid.NewGuid(), ProviderEventId = eventId, Kind = "purchase", AccountRef = account, Quantity = 1, AmountMinorUnits = 1000, Currency = "pln", ReceivedWeek = week });
        await db.SaveChangesAsync();
        db.PaymentEvents.Add(new PaymentEventRow { Id = Guid.NewGuid(), ProviderEventId = eventId, Kind = "purchase", AccountRef = account, Quantity = 1, AmountMinorUnits = 1000, Currency = "pln", ReceivedWeek = week });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.CreditEntries.Add(new CreditEntry { Id = Guid.NewGuid(), AccountRef = account, Delta = 1, Reason = CreditReason.Refund, Reference = "sess-x", CreatedWeek = week });
        await db.SaveChangesAsync();
        db.CreditEntries.Add(new CreditEntry { Id = Guid.NewGuid(), AccountRef = account, Delta = 1, Reason = CreditReason.Refund, Reference = "sess-x", CreatedWeek = week });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task<T[]> Race<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await start.Task; return await work(i); })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
