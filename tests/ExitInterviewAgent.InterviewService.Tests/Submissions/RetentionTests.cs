using System.Net;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

/// <summary>Age-based purge with a fake clock (ADR-0019, ADR-0027, ADR-0028).</summary>
public sealed class RetentionTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<(string Sub, string Employer, string Id)> SubmitAsync()
    {
        var sub = TestRecords.NewSub();
        var record = TestRecords.Valid();
        var response = await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(record));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (sub, record["employerRef"]!.GetValue<string>(), record["interviewId"]!.GetValue<string>());
    }

    private Task<PurgeCounts> PurgeAsync() => _host.InScopeAsync(sp => sp.GetRequiredService<RetentionPurger>().PurgeAsync(CancellationToken.None));

    private static (int, int) Pair((int Records, int Receipts, int Ledger) c) => (c.Records, c.Receipts);

    private Task<(int Records, int Receipts, int Ledger)> CountsAsync(string? id = null) => _host.InScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<InterviewDbContext>();
        return (await db.Records.CountAsync(r => id == null || r.Id == id), await db.Receipts.CountAsync(r => id == null || r.RecordId == id), await db.SubmissionLedger.CountAsync());
    });

    [Fact]
    public async Task A_record_is_kept_until_its_age_is_reached_and_then_purged_with_its_receipt()
    {
        var submitted = await SubmitAsync(); // 2026-10-05 12:00, week of 2026-10-05

        _host.Clock.Set(new DateTimeOffset(2028, 10, 5, 12, 0, 0, TimeSpan.Zero)); // exactly 24 months later
        await PurgeAsync();
        Assert.Equal((1, 1), Pair(await CountsAsync(submitted.Id)));             // the bucket's END has not passed the cutoff: never purged early

        _host.Clock.Set(new DateTimeOffset(2028, 10, 13, 0, 0, 0, TimeSpan.Zero)); // the record's week (Mon 2026-10-05) ended 2026-10-12; plus 24 months
        var counts = await PurgeAsync();

        Assert.Equal(1, counts.Records);
        Assert.Equal((0, 0), Pair(await CountsAsync(submitted.Id)));
    }

    [Fact]
    public async Task The_maximum_record_age_is_configuration()
    {
        using var host = new TestHost(new() { ["Submission:Retention:RecordMaxAgeMonths"] = "1" });
        var record = TestRecords.Valid();
        await host.Client(host.WebToken(TestRecords.NewSub())).PostAsync("/api/v1/submissions", TestRecords.Json(record));

        host.Clock.Advance(TimeSpan.FromDays(20));
        Assert.Equal(0, (await host.InScopeAsync(sp => sp.GetRequiredService<RetentionPurger>().PurgeAsync(default))).Records);
        host.Clock.Advance(TimeSpan.FromDays(25));
        Assert.Equal(1, (await host.InScopeAsync(sp => sp.GetRequiredService<RetentionPurger>().PurgeAsync(default))).Records);
    }

    [Fact]
    public async Task Ledger_entries_are_purged_after_the_window_and_duplicate_suppression_ends_with_them()
    {
        var submitted = await SubmitAsync();
        var client = _host.Client(_host.WebToken(submitted.Sub));

        _host.Clock.Advance(TimeSpan.FromDays(300));
        await PurgeAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(submitted.Employer)))).StatusCode);

        _host.Clock.Advance(TimeSpan.FromDays(80)); // 380 days: past the 365-day window plus the bucket
        var counts = await PurgeAsync();

        Assert.Equal(1, counts.LedgerEntries);
        Assert.Equal(0, (await CountsAsync()).Ledger);
        // The consequence stated in the threat model: the same account may submit about the same employer again.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(submitted.Employer)))).StatusCode);
    }

    [Fact]
    public async Task Expired_tickets_are_swept_and_live_ones_are_kept()
    {
        var client = _host.Client(_host.WebToken(TestRecords.NewSub()));
        await client.PostAsync("/api/v1/tickets", null);
        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        await client.PostAsync("/api/v1/tickets", null);
        _host.Clock.Advance(TimeSpan.FromMinutes(8)); // the first (12:15) is gone, the second (12:30 rounded) is not

        var counts = await PurgeAsync();

        Assert.Equal(1, counts.Tickets);
        Assert.Equal(1, await _host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionTickets.CountAsync()));
    }

    [Fact]
    public async Task A_sweep_over_a_large_backlog_works_in_batches()
    {
        await _host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            for (var i = 0; i < 1100; i++)
            {
                db.SubmissionLedger.Add(new LedgerEntry { Id = Guid.NewGuid(), KeyId = "k", Tag = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), CreatedWeek = new DateOnly(2020, 1, 6) });
            }
            await db.SaveChangesAsync();
            return 0;
        });

        var counts = await PurgeAsync();

        Assert.Equal(1100, counts.LedgerEntries);
    }

    [Fact]
    public void The_sweep_runs_as_a_hosted_service_after_the_schema_is_ready()
    {
        Assert.Contains(_host.Services.GetServices<IHostedService>(), s => s is RetentionService);
    }
}
