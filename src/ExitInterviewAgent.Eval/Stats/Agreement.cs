namespace ExitInterviewAgent.Eval.Stats;

/// <summary>Cohen's kappa and a seeded bootstrap interval. Unweighted on purpose: one level out is a real disagreement on these anchors.</summary>
public static class Agreement
{
    /// <summary>Unweighted Cohen's kappa. Null (undefined, never 1.0) with fewer than two pairs or when expected agreement is 1.</summary>
    public static double? Kappa(IReadOnlyList<(int A, int B)> pairs)
    {
        if (pairs.Count < 2) return null;
        var n = (double)pairs.Count;
        var observed = pairs.Count(p => p.A == p.B) / n;
        var cats = pairs.SelectMany(p => new[] { p.A, p.B }).Distinct();
        var expected = cats.Sum(c => pairs.Count(p => p.A == c) / n * (pairs.Count(p => p.B == c) / n));
        return Math.Abs(1 - expected) < 1e-9 ? null : (observed - expected) / (1 - expected);
    }

    /// <summary>Percentile bootstrap over pairs, 95%, fixed seed (a fixed algorithm, so the interval is reproducible on every platform).</summary>
    public static (double Low, double High, int Defined)? KappaInterval(IReadOnlyList<(int A, int B)> pairs, int resamples = 2000, ulong seed = 20261005)
    {
        if (pairs.Count < 2) return null;
        var rng = new SplitMix(seed);
        var samples = new List<double>(resamples);
        var buffer = new (int A, int B)[pairs.Count];
        for (var r = 0; r < resamples; r++)
        {
            for (var i = 0; i < buffer.Length; i++) buffer[i] = pairs[(int)(rng.Next() % (ulong)pairs.Count)];
            if (Kappa(buffer) is { } k) samples.Add(k);
        }
        if (samples.Count < resamples / 2) return null;
        samples.Sort();
        return (samples[(int)(0.025 * (samples.Count - 1))], samples[(int)Math.Ceiling(0.975 * (samples.Count - 1))], samples.Count);
    }

    /// <summary>Mean and a seeded bootstrap interval for ordinal scores.</summary>
    public static (double Mean, double Low, double High)? MeanInterval(IReadOnlyList<int> scores, int resamples = 2000, ulong seed = 20261006)
    {
        if (scores.Count == 0) return null;
        var rng = new SplitMix(seed);
        var means = new List<double>(resamples);
        for (var r = 0; r < resamples; r++)
        {
            double sum = 0;
            for (var i = 0; i < scores.Count; i++) sum += scores[(int)(rng.Next() % (ulong)scores.Count)];
            means.Add(sum / scores.Count);
        }
        means.Sort();
        return (scores.Average(), means[(int)(0.025 * (means.Count - 1))], means[(int)Math.Ceiling(0.975 * (means.Count - 1))]);
    }

    private sealed class SplitMix(ulong state)
    {
        public ulong Next()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                var x = state;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }
    }
}
