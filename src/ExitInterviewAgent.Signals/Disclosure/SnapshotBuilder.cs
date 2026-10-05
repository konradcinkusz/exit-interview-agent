using System.Runtime.CompilerServices;

namespace ExitInterviewAgent.Signals;

/// <summary>What a build skipped, as counts only.</summary>
public sealed class BuildReport
{
    public int RejectedObservations { get; internal set; }
}

/// <summary>
/// Turns a feed of observations into employer views, one employer at a time, so memory stays that of one employer whatever the size
/// of the store. A feed whose employers are not contiguous breaks the port's contract; the build then fails and nothing is
/// published, because a half-counted employer would pass the rules with wrong numbers.
/// </summary>
public sealed class SnapshotBuilder
{
    private readonly DisclosureRules _rules;
    private readonly IDisclosurePolicy _policy;

    public SnapshotBuilder(DisclosureRules rules) : this(rules, StandardDisclosurePolicy.Instance)
    {
    }

    internal SnapshotBuilder(DisclosureRules rules, IDisclosurePolicy policy)
    {
        if (rules.MinimumGroupSize < DisclosureRules.FloorForMinimumGroupSize)
        {
            throw new ArgumentOutOfRangeException(nameof(rules), $"The minimum group size is at least {DisclosureRules.FloorForMinimumGroupSize}.");
        }
        _rules = rules;
        _policy = policy;
    }

    public async IAsyncEnumerable<EmployerView> BuildAsync(
        IAsyncEnumerable<Observation> feed, BuildReport report, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var finished = new HashSet<string>(StringComparer.Ordinal);
        string? current = null;
        var accumulator = new EmployerAccumulator(_rules, _policy);

        await foreach (var observation in feed.WithCancellation(ct))
        {
            if (!string.Equals(observation.EmployerRef, current, StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    report.RejectedObservations += accumulator.Rejected;
                    finished.Add(current);
                    if (accumulator.Complete(current) is { } view)
                    {
                        yield return view;
                    }
                }
                if (finished.Contains(observation.EmployerRef))
                {
                    throw new InvalidOperationException("The observation feed is not grouped by employer.");
                }
                current = observation.EmployerRef;
                accumulator = new EmployerAccumulator(_rules, _policy);
            }
            accumulator.Add(observation);
        }

        if (current is not null)
        {
            report.RejectedObservations += accumulator.Rejected;
            if (accumulator.Complete(current) is { } last)
            {
                yield return last;
            }
        }
    }
}
