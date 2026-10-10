using System.Diagnostics.Metrics;

namespace ExitInterviewAgent.InterviewService.Interviews.CostControls;

/// <summary>
/// The spend and abuse metrics (ADR-0078, METRIC-ETHICS, METRICS-EXPOSITION). Every instrument is untagged: a measurement is a
/// number, never a label. No account, session, address or content can reach a series, and there is no per-account view. The
/// meter is exported through the kernel's OpenTelemetry meter provider (wired in <c>ServiceCollectionExtensions</c>).
/// </summary>
public sealed class CostMetrics : IDisposable
{
    public const string MeterName = "ExitInterviewAgent.InterviewService.Interviews";

    private readonly Counter<long> _started;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _withdrawn;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _rejectedEmailUnverified;
    private readonly Counter<long> _lostRefunded;
    private readonly Histogram<long> _tokensEstimated;
    private readonly Histogram<long> _modelCalls;

    public CostMetrics()
    {
        Meter = new Meter(MeterName);
        _started = Meter.CreateCounter<long>("interviews_started");
        _completed = Meter.CreateCounter<long>("interviews_completed");
        _failed = Meter.CreateCounter<long>("interviews_failed");
        _withdrawn = Meter.CreateCounter<long>("interviews_withdrawn");
        _rateLimited = Meter.CreateCounter<long>("rate_limited");
        _rejectedEmailUnverified = Meter.CreateCounter<long>("rejected_email_unverified");
        _lostRefunded = Meter.CreateCounter<long>("interviews_lost_refunded");
        _tokensEstimated = Meter.CreateHistogram<long>("tokens_estimated");
        _modelCalls = Meter.CreateHistogram<long>("model_calls");
    }

    /// <summary>The meter itself, so a test can listen to this host's instruments and no other.</summary>
    public Meter Meter { get; }

    public void Started() => _started.Add(1);

    public void Completed() => _completed.Add(1);

    /// <summary>A session ended as failed: a service fault, the credit comes back.</summary>
    public void Failed() => _failed.Add(1);

    /// <summary>A person ended the interview (deleted it, withdrew consent, or stopped).</summary>
    public void Withdrawn() => _withdrawn.Add(1);

    public void RateLimited() => _rateLimited.Add(1);

    public void RejectedEmailUnverified() => _rejectedEmailUnverified.Add(1);

    /// <summary>A session that was open when its process stopped got its credit back at the startup sweep (W11). Counts only.</summary>
    public void LostRefunded() => _lostRefunded.Add(1);

    /// <summary>What one interview (its turns and its tiles) cost in estimated tokens, once it has ended.</summary>
    public void Usage(long tokensEstimated, long modelCalls)
    {
        _tokensEstimated.Record(tokensEstimated);
        _modelCalls.Record(modelCalls);
    }

    public void Dispose() => Meter.Dispose();
}
