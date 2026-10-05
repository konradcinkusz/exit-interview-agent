using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Reporting;

public sealed record ProfileRun(
    ModelProfile Profile, string Status, IReadOnlyList<RunGrade> Grades, IReadOnlyList<RunError> Errors, IReadOnlyList<string> SkippedScenarios, IReadOnlyList<RunRecord> Runs)
{
    public bool Ran => Status == "ran";

    public static ProfileRun Skipped(ModelProfile p) => new(p, p.SkipReason ?? "skipped", [], [], [], []);
}

public enum Direction { Higher, Lower, Report }

public sealed record MetricDef(string Id, string Title, Direction Direction, string? Counter, bool Gated, string Description);

/// <summary>The metrics, their direction and their counter-metric. A counter-metric is reported in the same row and gated with its primary: a primary cannot improve by degrading its counter without the gate seeing it.</summary>
public static class MetricCatalog
{
    public static readonly IReadOnlyList<MetricDef> All =
    [
        new("coverage", "Topic coverage (B-01)", Direction.Higher, "depth", true, "covered topics / 6, over scenarios that measure coverage"),
        new("depth", "Depth of covered topics (counter of coverage)", Direction.Higher, "coverage", true, "covered topics with a quote showing a concrete detail / covered topics"),
        new("lqr", "Leading-question rate (B-02)", Direction.Lower, "fuv", true, "questions flagged by the guard rules or the independent rules / questions"),
        new("fuv", "Follow-up on vague (B-03)", Direction.Higher, "opr", true, "hand-labelled vague answers followed by a probe / hand-labelled vague answers"),
        new("opr", "Over-probe rate (counter of fuv)", Direction.Lower, "fuv", true, "probes after a specific answer or a decline / specific answers and declines"),
        new("clarified", "Contradiction clarified once (B-04)", Direction.Higher, null, true, "contradictory interviews with one neutral clarification per topic / contradictory interviews"),
        new("released", "Hostile or terse interviewee released (B-05, B-06)", Direction.Higher, null, true, "interviews that closed without pressure / such interviews"),
        new("budget_graceful", "Budget respected (B-07)", Direction.Higher, null, true, "budget-exhausted interviews that closed gracefully / such interviews"),
        new("degraded_ok", "Degradation graceful (B-08)", Direction.Higher, null, true, "fault runs that fell back without fabricating / fault runs"),
        new("redirected", "Names masked and redirected once (B-09)", Direction.Higher, null, true, "naming interviews handled by masking and one redirect per topic / naming interviews"),
        new("edge_case", "Edge-case handling vs the persona's expectation (B-10)", Direction.Higher, null, true, "runs matching the persona's declared expectation / fault-free runs"),
        new("double_barrelled", "Double-barrelled questions (report, known protocol wording)", Direction.Lower, null, true, "questions with two interrogatives / questions; a recorded finding, gated not to get worse"),
        new("tf", "Transcript fidelity (report; C-06 is the constraint)", Direction.Report, "qs", false, "record quotes found in the transcript / record quotes"),
        new("qs", "Quote support (counter of tf)", Direction.Report, "tf", false, "rated topics with at least one quote / rated topics"),
        new("lqr.guard_rules", "Leading questions by the guard's rules (report)", Direction.Report, null, false, "questions flagged by QuestionGuard.LeadingReason / questions"),
        new("lqr.independent_rules", "Leading questions by the independent rules (report)", Direction.Report, null, false, "questions flagged by IndependentRules / questions"),
        new("lqr.rules_disagree", "Questions the two rule sets disagree on (report)", Direction.Report, null, false, "questions flagged by exactly one rule set / questions"),
    ];

    public static MetricDef Get(string id) => All.Single(m => m.Id == id);
}

public static class Evaluation
{
    public static async Task<ProfileRun> RunProfileAsync(
        ModelProfile profile, IReadOnlyList<LoadedScenario> corpus, IReadOnlyDictionary<string, string> labels, Action<string>? progress = null, CancellationToken ct = default)
    {
        if (!profile.Runnable) return ProfileRun.Skipped(profile);
        var grades = new List<RunGrade>();
        var errors = new List<RunError>();
        var skipped = new List<string>();
        var runs = new List<RunRecord>();
        foreach (var l in corpus)
        {
            if (l.Scenario.Skip is not null) { skipped.Add(l.Id); continue; }
            foreach (var seed in l.Scenario.EffectiveSeeds)
            {
                var outcome = await ScenarioRunner.RunAsync(l.Scenario, seed, profile, ct).ConfigureAwait(false);
                if (outcome.Run is { } run)
                {
                    runs.Add(run);
                    grades.Add(Layer1Grader.Grade(run, labels));
                }
                else errors.Add(outcome.Error!);
                progress?.Invoke($"{l.Id}#{seed}");
            }
        }
        return new ProfileRun(profile, "ran", grades, errors, skipped, runs);
    }

    /// <summary>Pools one metric over the grades that satisfy <paramref name="where"/>.</summary>
    public static Count Pool(IEnumerable<RunGrade> grades, string metric, Func<RunGrade, bool>? where = null) =>
        grades.Where(g => where?.Invoke(g) ?? true).Aggregate(Count.Zero, (acc, g) => g.Measurements.TryGetValue(metric, out var c) ? acc + c : acc);

    public static string ScenarioStatus(IEnumerable<RunGrade> grades, IEnumerable<RunError> errors, string id) =>
        errors.Any(e => e.ScenarioId == id) ? "error" : grades.Where(g => g.ScenarioId == id).All(g => g.Passed) ? "pass" : "fail";

    public static (double Mean, long Min, long Max, int Runs)? Tokens(IEnumerable<RunGrade> grades, Func<RunGrade, bool>? where = null)
    {
        var totals = grades.Where(g => where?.Invoke(g) ?? true).Select(g => g.Usage.InputTokens + g.Usage.OutputTokens).ToList();
        return totals.Count == 0 ? null : (totals.Average(), totals.Min(), totals.Max(), totals.Count);
    }
}
