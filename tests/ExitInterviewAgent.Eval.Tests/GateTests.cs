using ExitInterviewAgent.Eval.Cli;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Reporting;
using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Stats;
using static ExitInterviewAgent.Eval.Tests.Support.Fixtures;

namespace ExitInterviewAgent.Eval.Tests;

/// <summary>The gate can fail: constraints at 100%, behaviours against the recorded baseline with a tolerance, refusals across versions, harness errors.</summary>
public class GateTests
{
    private static async Task<(ProfileRun Run, Baseline Baseline)> Recorded(Dictionary<string, double>? tolerances = null, Func<Dictionary<string, Count>, Dictionary<string, Count>>? editMetrics = null, string? digest = null, string? spec = null)
    {
        var run = await FullMockRun();
        var metrics = MetricCatalog.All.Where(m => m.Gated).ToDictionary(m => m.Id, m => Evaluation.Pool(run.Grades, m.Id));
        if (editMetrics is not null) metrics = editMetrics(metrics);
        var scenarios = All.ToDictionary(l => l.Id, l => Evaluation.ScenarioStatus(run.Grades, run.Errors, l.Id));
        var tokens = Evaluation.Tokens(run.Grades, g => All.First(l => l.Id == g.ScenarioId).Scenario.Measures("coverage"))!.Value.Mean;
        var json = Baseline.Serialize(spec ?? Corpus.SpecVersion(), digest ?? Corpus.Digest(All), "mock", "test baseline", scenarios, metrics, tokens, tolerances, 0.10, "2026-10-05");
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return (run, Baseline.Load(path));
    }

    private static GateResult Evaluate(ProfileRun run, Baseline b) => Gate.Evaluate(run, All, b, Corpus.SpecVersion(), Corpus.Digest(All));

    [Fact]
    public async Task A_run_against_a_baseline_recorded_from_it_passes()
    {
        var (run, b) = await Recorded();

        var result = Evaluate(run, b);

        Assert.True(result.Passed, string.Join("; ", result.Failures.Select(f => f.Subject + " " + f.Message)));
    }

    [Fact]
    public async Task The_committed_baseline_matches_the_committed_corpus_and_the_run_passes_the_gate()
    {
        var run = await FullMockRun();

        var result = Evaluate(run, Baseline.Load());

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures.Select(f => $"[{f.Kind}] {f.Subject}: {f.Message}")));
    }

    [Fact]
    public async Task A_constraint_failure_in_any_run_fails_the_gate_even_inside_a_behaviour_scenario_and_even_if_the_baseline_says_pass()
    {
        var (run, b) = await Recorded();
        var broken = run.Grades.Select(g => g.ScenarioId.StartsWith("hap-001", StringComparison.Ordinal) && g.Seed == 1
            ? g with { Assertions = [.. g.Assertions.Where(a => a.Id != "L1.C-06"), new AssertionResult("L1.C-06", AssertionResult.Constraint, Verdict.Fail, "non_verbatim_quotes=1")] }
            : g).ToList();

        var result = Evaluate(run with { Grades = broken }, b);

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, f => f.Kind == "constraint" && f.Subject.Contains("L1.C-06"));
    }

    [Fact]
    public async Task A_behaviour_metric_that_falls_below_its_baseline_fails_and_a_tolerance_can_allow_it()
    {
        var (run, strict) = await Recorded();
        var worse = run.Grades.Select(g => g.Measurements.ContainsKey("coverage") ? g with { Measurements = new Dictionary<string, Count>(g.Measurements) { ["coverage"] = new Count(3, 6) } } : g).ToList();
        var degraded = run with { Grades = worse };
        var (_, lenient) = await Recorded(new Dictionary<string, double> { ["default"] = 0.0, ["coverage"] = 0.6, ["depth"] = 0.6 });

        Assert.Contains(Evaluate(degraded, strict).Failures, f => f.Kind == "metric" && f.Subject == "coverage");
        Assert.DoesNotContain(Evaluate(degraded, lenient).Failures, f => f.Subject == "coverage");
    }

    [Fact]
    public async Task A_lower_is_better_metric_that_rises_fails_so_a_leading_question_regression_blocks()
    {
        var (run, b) = await Recorded();
        var worse = run.Grades.Select(g => g.Measurements.ContainsKey("lqr") ? g with { Measurements = new Dictionary<string, Count>(g.Measurements) { ["lqr"] = new Count(3, g.Measurements["lqr"].N) } } : g).ToList();

        var result = Evaluate(run with { Grades = worse }, b);

        Assert.Contains(result.Failures, f => f.Kind == "metric" && f.Subject == "lqr");
    }

    [Fact]
    public async Task A_primary_cannot_improve_by_degrading_its_counter_because_each_is_gated_on_its_own_line()
    {
        var (run, b) = await Recorded();
        // fuv up (it is already 100%), opr up: interrogating everything.
        var worse = run.Grades.Select(g => g.Measurements.ContainsKey("opr") ? g with { Measurements = new Dictionary<string, Count>(g.Measurements) { ["opr"] = new Count(g.Measurements["opr"].N, g.Measurements["opr"].N) } } : g).ToList();

        var result = Evaluate(run with { Grades = worse }, b);

        Assert.Contains(result.Failures, f => f.Kind == "metric" && f.Subject == "opr");
    }

    [Fact]
    public async Task An_improvement_is_noted_and_never_blocked()
    {
        var (run, b) = await Recorded(editMetrics: m => { m["lqr"] = new Count(5, m["lqr"].N); return m; });

        var result = Evaluate(run, b);

        Assert.True(result.Passed);
        Assert.Contains(result.Notes, n => n.Kind == "improved" && n.Subject == "lqr");
    }

    [Fact]
    public async Task A_baseline_for_another_spec_version_or_corpus_is_refused_not_compared()
    {
        var (run, staleDigest) = await Recorded(digest: "sha256:0000000000000000");
        var (_, staleSpec) = await Recorded(spec: "0.9.0");

        Assert.Contains(Evaluate(run, staleDigest).Failures, f => f.Subject == "corpusDigest" && f.Message.Contains("regenerate"));
        Assert.Contains(Evaluate(run, staleSpec).Failures, f => f.Subject == "specVersion");
    }

    [Fact]
    public async Task A_scenario_missing_from_the_baseline_and_a_stale_baseline_entry_both_fail()
    {
        var (run, b) = await Recorded();
        var missing = new Baseline { SpecVersion = b.SpecVersion, HarnessVer = b.HarnessVer, CorpusDigest = b.CorpusDigest, Profile = b.Profile, Recorded = b.Recorded, Justification = b.Justification, Tolerances = b.Tolerances, TokensMean = b.TokensMean, TokensRelativeTolerance = b.TokensRelativeTolerance, Metrics = b.Metrics, Scenarios = b.Scenarios.Skip(1).Append(KeyValuePair.Create("hap-999-gone", "pass")).ToDictionary() };

        var result = Evaluate(run, missing);

        Assert.Contains(result.Failures, f => f.Message.Contains("not in the baseline"));
        Assert.Contains(result.Failures, f => f.Subject == "hap-999-gone");
    }

    [Fact]
    public async Task A_behaviour_scenario_that_passed_at_the_baseline_and_fails_now_is_a_regression()
    {
        var (run, b) = await Recorded();
        var bad = run.Grades.Select(g => g.ScenarioId.StartsWith("amb-002", StringComparison.Ordinal) ? g with { Assertions = [.. g.Assertions, new AssertionResult("L1.X.min.clarifications", AssertionResult.Expectation, Verdict.Fail, "clarifications=0, expected at least 1")] } : g).ToList();

        var result = Evaluate(run with { Grades = bad }, b);

        Assert.Contains(result.Failures, f => f.Kind == "regression" && f.Subject.StartsWith("amb-002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_harness_error_fails_the_gate_and_is_never_a_pass()
    {
        var (run, b) = await Recorded();

        var result = Evaluate(run with { Errors = [new RunError("hap-001-talkative-covers-all-six-topics", 1, "mock", "InvalidOperationException")] }, b);

        Assert.Contains(result.Failures, f => f.Kind == "error");
    }

    [Fact]
    public async Task A_profile_that_did_not_run_cannot_pass_the_gate()
    {
        var (_, b) = await Recorded();
        var skipped = ProfileRun.Skipped(new ModelProfile("mock", "x", "none", null, "skipped:no-credential (environment not set: X)"));

        var result = Evaluate(skipped, b);

        Assert.False(result.Passed);
        Assert.Contains("skipped:no-credential", result.Failures.Single().Message);
    }

    [Fact]
    public async Task A_cost_regression_in_tokens_per_interview_fails_even_when_every_quality_metric_is_unchanged()
    {
        var (run, b) = await Recorded();
        var costly = run.Grades.Select(g => g with { Usage = g.Usage with { InputTokens = g.Usage.InputTokens * 3 } }).ToList();

        var result = Evaluate(run with { Grades = costly }, b);

        Assert.Contains(result.Failures, f => f.Subject == "tokens");
    }

    [Fact]
    public async Task Regenerating_the_baseline_without_a_justification_is_refused_and_writes_nothing()
    {
        var before = File.ReadAllText(RepoLayout.BaselinePath);
        var @out = new StringWriter();
        var err = new StringWriter();

        var code = await EvalCli.RunAsync(["baseline"], @out, err);

        Assert.Equal(2, code);
        Assert.Contains("--justification", err.ToString());
        Assert.Equal(before, File.ReadAllText(RepoLayout.BaselinePath));
    }

    [Fact]
    public async Task The_committed_baseline_carries_its_pins_a_justification_and_explicit_tolerances()
    {
        var b = Baseline.Load();

        Assert.False(string.IsNullOrWhiteSpace(b.Justification));
        Assert.Equal(Corpus.SpecVersion(), b.SpecVersion);
        Assert.Equal("mock", b.Profile);
        Assert.True(b.Tolerances.ContainsKey("default"));
        await Task.CompletedTask;
    }
}
