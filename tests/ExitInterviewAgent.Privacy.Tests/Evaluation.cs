using System.Text;

namespace ExitInterviewAgent.Privacy.Tests;

internal sealed record Scores(
    int GoldSpans, int Caught, int FullyMasked, int Findings, int TruePositiveFindings, int FalsePositiveFindings,
    IReadOnlyDictionary<string, (int Gold, int Caught)> PerGoldKind,
    IReadOnlyDictionary<PiiKind, (int Findings, int TruePositives)> PerFoundKind,
    IReadOnlyList<string> Misses, IReadOnlyList<string> FalsePositives)
{
    public double Recall => GoldSpans == 0 ? 1 : (double)Caught / GoldSpans;
    public double FullRecall => GoldSpans == 0 ? 1 : (double)FullyMasked / GoldSpans;
    public double Precision => Findings == 0 ? 1 : (double)TruePositiveFindings / Findings;
}

/// <summary>
/// Scoring. A gold span is "caught" when findings cover at least half of its characters, "fully masked" when they cover
/// all of them (anything less leaves part of the personal data in the text). A finding is a true positive when it
/// overlaps any gold span by at least one character, otherwise a false positive. Kind is not required to agree:
/// the guard's job is that the data is masked; the kind is reported separately.
/// </summary>
internal static class Evaluation
{
    public static Scores Score(IReadOnlyList<Sample> samples, PiiDetector detector)
    {
        int goldSpans = 0, caught = 0, full = 0, findings = 0, tp = 0, fp = 0;
        var perGold = new Dictionary<string, (int, int)>();
        var perFound = new Dictionary<PiiKind, (int, int)>();
        var misses = new List<string>();
        var falsePositives = new List<string>();

        foreach (var s in samples)
        {
            var found = detector.Detect(s.Text);
            foreach (var g in s.Gold)
            {
                var covered = 0;
                for (var i = g.Start; i < g.End; i++)
                    if (found.Any(f => i >= f.Start && i < f.End)) covered++;
                goldSpans++;
                var isCaught = covered * 2 >= g.Length;
                if (isCaught) caught++;
                else misses.Add($"line {s.Line}: {g.Kind} [{g.Start},{g.End})");
                if (covered == g.Length) full++;
                var (gn, gc) = perGold.GetValueOrDefault(g.Kind);
                perGold[g.Kind] = (gn + 1, gc + (isCaught ? 1 : 0));
            }

            foreach (var f in found)
            {
                findings++;
                var hit = s.Gold.Any(g => f.Start < g.End && g.Start < f.End);
                if (hit) tp++;
                else { fp++; falsePositives.Add($"line {s.Line}: {f.Kind} [{f.Start},{f.End})"); }
                var (fn, ft) = perFound.GetValueOrDefault(f.Kind);
                perFound[f.Kind] = (fn + 1, ft + (hit ? 1 : 0));
            }
        }

        return new Scores(goldSpans, caught, full, findings, tp, fp, perGold, perFound, misses, falsePositives);
    }

    /// <summary>95% Wilson score interval for a proportion: honest about how little a few dozen spans can show.</summary>
    public static (double Low, double High) Wilson(int successes, int total)
    {
        if (total == 0) return (0, 1);
        const double z = 1.96;
        var p = (double)successes / total;
        var denom = 1 + z * z / total;
        var centre = (p + z * z / (2 * total)) / denom;
        var half = z * Math.Sqrt(p * (1 - p) / total + z * z / (4.0 * total * total)) / denom;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    public static string Report(string title, Scores s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {title}");
        var (rl, rh) = Wilson(s.Caught, s.GoldSpans);
        var (fl, fh) = Wilson(s.FullyMasked, s.GoldSpans);
        var (pl, ph) = Wilson(s.TruePositiveFindings, s.Findings);
        sb.AppendLine($"gold spans {s.GoldSpans} | caught {s.Caught} (recall {s.Recall:P1}, 95% CI {rl:P0}-{rh:P0}) | fully masked {s.FullyMasked} (full recall {s.FullRecall:P1}, 95% CI {fl:P0}-{fh:P0})");
        sb.AppendLine($"findings {s.Findings} | true positive {s.TruePositiveFindings} | false positive {s.FalsePositiveFindings} (precision {s.Precision:P1}, 95% CI {pl:P0}-{ph:P0})");
        sb.AppendLine("recall by gold kind: " + string.Join(", ", s.PerGoldKind.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.Caught}/{k.Value.Gold}")));
        sb.AppendLine("precision by found kind: " + string.Join(", ", s.PerFoundKind.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.TruePositives}/{k.Value.Findings}")));
        sb.AppendLine("misses: " + (s.Misses.Count == 0 ? "none" : string.Join("; ", s.Misses)));
        sb.AppendLine("false positives: " + (s.FalsePositives.Count == 0 ? "none" : string.Join("; ", s.FalsePositives)));
        return sb.ToString();
    }
}
