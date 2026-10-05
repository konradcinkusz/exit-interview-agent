using System.Text.Json.Nodes;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Eval.Reporting;
using static ExitInterviewAgent.Eval.Tests.Support.Fixtures;

namespace ExitInterviewAgent.Eval.Tests;

public class DeterminismTests
{
    private static async Task<string> DeterministicReport()
    {
        var run = await Evaluation.RunProfileAsync(ModelProfile.Mock, All, Labels);
        var l2 = await Layer2Run.RunAsync(run.Runs, null);
        return ConformanceReport.Serialize(ConformanceReport.ToJson(new ReportInput("1.0.0", ExitInterviewAgent.Eval.Scenarios.Corpus.Digest(All), All, [run], l2, null, "test", true)));
    }

    [Fact]
    public async Task Two_complete_runs_with_a_fixed_seed_produce_byte_identical_deterministic_reports()
    {
        var a = await DeterministicReport();
        var b = await DeterministicReport();

        Assert.Equal(a, b);
        Assert.DoesNotContain("volatile", a);
    }

    [Fact]
    public async Task The_same_scenario_and_seed_give_the_same_transcript_record_and_span_names_but_a_different_seed_can_differ()
    {
        var a = await RunOne("hap-001", 1);
        var b = await RunOne("hap-001", 1);
        var c = await RunOne("hap-001", 2);

        Assert.Equal(a.Result.RecordJson, b.Result.RecordJson);
        Assert.Equal(a.Result.Transcript!.Render(), b.Result.Transcript!.Render());
        Assert.Equal(a.Trace.Spans.Select(s => s.Name), b.Trace.Spans.Select(s => s.Name));
        Assert.NotEqual(a.Result.Transcript.Render(), c.Result.Transcript!.Render());
    }

    [Fact]
    public async Task The_volatile_latency_block_exists_only_when_not_deterministic()
    {
        var run = await FullMockRun();
        var l2 = await Layer2Run.RunAsync(run.Runs, null);
        var withLatency = ConformanceReport.ToJson(new ReportInput("1.0.0", "d", All, [run], l2, null, "test", false));
        var without = ConformanceReport.ToJson(new ReportInput("1.0.0", "d", All, [run], l2, null, "test", true));

        Assert.NotNull(withLatency["volatile"]);
        Assert.Null(without["volatile"]);
        Assert.Contains("VOLATILE", withLatency["volatile"]!["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task Scenarios_are_isolated_the_same_run_after_other_scenarios_is_unchanged()
    {
        await RunOne("adv-001", 1);
        await RunOne("deg-008", 1);
        var after = await RunOne("hap-001", 1);
        var alone = await ScenarioRunner.RunAsync(Scenario("hap-001"), 1, ModelProfile.Mock);

        Assert.Equal(alone.Run!.Result.RecordJson, after.Result.RecordJson);
        Assert.Equal(alone.Run.Trace.Spans.Count, after.Trace.Spans.Count);
    }
}
