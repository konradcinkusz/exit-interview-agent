using Xunit.Abstractions;

namespace ExitInterviewAgent.Privacy.Tests;

/// <summary>
/// Reproduces the numbers in docs/privacy/pii-detector.md:
/// <c>dotnet test tests/ExitInterviewAgent.Privacy.Tests --filter "Category=PiiEvaluation" --logger "console;verbosity=detailed"</c>.
/// The floors below sit a few points under the measured values, so a regression fails the build.
/// </summary>
[Trait("Category", "PiiEvaluation")]
public class EvaluationTests(ITestOutputHelper output)
{
    private static readonly string[] Allow = ["Zephyrix", "Orbit Suite"];

    private static PiiDetector Default => new(new PiiOptions { AllowList = Allow });
    private static PiiDetector Strict => new(new PiiOptions { AllowList = Allow, FailClosed = true });

    private Scores Run(string title, string corpus, PiiDetector detector)
    {
        var scores = Evaluation.Score(Corpus.Load(corpus), detector);
        output.WriteLine(Evaluation.Report(title, scores));
        return scores;
    }

    [Fact]
    public void Dev_corpus_default_mode()
    {
        var s = Run("dev / default", "dev.txt", Default);
        Assert.True(s.Recall >= Floors.DevRecall, $"recall {s.Recall:P1}");
        Assert.True(s.Precision >= Floors.DevPrecision, $"precision {s.Precision:P1}");
    }

    [Fact]
    public void Dev_corpus_fail_closed_mode()
    {
        var s = Run("dev / fail-closed", "dev.txt", Strict);
        Assert.True(s.Recall >= Floors.DevStrictRecall, $"recall {s.Recall:P1}");
        Assert.True(s.Precision >= Floors.DevStrictPrecision, $"precision {s.Precision:P1}");
    }

    [Fact]
    public void Heldout_corpus_default_mode()
    {
        var s = Run("held-out / default", "heldout.txt", Default);
        Assert.True(s.Recall >= Floors.HeldoutRecall, $"recall {s.Recall:P1}");
        Assert.True(s.Precision >= Floors.HeldoutPrecision, $"precision {s.Precision:P1}");
    }

    [Fact]
    public void Heldout_corpus_fail_closed_mode()
    {
        var s = Run("held-out / fail-closed", "heldout.txt", Strict);
        Assert.True(s.Recall >= Floors.HeldoutStrictRecall, $"recall {s.Recall:P1}");
        Assert.True(s.Precision >= Floors.HeldoutStrictPrecision, $"precision {s.Precision:P1}");
    }

    [Fact]
    public void Fail_closed_never_masks_less_than_default()
    {
        foreach (var corpus in new[] { "dev.txt", "heldout.txt" })
            foreach (var sample in Corpus.Load(corpus))
            {
                var open = Default.Detect(sample.Text);
                var closed = Strict.Detect(sample.Text);
                for (var i = 0; i < sample.Text.Length; i++)
                    if (open.Any(f => i >= f.Start && i < f.End))
                        Assert.True(closed.Any(f => i >= f.Start && i < f.End), $"{corpus} line {sample.Line}: offset {i} masked by default but not fail-closed");
            }
    }

    [Fact]
    public void The_evaluation_can_fail_an_unmatched_span_is_a_miss_and_a_stray_finding_is_a_false_positive()
    {
        var miss = Evaluation.Score([new Sample(1, "Call 123", [new Gold("phone", 5, 3)])], new PiiDetector());
        var stray = Evaluation.Score([new Sample(1, "Mail a@b.example now", [])], new PiiDetector());

        Assert.Equal(0, miss.Caught);
        Assert.Single(miss.Misses);
        Assert.Equal(0.0, miss.Recall);
        Assert.Equal(1, stray.FalsePositiveFindings);
        Assert.Equal(0.0, stray.Precision);
    }

    [Fact]
    public void Corpus_markup_yields_offsets_in_the_unmarked_text()
    {
        var s = Corpus.Parse(1, "Hi {{person:Anna}} and {{email:a@b.example}}!");

        Assert.Equal("Hi Anna and a@b.example!", s.Text);
        Assert.Equal("Anna", s.Text.Substring(s.Gold[0].Start, s.Gold[0].Length));
        Assert.Equal("a@b.example", s.Text.Substring(s.Gold[1].Start, s.Gold[1].Length));
    }

    /// <summary>Set to the measured value minus a margin; raise them when the detector improves, never lower them to pass.</summary>
    private static class Floors
    {
        public const double DevRecall = 0.95, DevPrecision = 0.95, DevStrictRecall = 0.95, DevStrictPrecision = 0.85;
        public const double HeldoutRecall = 0.90, HeldoutPrecision = 0.90, HeldoutStrictRecall = 0.95, HeldoutStrictPrecision = 0.85;
    }
}
