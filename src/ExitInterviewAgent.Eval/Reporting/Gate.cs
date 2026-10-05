using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Reporting;

public sealed record GateFinding(string Kind, string Subject, string Message);

public sealed record GateResult(IReadOnlyList<GateFinding> Failures, IReadOnlyList<GateFinding> Notes)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// The CI gate. Constraints: 100% of runs, hard block, the baseline is never consulted. Behaviours: not worse than the baseline by more than the
/// stated tolerance, per metric (a primary and its counter are separate lines, so neither can improve by hiding a regression in the other).
/// A harness error is a failure and never a caught failure. Improvements and unrecorded changes are printed, never blocked.
/// </summary>
public static class Gate
{
    public static GateResult Evaluate(ProfileRun run, IReadOnlyList<LoadedScenario> corpus, Baseline baseline, string specVersion, string digest)
    {
        var failures = new List<GateFinding>();
        var notes = new List<GateFinding>();
        void Fail(string kind, string subject, string msg) => failures.Add(new GateFinding(kind, subject, msg));

        if (!run.Ran) { Fail("run", run.Profile.Name, $"the gate profile did not run: {run.Status}"); return new GateResult(failures, notes); }

        if (baseline.SpecVersion != specVersion) Fail("baseline", "specVersion", $"baseline is for spec {baseline.SpecVersion}, the spec is {specVersion}: regenerate with `baseline --justification`");
        if (baseline.HarnessVer != Baseline.HarnessVersion) Fail("baseline", "harness", $"baseline is for harness {baseline.HarnessVer}, this is {Baseline.HarnessVersion}: regenerate with `baseline --justification`");
        if (baseline.CorpusDigest != digest) Fail("baseline", "corpusDigest", $"baseline digest {baseline.CorpusDigest} != corpus digest {digest}: scenarios, labels, rubric, personas or protocol changed; regenerate with `baseline --justification` and explain it in the PR");
        if (baseline.Profile != run.Profile.Name) Fail("baseline", "profile", $"baseline was recorded with profile '{baseline.Profile}', not '{run.Profile.Name}'");
        if (failures.Count > 0) return new GateResult(failures, notes);

        foreach (var e in run.Errors) Fail("error", $"{e.ScenarioId}#{e.Seed}", $"the harness threw {e.ExceptionType} (never counted as a pass or as a caught failure)");

        // Constraints: every run, every scenario.
        foreach (var g in run.Grades)
            foreach (var f in g.Failures.Where(f => f.Kind == AssertionResult_Constraint))
                Fail("constraint", $"{g.ScenarioId}#{g.Seed} {f.Id}", f.Message);

        var gateOf = corpus.ToDictionary(l => l.Id, l => l.Scenario.Gate);
        foreach (var id in corpus.Where(l => l.Scenario.Skip is null).Select(l => l.Id))
        {
            var status = Evaluation.ScenarioStatus(run.Grades, run.Errors, id);
            var recorded = baseline.Scenarios.GetValueOrDefault(id, "unrecorded");
            if (gateOf[id] == "constraint" && status != "pass")
            {
                foreach (var f in run.Grades.Where(g => g.ScenarioId == id).SelectMany(g => g.Failures).Where(f => f.Kind != AssertionResult_Constraint))
                    Fail("scenario", id, $"{f.Id}: {f.Message}");
            }
            else if (gateOf[id] == "behaviour")
            {
                if (status != "pass" && recorded == "pass")
                    foreach (var f in run.Grades.Where(g => g.ScenarioId == id).SelectMany(g => g.Failures).Where(f => f.Kind != AssertionResult_Constraint).DefaultIfEmpty())
                        Fail("regression", id, f is null ? "scenario no longer passes" : $"{f.Id}: {f.Message}");
                else if (status == "pass" && recorded != "pass") notes.Add(new GateFinding("improved", id, $"now passes (baseline: {recorded}); regenerate the baseline"));
            }
            if (recorded == "unrecorded") Fail("baseline", id, "scenario is not in the baseline: regenerate with `baseline --justification`");
        }
        foreach (var stale in baseline.Scenarios.Keys.Except(corpus.Select(l => l.Id))) Fail("baseline", stale, "baseline records a scenario that no longer exists");

        // Behaviour metrics.
        foreach (var def in MetricCatalog.All.Where(m => m.Gated))
        {
            var now = Evaluation.Pool(run.Grades, def.Id);
            if (!baseline.Metrics.TryGetValue(def.Id, out var then)) { Fail("baseline", def.Id, "metric is not in the baseline"); continue; }
            var was = new Count(then.K, then.N);
            if (now.N == 0 && was.N > 0) { Fail("metric", def.Id, $"no longer measured (baseline {was})"); continue; }
            if (now.Rate is not { } cur || was.Rate is not { } prev) continue;
            var tol = baseline.ToleranceOf(def.Id);
            const double eps = 1e-9;
            var worse = def.Direction == Direction.Higher ? cur < prev - tol - eps : cur > prev + tol + eps;
            var better = def.Direction == Direction.Higher ? cur > prev + eps : cur < prev - eps;
            if (worse) Fail("metric", def.Id, $"{Describe(def)} fell behind its baseline by more than the tolerance {tol:0.###}: now {now}, baseline {was}");
            else if (better) notes.Add(new GateFinding("improved", def.Id, $"now {now}, baseline {was}; regenerate the baseline to keep it describing the suite"));
        }

        var tokens = Evaluation.Tokens(run.Grades, g => gateOf.TryGetValue(g.ScenarioId, out _) && corpus.First(l => l.Id == g.ScenarioId).Scenario.Measures("coverage"));
        if (tokens is { } t && baseline.TokensMean > 0 && t.Mean > baseline.TokensMean * (1 + baseline.TokensRelativeTolerance))
            Fail("metric", "tokens", $"mean tokens per interview rose from {baseline.TokensMean:0.#} to {t.Mean:0.#} (more than {baseline.TokensRelativeTolerance:P0}); reported beside coverage, so cheaper-and-worse cannot hide");
        return new GateResult(failures, notes);
    }

    private const string AssertionResult_Constraint = Layer1.AssertionResult.Constraint;

    private static string Describe(MetricDef d) => d.Direction == Direction.Higher ? $"{d.Id} (higher is better)" : $"{d.Id} (lower is better)";
}
