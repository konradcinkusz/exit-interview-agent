using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Eval.Tests.Support;

namespace ExitInterviewAgent.Eval.Tests;

public class CalibrationTests
{
    private static JudgeLabelFile Labels(string kind, string who, int n = 48) => new()
    {
        Labeller = who,
        Labeller_Kind = kind,
        Items = Enumerable.Range(1, n).Select(i => new JudgeItem { Id = $"j-{i:D3}", Rubric = "R-01", Question = "q", Label = i % 3, Rationale = "r" }).ToList(),
    };

    private static Rubric RubricWith(string owner)
    {
        var r = Rubric.Load();
        r.Calibration.Owner_Handle = owner;
        return r;
    }

    [Fact]
    public void The_committed_judge_labels_are_author_labelled_and_the_gate_stays_shut()
    {
        var labels = LabelSets.LoadJudge();

        var d = Calibration.Decide(labels, Rubric.Load(), 0.95);

        Assert.Equal("ai-author", labels.Labeller_Kind);
        Assert.False(d.Gating);
        Assert.Equal(0, d.HumanLabels);
        Assert.Contains("0 of 40 human labels", d.Reason);
    }

    [Fact]
    public void Author_labels_never_count_towards_the_gate_even_with_a_perfect_kappa_and_enough_of_them()
    {
        var d = Calibration.Decide(Labels("ai-author", "author-ai"), RubricWith("author-ai"), 1.0);

        Assert.False(d.Gating);
        Assert.Equal(0, d.HumanLabels);
    }

    [Fact]
    public void Human_labels_under_the_owners_handle_with_enough_items_and_a_good_kappa_open_the_gate()
    {
        var d = Calibration.Decide(Labels("human", "owner-handle"), RubricWith("owner-handle"), 0.7);

        Assert.True(d.Gating, d.Reason);
        Assert.Equal(48, d.HumanLabels);
    }

    [Theory]
    [InlineData(0.59, false)]
    [InlineData(0.6, true)]
    [InlineData(null, false)]
    public void The_kappa_threshold_and_an_undefined_kappa_decide_the_gate(double? kappa, bool gating)
    {
        var d = Calibration.Decide(Labels("human", "owner-handle"), RubricWith("owner-handle"), kappa);

        Assert.Equal(gating, d.Gating);
    }

    [Fact]
    public void Too_few_human_labels_or_the_wrong_handle_keep_the_gate_shut()
    {
        Assert.False(Calibration.Decide(Labels("human", "owner-handle", 10), RubricWith("owner-handle"), 0.9).Gating);
        Assert.False(Calibration.Decide(Labels("human", "someone-else"), RubricWith("owner-handle"), 0.9).Gating);
        Assert.False(Calibration.Decide(Labels("human", "owner-handle"), RubricWith(""), 0.9).Gating);
    }

    [Fact]
    public void The_judge_label_set_is_well_formed_every_level_is_on_the_scale_with_a_rationale_and_both_rubrics_have_all_levels()
    {
        var labels = LabelSets.LoadJudge();
        var rubric = Rubric.Load();

        Assert.True(labels.Items.Count >= rubric.Calibration.Minimum_Labels);
        Assert.Equal(labels.Items.Count, labels.Items.Select(i => i.Id).Distinct().Count());
        Assert.All(labels.Items, i =>
        {
            Assert.InRange(i.Label, 0, rubric.Get(i.Rubric).Scale);
            Assert.False(string.IsNullOrWhiteSpace(i.Rationale));
        });
        foreach (var r in new[] { "R-01", "R-02" })
            Assert.Equal([0, 1, 2], labels.Items.Where(i => i.Rubric == r).Select(i => i.Label).Distinct().Order().ToList());
    }

    [Fact]
    public void The_rule_screens_agreement_with_the_labels_is_computed_offline_with_a_confidence_interval()
    {
        var reports = Calibration.RuleScreensVsLabels(LabelSets.LoadJudge());

        Assert.Equal(2, reports.Count);
        Assert.All(reports, r =>
        {
            Assert.NotNull(r.Kappa);
            Assert.NotNull(r.KappaLow);
            Assert.True(r.KappaLow <= r.Kappa && r.Kappa <= r.KappaHigh);
        });
    }

    [Fact]
    public void Judge_versus_label_agreement_uses_three_levels_and_unweighted_kappa()
    {
        var labels = LabelSets.LoadJudge();
        var perfect = labels.Items.ToDictionary(i => i.Id, i => (int?)i.Label);
        var off = labels.Items.ToDictionary(i => i.Id, i => (int?)((i.Label + 1) % 3));

        Assert.Equal(1.0, Calibration.JudgeVsLabels(labels, perfect, "R-01").Kappa!.Value, 9);
        Assert.True(Calibration.JudgeVsLabels(labels, off, "R-01").Kappa!.Value < 0.2);
    }

    // ---- the vagueness / contradiction experiment -------------------------------------------------------------------------

    [Fact]
    public void Every_vagueness_label_is_a_known_class_with_a_rationale_and_the_set_covers_all_three_classes()
    {
        var f = LabelSets.LoadVagueness();

        Assert.Equal("ai-author", f.Labeller_Kind);
        Assert.Equal(["decline", "specific", "vague"], f.Replies.Select(r => r.Label).Distinct().Order().ToList());
        Assert.All(f.Replies, r => Assert.False(string.IsNullOrWhiteSpace(r.Rationale)));
        Assert.Equal(["consistent", "contradiction"], f.Pairs.Select(p => p.Label).Distinct().Order().ToList());
    }

    [Fact]
    public void The_analyser_experiment_computes_a_confusion_matrix_recall_precision_and_kappa_with_intervals_offline()
    {
        var f = LabelSets.LoadVagueness();

        var r = ClassifierExperiment.VaguenessAnalyzer(f);

        Assert.Equal(f.Replies.Count, r.N);
        Assert.Equal(f.Replies.Count, r.Confusion!.Total);
        Assert.NotNull(r.Recall!.Value.Rate);
        Assert.NotNull(r.Precision!.Value.Rate);
        Assert.NotNull(r.Agreement!.KappaLow);
        Assert.Equal(f.Replies.Count(x => x.Label == "vague"), r.Recall.Value.N);
    }

    [Fact]
    public void The_contradiction_analyser_is_scored_against_the_labelled_pairs()
    {
        var f = LabelSets.LoadVagueness();

        var r = ClassifierExperiment.ContradictionAnalyzer(f);

        Assert.Equal(f.Pairs.Count, r.N);
        Assert.Equal(f.Pairs.Count(p => p.Label == "contradiction"), r.Recall!.Value.N);
    }

    [Fact]
    public async Task The_model_assisted_classifier_runs_on_the_same_items_with_strict_parsing_and_untrusted_input()
    {
        var f = LabelSets.LoadVagueness();
        var fake = new FakeModel((system, user) => system.Contains("TWO replies", StringComparison.Ordinal) ? "{\"label\": \"consistent\"}" : "{\"label\": \"vague\"}");

        var reports = await new ModelClassifier(fake, "m").RunAsync(f);

        Assert.Equal(2, reports.Count);
        Assert.Equal(f.Replies.Count, reports[0].N);
        Assert.Equal(f.Pairs.Count, reports[1].N);
        Assert.All(fake.Calls, c => Assert.Contains("<<<DATA ", c.User));
    }

    [Theory]
    [InlineData("{\"label\": \"vague\"}", "vague")]
    [InlineData("vague", null)]
    [InlineData("{\"label\": \"maybe\"}", null)]
    [InlineData("{\"label\": \"vague\", \"x\": 1}", null)]
    public void The_model_classifier_reply_is_parsed_strictly(string reply, string? expected) =>
        Assert.Equal(expected, ModelClassifier.Parse(reply, "vague", "specific", "decline"));
}
