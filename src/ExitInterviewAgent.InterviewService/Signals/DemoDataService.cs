using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.ServiceDefaults;
using ExitInterviewAgent.Signals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Signals;

/// <summary>Configuration section <c>Signals:Demo</c>. Development only: any other mode outside Development stops the service at startup.</summary>
public sealed class DemoDataOptions
{
    public const string SectionName = "Signals:Demo";
    public const string Off = "Off";
    public const string SeedMode = "Seed";
    public const string Remove = "Remove";

    /// <summary><c>Off</c> (default), <c>Seed</c> (reset the demo namespace, then seed it) or <c>Remove</c> (reset it and stop).</summary>
    public string Mode { get; set; } = Off;

    /// <summary>Seed of the generator. The same seed produces the same dataset.</summary>
    public int Seed { get; set; } = 42;

    public bool IsActive => !string.Equals(Mode, Off, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Seeds synthetic records for a local demo, THROUGH THE REAL DOOR: every record goes through <see cref="SubmissionService"/> (validation,
/// PII scan, ledger, receipt) as its own synthetic account, exactly as a request would. It writes only under the reserved
/// <see cref="DemoRecords.Prefix"/> and removes only under it, in every store the data landed in (records and receipts, the ledger
/// entries of its own synthetic accounts, and the published snapshot, which it republishes). Idempotent: every start resets, then seeds.
/// Not available outside Development; nothing here is reachable from a request.
/// </summary>
public sealed class DemoDataService(
    IServiceScopeFactory scopes,
    MigrationCompletionSignal interviewSchema,
    SignalsSchemaSignal signalsSchema,
    LedgerKeySet ledgerKeys,
    IOptions<DemoDataOptions> options,
    ILogger<DemoDataService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsActive)
        {
            return;
        }
        await interviewSchema.WaitAsync(stoppingToken);
        await signalsSchema.WaitAsync(stoppingToken);
        if (interviewSchema.Failure is not null || signalsSchema.Failure is not null)
        {
            return;
        }
        try
        {
            var removed = await ResetAsync(stoppingToken);
            var seeded = 0;
            if (string.Equals(options.Value.Mode, DemoDataOptions.SeedMode, StringComparison.OrdinalIgnoreCase))
            {
                seeded = await SeedAsync(options.Value.Seed, stoppingToken);
            }
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SnapshotPublisher>().RepublishAsync(stoppingToken);
            logger.LogInformation("demo data: {Removed} records removed, {Seeded} synthetic records submitted; snapshot republished", removed, seeded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("demo data failed ({ErrorType})", ex.GetType().Name);
        }
    }

    /// <summary>Submits the synthetic records through the submission pipeline. Returns how many were accepted.</summary>
    public async Task<int> SeedAsync(int seed, CancellationToken ct)
    {
        var accepted = 0;
        foreach (var record in DemoRecords.Generate(seed))
        {
            // The namespace is enforced here, at the receiver of the seeding, not left to the generator's good behaviour.
            if (!record.Employer.StartsWith(DemoRecords.Prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A demo record outside the reserved namespace was refused.");
            }
            using var scope = scopes.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<SubmissionService>().SubmitAsync(record.Json, record.Sub, ct);
            if (outcome.Accepted)
            {
                accepted++;
            }
            else
            {
                logger.LogWarning("a demo record was rejected ({Code})", outcome.Rejection!.Code);
            }
        }
        return accepted;
    }

    /// <summary>Removes everything the demo wrote: records and receipts under the prefix, and the ledger entries of the synthetic accounts.</summary>
    public async Task<int> ResetAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();

        var ids = await db.Records.Where(r => r.EmployerRef.StartsWith(DemoRecords.Prefix)).Select(r => r.Id).ToListAsync(ct);
        foreach (var chunk in ids.Chunk(500))
        {
            db.Receipts.RemoveRange(await db.Receipts.Where(r => chunk.Contains(r.RecordId)).ToListAsync(ct));
            db.Records.RemoveRange(await db.Records.Where(r => chunk.Contains(r.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        // The accounts are synthetic and deterministic, so their ledger entries can be recomputed; no other entry is touched.
        var tags = DemoRecords.Generate(0).SelectMany(r => ledgerKeys.AllTags(r.Sub, r.Employer)).ToList();
        foreach (var chunk in tags.Chunk(500))
        {
            db.SubmissionLedger.RemoveRange(await db.SubmissionLedger.Where(l => chunk.Contains(l.Tag)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
        return ids.Count;
    }
}
