using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Reporting;
using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Stats;
using static ExitInterviewAgent.Eval.Tests.Support.Fixtures;

namespace ExitInterviewAgent.Eval.Tests;

public class Layer1Tests
{
    [Fact]
    public async Task Every_run_of_every_committed_scenario_passes_every_assertion_with_the_mock_profile()
    {
        var run = await FullMockRun();

        Assert.Empty(run.Errors);
        Assert.Equal(All.Sum(l => l.Scenario.EffectiveSeeds.Count), run.Grades.Count);
        Assert.All(run.Grades, g => Assert.True(g.Passed, $"{g.ScenarioId}#{g.Seed}: " + string.Join("; ", g.Failures.Select(f => f.Id + " " + f.Message))));
    }

    [Fact]
    public async Task The_thirteen_constraints_are_evaluated_on_every_run_whatever_the_scenario_gate()
    {
        var run = await FullMockRun();

        Assert.All(run.Grades, g =>
        {
            for (var i = 1; i <= 13; i++) Assert.Contains(g.Assertions, a => a.Id == $"L1.C-{i:D2}");
        });
    }

    [Fact]
    public async Task Every_constraint_is_actually_applicable_somewhere_so_none_is_green_by_being_always_not_applicable()
    {
        var run = await FullMockRun();

        for (var i = 1; i <= 12; i++)
        {
            var id = $"L1.C-{i:D2}";
            Assert.Contains(run.Grades.SelectMany(g => g.Assertions), a => a.Id == id && a.Verdict == Verdict.Pass);
        }
    }

    [Fact]
    public async Task Failure_messages_carry_ids_and_counts_never_interview_text()
    {
        var run = await FullMockRun();
        var texts = run.Runs.SelectMany(r => r.Result.Transcript?.Turns.Select(t => t.Text) ?? []).Where(t => t.Length > 20).Distinct().ToList();
        var messages = run.Grades.SelectMany(g => g.Assertions).Select(a => a.Message).Distinct().ToList();

        Assert.NotEmpty(texts);
        Assert.All(messages, m => Assert.DoesNotContain(texts, t => m.Contains(t, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_corpus_measures_every_gated_behaviour_metric_with_a_non_empty_denominator()
    {
        var run = await FullMockRun();

        foreach (var def in MetricCatalog.All.Where(m => m.Gated))
            Assert.True(Evaluation.Pool(run.Grades, def.Id).N > 0, $"metric {def.Id} has no observations in the corpus");
    }

    [Fact]
    public async Task The_counter_metrics_are_measured_in_the_same_runs_as_their_primaries()
    {
        var run = await FullMockRun();

        Assert.True(Evaluation.Pool(run.Grades, "opr").N > 0, "fuv without opr would reward interrogating everything");
        Assert.True(Evaluation.Pool(run.Grades, "depth").N > 0, "coverage without depth would reward rushing");
        Assert.True(Evaluation.Pool(run.Grades, "qs").N > 0, "tf without qs would reward emitting no quotes");
    }

    [Fact]
    public async Task Leading_question_rate_uses_two_rule_sets_and_reports_where_they_disagree()
    {
        var run = await FullMockRun();

        Assert.True(Evaluation.Pool(run.Grades, "lqr.guard_rules").N > 0);
        Assert.True(Evaluation.Pool(run.Grades, "lqr.independent_rules").N > 0);
        Assert.Equal(Evaluation.Pool(run.Grades, "lqr").N, Evaluation.Pool(run.Grades, "lqr.rules_disagree").N);
    }

    [Fact]
    public void The_independent_rules_are_not_the_guard_and_catch_what_the_guard_misses()
    {
        // The guard lets a double-barrelled question through; the independent rules flag it (reported, not "leading").
        var q = "What was onboarding like and how did your manager treat you?";

        Assert.Null(ExitInterviewAgent.Agent.Roles.QuestionGuard.LeadingReason(q));
        Assert.True(IndependentRules.IsDoubleBarrelled(q));
        // And the other way round: tag questions both catch, 'can you' requests neither flags.
        Assert.True(IndependentRules.IsLeading("You must have been frustrated, weren't you?"));
        Assert.False(IndependentRules.IsLeading("Can you tell me more about how reviews worked?"));
    }

    [Fact]
    public void The_protocols_own_wording_is_graded_and_none_of_its_six_topic_questions_is_double_barrelled_or_leading()
    {
        // Was a recorded finding (docs/eval/METHODOLOGY.md): management and culture were two questions in one; protocol 1.1 asks one thing each (ADR-0062).
        var flagged = ExitInterviewAgent.Agent.Protocol.InterviewProtocol.Current.Topics.Where(t => IndependentRules.IsDoubleBarrelled(t.Question)).Select(t => t.Id).ToList();

        Assert.Empty(flagged);
        Assert.All(ExitInterviewAgent.Agent.Protocol.InterviewProtocol.Current.Topics, t => Assert.False(IndependentRules.IsLeading(t.Question)));
    }

    [Fact]
    public void The_operation_table_has_no_write_classified_operation_and_names_every_span_the_agent_emits()
    {
        Assert.Empty(OperationTable.WriteClassified);
        foreach (var name in typeof(ExitInterviewAgent.Agent.Tracing.InterviewTelemetry.Spans).GetFields().Select(f => (string)f.GetRawConstantValue()!).Where(n => n != "chat"))
            Assert.Contains(name, OperationTable.Spans);
    }

    [Fact]
    public void The_field_word_lists_equal_the_repositorys_forbidden_field_names_so_the_two_cannot_drift()
    {
        var mine = FieldWords.Affect.Concat(FieldWords.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(ExitInterviewAgent.TestSupport.ForbiddenFieldNames.Words.OrderBy(x => x), mine.OrderBy(x => x));
    }

    [Fact]
    public async Task Each_scenario_whose_expectations_pass_also_proves_its_absences_by_trace()
    {
        var run = await FullMockRun();
        var withdrawn = run.Runs.First(r => r.Scenario.Class == "consent");

        Assert.Empty(withdrawn.Trace.Named("interview.extraction"));
        Assert.Null(withdrawn.Result.Record);
        Assert.All(run.Grades.Where(g => g.ScenarioId.StartsWith("con-", StringComparison.Ordinal)), g => Assert.Contains(g.Assertions, a => a.Id.StartsWith("L1.X.absent_span.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Fuv_is_computed_from_the_hand_labels_and_the_vague_persona_is_fully_probed()
    {
        var run = await FullMockRun();
        var vague = Evaluation.Pool(run.Grades, "fuv", g => g.ScenarioId.StartsWith("amb-001", StringComparison.Ordinal));

        Assert.Equal(new Count(18, 18), vague);
    }
}
