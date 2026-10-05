using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Eval.Reporting;
using ExitInterviewAgent.Eval.Scenarios;

namespace ExitInterviewAgent.Eval.Tests.Support;

/// <summary>The committed corpus and one full mock run of it, computed once per test process (the run is deterministic and takes about a second).</summary>
internal static class Fixtures
{
    public static readonly IReadOnlyList<LoadedScenario> All = ScenarioLoader.LoadAll();

    public static readonly IReadOnlyDictionary<string, string> Labels = LabelSets.ReplyIndex(LabelSets.LoadVagueness());

    private static readonly Lazy<Task<ProfileRun>> Full = new(() => Evaluation.RunProfileAsync(ModelProfile.Mock, All, Labels));

    public static Task<ProfileRun> FullMockRun() => Full.Value;

    public static Scenario Scenario(string shortId) => All.Single(l => ExitInterviewAgent.Eval.Scenarios.Corpus.ShortId(l.Id) == shortId).Scenario;

    public static async Task<RunRecord> RunOne(string shortId, int seed = 1)
    {
        var outcome = await ScenarioRunner.RunAsync(Scenario(shortId), seed, ModelProfile.Mock);
        Assert.Null(outcome.Error);
        return outcome.Run!;
    }

    public static RunGrade Grade(RunRecord run) => Layer1Grader.Grade(run, Labels);

    public static AssertionResult Result(RunGrade g, string id) => g.Assertions.Single(a => a.Id == id);
}
