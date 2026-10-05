using System.Text.Json;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExitInterviewAgent.Signals;

/// <summary>The published snapshot's header: when its batch started (never when the run finished), the rules it was made under and how many employers it lists.</summary>
public sealed record SnapshotInfo(long Seq, Guid Id, DateTimeOffset GeneratedAt, int IntervalHours, string RulesVersion, int MinimumGroupSize, int EmployerCount);

public sealed record EmployerPage(SnapshotInfo? Snapshot, IReadOnlyList<string> Employers, int Total);

/// <summary>
/// The only read path. Every query is a lookup by snapshot and employer reference, or a page of references in alphabetical order:
/// there is no query by value, so nothing here can rank, filter or search employers by what was measured about them.
/// </summary>
public sealed class SnapshotReader(SignalsDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SnapshotInfo?> CurrentAsync(CancellationToken ct)
    {
        var row = await db.Snapshots.AsNoTracking().OrderByDescending(s => s.Seq).FirstOrDefaultAsync(ct);
        return row is null ? null : new SnapshotInfo(row.Seq, row.Id, row.PeriodStart, row.IntervalHours, row.RulesVersion, row.MinimumGroupSize, row.EmployerCount);
    }

    public async Task<EmployerPage> ListAsync(int page, int limit, CancellationToken ct)
    {
        var snapshot = await CurrentAsync(ct);
        if (snapshot is null)
        {
            return new EmployerPage(null, [], 0);
        }
        var id = snapshot.Id;
        var employers = await db.EmployerSnapshots.AsNoTracking().Where(r => r.SnapshotId == id)
            .OrderBy(r => r.EmployerRef).Skip((page - 1) * limit).Take(limit).Select(r => r.EmployerRef).ToListAsync(ct);
        return new EmployerPage(snapshot, employers, snapshot.EmployerCount);
    }

    /// <summary>The employer's view in the current snapshot, or null. An employer with nothing displayable and an unknown one are the same answer, by construction: neither has a row.</summary>
    public async Task<(SnapshotInfo Snapshot, EmployerView View)?> GetAsync(string employerRef, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var snapshot = await CurrentAsync(ct);
            if (snapshot is null)
            {
                return null;
            }
            var id = snapshot.Id;
            var json = await db.EmployerSnapshots.AsNoTracking().Where(r => r.SnapshotId == id && r.EmployerRef == employerRef).Select(r => r.View).FirstOrDefaultAsync(ct);
            if (json is not null)
            {
                return (snapshot, JsonSerializer.Deserialize<EmployerView>(json, Json)!);
            }
            // A publication may have replaced the snapshot between the two reads; look once more at the new current one.
            if ((await CurrentAsync(ct))?.Seq == snapshot.Seq)
            {
                return null;
            }
        }
        return null;
    }
}
