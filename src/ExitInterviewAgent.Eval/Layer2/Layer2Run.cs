using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Layer2;

public sealed record ScoredItem(string ScenarioId, int Seed, string Rubric, int ItemIndex, JudgeScore Score);

public sealed record InjectionCheck(string ScenarioId, int Seed, int Items, int Moved);

public sealed record Layer2Result(
    string Status, string? JudgeModelConfigured, string? JudgeModelAnswering, string RubricSha, string PromptSha,
    IReadOnlyList<ScoredItem> Items, IReadOnlyList<InjectionCheck> InjectionChecks, GateDecision Gate, IReadOnlyList<AgreementReport> RuleScreens, AgreementReport? JudgeAgreement)
{
    public IEnumerable<ScoredItem> ValidScores => Items.Where(i => i.Score.Score is not null);
}

/// <summary>Layer 2 over a profile's runs: the judge scores the questions of every scenario that names rubrics, plus the injection differential. Without a judge it reports <c>skipped:*</c>, never a pass.</summary>
public static class Layer2Run
{
    public static IEnumerable<JudgeInput> ItemsOf(RunRecord run, string rubric)
    {
        var turns = run.Result.Transcript?.Turns ?? [];
        for (var i = 0; i < turns.Count; i++)
        {
            var t = turns[i];
            if (t.Speaker != Speaker.Interviewer || t.Kind is not (TurnKind.Topic or TurnKind.Probe or TurnKind.Clarification or TurnKind.Redirect)) continue;
            if (rubric == "R-02" && t.Kind != TurnKind.Probe) continue;
            var before = turns.Take(i).LastOrDefault(x => x.Speaker == Speaker.Interviewee)?.Text ?? "";
            yield return new JudgeInput(rubric, t.Kind.ToString().ToLowerInvariant(), t.Text, before);
        }
    }

    public static async Task<Layer2Result> RunAsync(IReadOnlyList<RunRecord> runs, ModelProfile? judgeProfile, CancellationToken ct = default)
    {
        var rubric = Rubric.Load();
        var (rubricSha, promptSha) = Rubric.Hashes();
        var labels = LabelSets.LoadJudge();
        var screens = Calibration.RuleScreensVsLabels(labels);
        if (judgeProfile is not { Runnable: true })
        {
            var why = judgeProfile?.SkipReason ?? "skipped:no-credential (no judge profile is declared in evals/profiles.yaml)";
            return new Layer2Result(why, judgeProfile?.ModelId, null, rubricSha, promptSha, [], [], Calibration.Decide(labels, rubric, null), screens, null);
        }

        var client = judgeProfile.Factory!();
        var judge = new RubricJudge(client, judgeProfile.ModelId, rubric);
        var items = new List<ScoredItem>();
        var checks = new List<InjectionCheck>();
        foreach (var run in runs.Where(r => r.Scenario.Rubrics is { Count: > 0 }))
        {
            foreach (var rubricId in run.Scenario.Rubrics!)
            {
                var idx = 0;
                var moved = 0;
                var checkedItems = 0;
                foreach (var input in ItemsOf(run, rubricId))
                {
                    items.Add(new ScoredItem(run.Scenario.Id, run.Seed, rubricId, idx++, await judge.JudgeAsync(input, ct).ConfigureAwait(false)));
                    if (run.Scenario.Control is not null && IndependentRulesMatch(input))
                    {
                        checkedItems++;
                        var (_, _, differs) = await judge.InjectionDifferentialAsync(input, Execution.ScenarioRunner.StripInjection, ct).ConfigureAwait(false);
                        if (differs) moved++;
                    }
                }
                if (checkedItems > 0) checks.Add(new InjectionCheck(run.Scenario.Id, run.Seed, checkedItems, moved));
            }
        }

        // Calibration: judge the labelled items themselves and compare.
        var judged = new Dictionary<string, int?>();
        foreach (var item in labels.Items)
            judged[item.Id] = (await judge.JudgeAsync(new JudgeInput(item.Rubric, item.Rubric == "R-02" ? "probe" : "question", item.Question, item.Preceded_By), ct).ConfigureAwait(false)).Score;
        var all = labels.Items.Where(i => judged[i.Id] is not null).Select(i => (judged[i.Id]!.Value, i.Label)).Select(p => (p.Item1, p.Label)).ToList();
        var agreement = AgreementReport.From("judge vs author labels, both rubrics (3 levels)", all, "unweighted kappa");
        return new Layer2Result("ran", judgeProfile.ModelId, judge.AnsweringModelId, rubricSha, promptSha, items, checks, Calibration.Decide(labels, rubric, agreement.Kappa), screens, agreement);
    }

    private static bool IndependentRulesMatch(JudgeInput i) => Layer1.IndependentRules.LooksLikeInstruction(i.PrecededBy) || Layer1.IndependentRules.LooksLikeInstruction(i.Question);
}
