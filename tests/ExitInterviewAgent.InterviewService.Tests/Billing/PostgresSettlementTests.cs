using System.Net.Http.Json;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The settlement rules on a real PostgreSQL server (TEST_POSTGRES_CONNECTION, W11). The migration creates the settlement table
/// with its unique index; two service instances on one database run the startup sweep at the same time and refund a lost session
/// once. Without the variable these are SKIPPED and reported as not run.
/// </summary>
public sealed class PostgresSettlementTests
{
    [PostgresFact]
    public async Task The_migration_creates_the_settlement_table_with_a_unique_session_id()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        await using var host = new BillingHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();

        var indexes = await db.Database.SqlQueryRaw<string>(
                "SELECT indexdef FROM pg_indexes WHERE tablename = 'SessionSettlements' AND indexname = 'IX_SessionSettlements_SessionId'")
            .ToListAsync();

        var definition = Assert.Single(indexes);
        Assert.Contains("UNIQUE", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SessionId", definition);
    }

    [PostgresFact]
    public async Task Parallel_settlements_of_one_session_on_postgres_refund_at_most_once()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        await using var host = new BillingHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var ledger = host.Services.GetRequiredService<CreditLedger>();
        var account = BillingTestIds.Account();
        var session = "sess-pg-settle-" + Guid.NewGuid().ToString("N");
        await ledger.RecordPurchaseAsync(CreditLedgerTests.Purchase(BillingTestIds.EventId(), account, 1), CancellationToken.None);
        Assert.True(await ledger.ConsumeAsync(account, session, CancellationToken.None));

        var results = await Race(16, _ => ledger.SettleAsync(account, session, SessionOutcome.Failed, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, await ledger.BalanceAsync(account, CancellationToken.None));
    }

    [PostgresFact]
    public async Task Two_instances_sweeping_one_database_refund_a_lost_session_once()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        var account = BillingTestIds.Account();
        await using (var before = new BillingHost(postgres: pg.ConnectionString))
        {
            await PurchaseAndStartAsync(before, account);
        }

        // Two instances start on the same database at the consume's own time, so their startup runs find nothing old enough. They
        // start one after the other (the schema is migrated by the first; concurrent migrations are a separate, existing matter), and
        // then their clocks move two hours and both sweep at the same moment.
        await using var first = new BillingHost(postgres: pg.ConnectionString);
        await first.WaitReadyAsync();
        await using var second = new BillingHost(postgres: pg.ConnectionString);
        await second.WaitReadyAsync();
        Assert.Equal(0, await first.Services.GetRequiredService<LostSessionSweep>().StartupRun);
        Assert.Equal(0, await second.Services.GetRequiredService<LostSessionSweep>().StartupRun);
        first.Clock.Advance(TimeSpan.FromHours(2));
        second.Clock.Advance(TimeSpan.FromHours(2));

        var refunds = await Task.WhenAll(
            first.Services.GetRequiredService<LostSessionSweep>().RunOnceAsync(CancellationToken.None),
            second.Services.GetRequiredService<LostSessionSweep>().RunOnceAsync(CancellationToken.None));

        Assert.Equal(1, refunds.Sum());
        Assert.Equal(1, await first.Services.GetRequiredService<CreditLedger>().BalanceAsync(account, CancellationToken.None));
    }

    private static async Task PurchaseAndStartAsync(BillingHost host, string account)
    {
        await host.WaitReadyAsync();
        var ledger = host.Services.GetRequiredService<CreditLedger>();
        await ledger.RecordPurchaseAsync(CreditLedgerTests.Purchase(BillingTestIds.EventId(), account, 1), CancellationToken.None);
        var response = await host.As(account).PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        _ = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
    }

    private static async Task<T[]> Race<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await start.Task; return await work(i); })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
