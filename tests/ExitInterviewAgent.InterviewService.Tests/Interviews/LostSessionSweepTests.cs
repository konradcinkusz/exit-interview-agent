using System.Net.Http.Json;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Tests.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Interviews;

/// <summary>
/// The startup sweep (W11, ADR-0077 implementation notes): a credit taken by a session that no longer exists (a restart) comes
/// back once, with its settlement row. A settled session, a session inside the idle window, and a session this process still
/// holds are left alone. The "restart" is a second host on the same database, which starts with an empty session store.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class LostSessionSweepTests
{
    private static async Task<(string Account, string Session)> StartSessionAsync(BillingHost host, string account)
    {
        await host.WaitReadyAsync();
        var client = host.As(account);
        await PurchaseAsync(host, account, 1);
        var response = await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (account, body.GetProperty("id").GetString()!);
    }

    private static async Task PurchaseAsync(BillingHost host, string account, int credits)
    {
        var ledger = host.Services.GetRequiredService<CreditLedger>();
        await ledger.RecordPurchaseAsync(CreditLedgerTests.Purchase(BillingTestIds.EventId(), account, credits), CancellationToken.None);
    }

    private static LostSessionSweep Sweep(BillingHost host) => host.Services.GetRequiredService<LostSessionSweep>();

    [Fact]
    public async Task After_a_restart_the_credit_of_the_lost_session_comes_back_exactly_once()
    {
        var account = BillingTestIds.Account();
        string session;
        await using (var before = new BillingHost())
        {
            (_, session) = await StartSessionAsync(before, account);
        }
        // The first process is gone with its session. The second starts two hours later: the consume is old enough to count.
        await using var after = new BillingHost();
        after.Clock.Advance(TimeSpan.FromHours(2));
        await after.WaitReadyAsync();

        await Sweep(after).StartupRun;
        Assert.Equal(0, await Sweep(after).RunOnceAsync(CancellationToken.None));

        var ledger = after.Services.GetRequiredService<CreditLedger>();
        Assert.Equal(1, await ledger.BalanceAsync(account, CancellationToken.None));
        Assert.False(await ledger.SettleAsync(account, session, SessionOutcome.Lost, CancellationToken.None));
    }

    [Fact]
    public async Task A_settled_session_is_never_refunded_by_the_sweep()
    {
        var account = BillingTestIds.Account();
        await using var host = new BillingHost();
        var (_, session) = await StartSessionAsync(host, account);
        var ledger = host.Services.GetRequiredService<CreditLedger>();
        Assert.True(await ledger.SettleAsync(account, session, SessionOutcome.Completed, CancellationToken.None));
        host.Clock.Advance(TimeSpan.FromHours(3));

        await Sweep(host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, await ledger.BalanceAsync(account, CancellationToken.None));
        Assert.False(await ledger.SettleAsync(account, session, SessionOutcome.Lost, CancellationToken.None), "the session was settled, not lost");
    }

    [Fact]
    public async Task A_session_inside_the_idle_window_is_not_refunded()
    {
        var account = BillingTestIds.Account();
        await using var host = new BillingHost();
        await StartSessionAsync(host, account);
        host.Clock.Advance(TimeSpan.FromMinutes(10));

        await Sweep(host).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, await host.Services.GetRequiredService<CreditLedger>().BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task A_session_this_process_still_holds_is_not_refunded_even_when_old()
    {
        var account = BillingTestIds.Account();
        await using var host = new BillingHost();
        await StartSessionAsync(host, account);
        // Old by the clock, but still in this process's store (no request has expired it yet): the sweep must leave it.
        host.Clock.Advance(TimeSpan.FromHours(3));

        await Sweep(host).RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, await host.Services.GetRequiredService<CreditLedger>().BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task Parallel_sweeps_refund_a_lost_session_once()
    {
        var account = BillingTestIds.Account();
        await using (var before = new BillingHost())
        {
            await StartSessionAsync(before, account);
        }
        // The startup run of this host sees nothing old yet; the clock moves only after it, so the race below is the only sweeper.
        await using var after = new BillingHost();
        await after.WaitReadyAsync();
        Assert.Equal(0, await Sweep(after).StartupRun);
        after.Clock.Advance(TimeSpan.FromHours(2));

        var counts = await Race(8, _ => Sweep(after).RunOnceAsync(CancellationToken.None));

        Assert.True(counts.Sum() >= 1, "the race refunded nothing");
        Assert.Equal(1, await after.Services.GetRequiredService<CreditLedger>().BalanceAsync(account, CancellationToken.None));
    }

    [Fact]
    public async Task The_sweep_logs_counts_only_never_the_session_or_the_account()
    {
        var account = BillingTestIds.Account();
        string session;
        await using (var before = new BillingHost())
        {
            (_, session) = await StartSessionAsync(before, account);
        }
        await using var after = new BillingHost();
        after.Clock.Advance(TimeSpan.FromHours(2));
        await after.WaitReadyAsync();
        await Sweep(after).StartupRun;

        var logged = string.Join('\n', after.Logs.Lines);
        Assert.DoesNotContain(session, logged);
        Assert.DoesNotContain(account, logged);
    }

    private static async Task<T[]> Race<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await start.Task; return await work(i); })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
