using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Submissions;

public sealed record PurgeCounts(int Records, int LedgerEntries, int Tickets);

/// <summary>
/// Age-based purge of records, ledger entries and expired tickets (brief section 6, ADR-0019, ADR-0028). Cutoffs use the
/// END of a row's week bucket, so a row is never removed before its age is reached. Returns counts only: what was
/// removed is never logged. Batched so one sweep never holds a long lock; works on every supported provider.
/// </summary>
public sealed class RetentionPurger(InterviewDbContext db, TimeProvider time, IOptions<SubmissionOptions> options)
{
    private const int Batch = 500;

    public async Task<PurgeCounts> PurgeAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var settings = options.Value.Retention;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // A bucket is purgeable when its end (exclusive) is on or before the cutoff date.
        var recordCutoff = today.AddMonths(-settings.RecordMaxAgeMonths);
        var ledgerCutoff = today.AddDays(-settings.LedgerWindowDays);

        var records = 0;
        while (true)
        {
            var ids = await db.Records.Where(r => r.CreatedWeek <= recordCutoff.AddDays(-7)).OrderBy(r => r.CreatedWeek)
                .Select(r => r.Id).Take(Batch).ToListAsync(ct);
            if (ids.Count == 0)
            {
                break;
            }
            // Receipts are removed with their records explicitly: the in-memory provider has no database cascade.
            db.Receipts.RemoveRange(await db.Receipts.Where(r => ids.Contains(r.RecordId)).ToListAsync(ct));
            db.Records.RemoveRange(await db.Records.Where(r => ids.Contains(r.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            records += ids.Count;
        }

        var ledger = 0;
        while (true)
        {
            var batch = await db.SubmissionLedger.Where(l => l.CreatedWeek <= ledgerCutoff.AddDays(-7)).Take(Batch).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }
            db.SubmissionLedger.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            ledger += batch.Count;
        }

        var expired = 0;
        while (true)
        {
            var batch = await db.SubmissionTickets.Where(t => t.ExpiresAt <= now).Take(Batch).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }
            db.SubmissionTickets.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            expired += batch.Count;
        }
        db.ChangeTracker.Clear();
        return new PurgeCounts(records, ledger, expired);
    }
}

/// <summary>Runs the purge on a schedule once the schema exists (SERVICE-API-PATTERNS section 7). Logs counts, nothing else.</summary>
public sealed class RetentionService(IServiceScopeFactory scopes, MigrationCompletionSignal migrated, IOptions<SubmissionOptions> options, ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await migrated.WaitAsync(stoppingToken);
        if (migrated.Failure is not null)
        {
            return;
        }
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.Retention.SweepIntervalMinutes));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var counts = await scope.ServiceProvider.GetRequiredService<RetentionPurger>().PurgeAsync(stoppingToken);
                if (counts != new PurgeCounts(0, 0, 0))
                {
                    logger.LogInformation("retention sweep removed {Records} records, {LedgerEntries} ledger entries, {Tickets} tickets",
                        counts.Records, counts.LedgerEntries, counts.Tickets);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("retention sweep failed ({ErrorType})", ex.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
