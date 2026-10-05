using System.Diagnostics;
using System.Text.Json;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.Signals;

public enum PublishOutcome { Published, Skipped, Failed }

/// <summary>
/// Publishes one snapshot per batch period (ADR-0055). Idempotent: the period, the rule set and k form a fingerprint, and a
/// snapshot with the current fingerprint is never rebuilt, so a restart or a second tick does nothing. Restart-safe: nothing but
/// the database remembers a publication. Atomic: the snapshot row is written after its employer rows and is the publication.
/// A change of k or of the rule set republishes at once (a stricter rule must not wait for midnight), which is an operator action,
/// never something a submission can trigger.
/// Logs and metrics carry counts of what was PUBLISHED and the type of a failure, nothing else.
/// </summary>
public sealed class SnapshotPublisher(
    SignalsDbContext db,
    IObservationSource source,
    IOptions<SignalsOptions> options,
    TimeProvider time,
    SignalsMetrics metrics,
    PublicationState state,
    ILogger<SnapshotPublisher> logger)
{
    private const int WriteBatch = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DateTimeOffset PeriodStart(DateTimeOffset now, TimeSpan interval)
        => new(now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % interval.Ticks, TimeSpan.Zero);

    public static string FingerprintOf(DateTimeOffset periodStart, TimeSpan interval, int k)
        => $"{periodStart.UtcDateTime:yyyyMMddTHHmm}Z|i{(int)interval.TotalHours}|v{DisclosureRules.Version}|k{k}";

    public Task<PublishOutcome> RunDueAsync(CancellationToken ct) => RunDueAsync(ct, StandardDisclosurePolicy.Instance);

    internal async Task<PublishOutcome> RunDueAsync(CancellationToken ct, IDisclosurePolicy policy)
    {
        await state.RunLock.WaitAsync(ct);
        var clock = Stopwatch.StartNew();
        try
        {
            var settings = options.Value;
            var periodStart = PeriodStart(time.GetUtcNow(), settings.PublishInterval);
            var fingerprint = FingerprintOf(periodStart, settings.PublishInterval, settings.MinimumGroupSize);

            var current = await db.Snapshots.AsNoTracking().OrderByDescending(s => s.Seq).FirstOrDefaultAsync(ct);
            if (current?.Fingerprint == fingerprint)
            {
                state.Succeeded();
                metrics.Record(SignalsMetrics.Skipped);
                return PublishOutcome.Skipped;
            }

            var snapshotId = Guid.NewGuid();
            try
            {
                var published = await WriteAsync(snapshotId, new DisclosureRules(settings.MinimumGroupSize), policy, ct);
                var row = new SnapshotRow
                {
                    Seq = (current?.Seq ?? 0) + 1,
                    Id = snapshotId,
                    Fingerprint = fingerprint,
                    PeriodStart = periodStart,
                    IntervalHours = (int)settings.PublishInterval.TotalHours,
                    RulesVersion = DisclosureRules.Version,
                    MinimumGroupSize = settings.MinimumGroupSize,
                    EmployerCount = published,
                };
                db.Snapshots.Add(row);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Another instance published the same fingerprint first: its snapshot stands, ours is dropped.
                    db.ChangeTracker.Clear();
                    await RemoveRowsAsync(r => r.SnapshotId == snapshotId, ct);
                    if (await db.Snapshots.AnyAsync(s => s.Fingerprint == fingerprint, ct))
                    {
                        metrics.Record(SignalsMetrics.Skipped);
                        return PublishOutcome.Skipped;
                    }
                    throw;
                }
                db.ChangeTracker.Clear();
                await RemoveRowsAsync(r => r.SnapshotId != snapshotId, ct);
                await RemoveOldSnapshotsAsync(row.Seq, ct);

                state.Succeeded();
                metrics.Record(SignalsMetrics.Published, clock.Elapsed, published);
                logger.LogInformation("signals snapshot published: {Employers} employers, rules v{Version}, k {K}", published, DisclosureRules.Version, settings.MinimumGroupSize);
                return PublishOutcome.Published;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                await TryRemoveAsync(snapshotId);
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type only: an exception message from the store or the feed could carry data.
            logger.LogWarning("signals publication failed ({ErrorType}); the previous snapshot stays in place", ex.GetType().Name);
            state.Failed();
            metrics.Record(SignalsMetrics.Failed, clock.Elapsed);
            return PublishOutcome.Failed;
        }
        finally
        {
            state.RunLock.Release();
        }
    }

    private async Task<int> WriteAsync(Guid snapshotId, DisclosureRules rules, IDisclosurePolicy policy, CancellationToken ct)
    {
        var builder = new SnapshotBuilder(rules, policy);
        var report = new BuildReport();
        var written = 0;
        var pending = 0;
        await foreach (var view in builder.BuildAsync(source.ReadAsync(ct), report, ct))
        {
            db.EmployerSnapshots.Add(new EmployerSnapshotRow { SnapshotId = snapshotId, EmployerRef = view.EmployerRef, View = JsonSerializer.Serialize(view, Json) });
            written++;
            if (++pending >= WriteBatch)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                pending = 0;
            }
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        if (report.RejectedObservations > 0)
        {
            logger.LogWarning("signals skipped {Rejected} observations outside the vocabulary", report.RejectedObservations);
        }
        return written;
    }

    private async Task RemoveRowsAsync(System.Linq.Expressions.Expression<Func<EmployerSnapshotRow, bool>> where, CancellationToken ct)
    {
        while (true)
        {
            var batch = await db.EmployerSnapshots.Where(where).Take(500).ToListAsync(ct);
            if (batch.Count == 0)
            {
                return;
            }
            db.EmployerSnapshots.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task RemoveOldSnapshotsAsync(long keepSeq, CancellationToken ct)
    {
        var old = await db.Snapshots.Where(s => s.Seq < keepSeq).ToListAsync(ct);
        if (old.Count > 0)
        {
            db.Snapshots.RemoveRange(old);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task TryRemoveAsync(Guid snapshotId)
    {
        try
        {
            await RemoveRowsAsync(r => r.SnapshotId == snapshotId, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best effort: invisible rows are removed by the next successful publication.
        }
    }
}
