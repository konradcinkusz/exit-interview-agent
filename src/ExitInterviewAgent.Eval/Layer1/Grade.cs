using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Layer1;

public enum Verdict { Pass, Fail, NotApplicable }

/// <summary>
/// One Layer 1 result. <see cref="Id"/> is stable (<c>L1.C-03</c>, <c>L1.X.outcome</c>); <see cref="Message"/> carries counts and ids and never
/// interview text, so a failure can be pasted into an issue.
/// </summary>
public sealed record AssertionResult(string Id, string Kind, Verdict Verdict, string Message)
{
    public const string Constraint = "constraint";
    public const string Expectation = "expectation";

    public bool Failed => Verdict == Verdict.Fail;
}

public sealed record UsageNumbers(int ModelCalls, long InputTokens, long OutputTokens, bool UsageReported);

/// <summary>Wall times of model-call spans by role. VOLATILE: depends on the machine, excluded from the deterministic report and from every gate.</summary>
public sealed record LatencyNumbers(double SessionMs, IReadOnlyDictionary<string, IReadOnlyList<double>> ChatMsByRole);

public sealed record RunGrade(
    string ScenarioId, int Seed, string Profile, IReadOnlyList<AssertionResult> Assertions,
    IReadOnlyDictionary<string, Count> Measurements, UsageNumbers Usage, LatencyNumbers Latency)
{
    public IEnumerable<AssertionResult> Failures => Assertions.Where(a => a.Failed);

    public bool Passed => !Failures.Any();
}
