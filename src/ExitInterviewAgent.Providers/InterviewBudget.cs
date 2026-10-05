using ExitInterviewAgent.Agent.Protocol;

namespace ExitInterviewAgent.Providers;

/// <summary>What one interview has used so far. Counts and a duration only.</summary>
public sealed record UsageSnapshot(int Calls, long InputTokens, long OutputTokens, TimeSpan TotalLatency, TimeSpan MaxLatency, decimal? Cost, string? Currency, bool AnyEstimated);

/// <summary>
/// The hard per-interview ceiling, enforced <b>before</b> each model call: when tokens, calls or cost are at the ceiling the
/// next call is not made and <see cref="ProviderException"/> (<see cref="ProviderFailureKind.BudgetExceeded"/>) ends the interview.
/// It sits above the protocol's graceful budget (T4: <c>MaxEstimatedTokens</c> and <c>MaxModelCalls</c> close the interview with
/// a record), so reaching it means something went wrong, and it counts provider-reported usage. "Hard" means no further call
/// starts: the call in flight when the ceiling is crossed completes, so the ceiling can be overshot by one call. Failed attempts
/// that the provider may bill are not visible here and are not counted.
/// </summary>
public sealed class InterviewBudget
{
    private readonly object _gate = new();
    private readonly long _maxTokens;
    private readonly int _maxCalls;
    private readonly PriceConfig? _prices;
    private readonly decimal? _maxCost;
    private int _calls;
    private long _input, _output;
    private TimeSpan _latency, _maxLatency;
    private bool _estimated;

    public InterviewBudget(long maxTokens, int maxCalls, PriceConfig? prices = null, decimal? maxCost = null)
    {
        if (maxTokens <= 0 || maxCalls <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens), "A budget needs positive ceilings.");
        if (maxCost is not null && prices is null) throw new ArgumentException("A cost ceiling needs prices.", nameof(maxCost));
        _maxTokens = maxTokens;
        _maxCalls = maxCalls;
        _prices = prices;
        _maxCost = maxCost;
    }

    /// <summary>
    /// The hard ceiling for a protocol: twice the graceful token budget (extraction, which the graceful budget does not count,
    /// resends the whole transcript up to twice) and the graceful call budget plus the extractor's two attempts and a margin.
    /// <paramref name="settings"/> may lower the graceful budget (<c>--max-tokens</c>) and add a cost ceiling.
    /// </summary>
    public static InterviewBudget ForProtocol(ProtocolLimits limits, ProviderSettings? settings = null) =>
        new(Math.Max(settings?.MaxTokens ?? limits.MaxEstimatedTokens, 1) * 2, limits.MaxModelCalls + 4, settings?.Prices, settings?.MaxCost);

    public void EnsureAvailable()
    {
        lock (_gate)
        {
            string? limit = null;
            if (_input + _output >= _maxTokens) limit = "tokens";
            else if (_calls >= _maxCalls) limit = "calls";
            else if (_maxCost is { } cap && _prices is { } p && p.Cost(_input, _output) >= cap) limit = "cost";
            if (limit is null) return;
            ProviderTelemetry.BudgetExceeded.Add(1, new KeyValuePair<string, object?>(ProviderTelemetry.Attr.Limit, limit));
            throw new ProviderException(ProviderFailureKind.BudgetExceeded, detail: limit);
        }
    }

    public void Record(long inputTokens, long outputTokens, TimeSpan latency, bool estimated)
    {
        lock (_gate)
        {
            _calls++;
            _input += Math.Max(inputTokens, 0);
            _output += Math.Max(outputTokens, 0);
            _latency += latency;
            if (latency > _maxLatency) _maxLatency = latency;
            _estimated |= estimated;
        }
    }

    public UsageSnapshot Snapshot()
    {
        lock (_gate)
            return new UsageSnapshot(_calls, _input, _output, _latency, _maxLatency, _prices?.Cost(_input, _output), _prices?.Currency, _estimated);
    }
}
