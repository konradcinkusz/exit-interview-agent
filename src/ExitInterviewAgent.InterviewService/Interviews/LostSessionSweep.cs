using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Interviews.CostControls;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>
/// The startup sweep (W11, ADR-0077 implementation notes). Sessions live in memory (ADR-0076), so a restart loses the open ones and
/// their credits would stay spent. Once the schema is ready, this settles every consume that has no settlement row, started long
/// enough ago that its session cannot still be open (the idle window, a margin and the hour bucket's slack), and that this process
/// does not hold: each one is settled as <c>lost</c>, which returns its credit (<c>refund_lost_session</c>) in the same write.
/// The settlement row's unique session id makes it idempotent and safe for several instances and parallel runs.
/// </summary>
public sealed class LostSessionSweep : BackgroundService
{
    /// <summary>Added to the idle window: the session may have started up to this long before its hour bucket.</summary>
    internal static readonly TimeSpan HourSlack = TimeSpan.FromHours(1);

    /// <summary>A margin beyond the idle window, so a request that just expired a session is never mistaken for a lost one.</summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    internal const int BatchSize = 100;

    private readonly TaskCompletionSource<int> _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CreditLedger _ledger;
    private readonly InterviewSessionStore _store;
    private readonly IOptionsMonitor<InterviewServiceOptions> _options;
    private readonly TimeProvider _clock;
    private readonly CostMetrics _metrics;
    private readonly MigrationCompletionSignal _schema;
    private readonly ILogger<LostSessionSweep> _logger;

    public LostSessionSweep(
        CreditLedger ledger,
        InterviewSessionStore store,
        IOptionsMonitor<InterviewServiceOptions> options,
        TimeProvider clock,
        CostMetrics metrics,
        MigrationCompletionSignal schema,
        ILogger<LostSessionSweep> logger)
    {
        _ledger = ledger;
        _store = store;
        _options = options;
        _clock = clock;
        _metrics = metrics;
        _schema = schema;
        _logger = logger;
    }

    /// <summary>The number of credits the startup run returned. Completes once that run has finished (tests await it).</summary>
    public Task<int> StartupRun => _startup.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The schema first: a sweep that reads an unmigrated database would fail on the first query (SERVICE-API-PATTERNS §7).
            await _schema.WaitAsync(stoppingToken).ConfigureAwait(false);
            if (_schema.Failure is not null || !_options.CurrentValue.RequireCredit)
            {
                // Without a ledger (or without a schema) there is nothing to return.
                _startup.TrySetResult(0);
                return;
            }

            _startup.TrySetResult(await RunOnceAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _startup.TrySetCanceled(stoppingToken);
        }
        catch (Exception e)
        {
            // The type only: an exception message may carry content.
            _logger.LogError("Lost-session sweep failed: {ExceptionType}", e.GetType().Name);
            _startup.TrySetException(e);
        }
    }

    /// <summary>
    /// One pass over the unsettled consumes, in batches, until none is left. Returns how many credits this call returned. Safe to run
    /// in parallel with another pass (here or on another instance): each session is settled by one of them only.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var startedBefore = _clock.GetUtcNow() - (options.IdleTimeout + Margin + HourSlack);
        var refunded = 0;

        while (true)
        {
            var batch = await _ledger.UnsettledConsumesAsync(startedBefore, _store.LiveIds(), BatchSize, ct).ConfigureAwait(false);
            if (batch.Count == 0) break;

            foreach (var consume in batch)
            {
                if (!await _ledger.SettleAsync(consume.AccountRef, consume.SessionId, SessionOutcome.Lost, ct).ConfigureAwait(false)) continue;
                refunded++;
                _metrics.LostRefunded();
            }
        }

        if (refunded > 0) _logger.LogInformation("Lost sessions refunded: {Count}", refunded);
        return refunded;
    }
}
