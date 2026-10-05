namespace ExitInterviewAgent.Signals;

/// <summary>The mean of a cell and a 95% interval for it, from the counts of the five rating levels.</summary>
public readonly record struct MeanEstimate(int N, double Mean, double Lower, double Upper);

/// <summary>
/// The interval for the mean of a small ordinal sample (ADR-0054). A Student t interval whose variance is regularised towards a
/// prior, clipped to the scale:
/// <code>
///   s2   = unbiased sample variance of the ratings
///   s2r  = (v0 * s0sq + (n - 1) * s2) / (v0 + n - 1)          v0 = PriorWeight, s0sq = PriorVariance
///   half = t(0.975; v0 + n - 1) * sqrt(s2r / n)
///   interval = [mean - half, mean + half] clipped to [1, 5], rounded outwards to two decimals
/// </code>
/// The prior is what stops a sample of identical ratings (s2 = 0) from producing a zero-width interval: with n = 5 identical
/// ratings the interval is about 3.8 to 5.0, not a point. It is not a claim that ratings really vary that much, and it washes out as n
/// grows. The constants are tuned against synthetic distributions by <c>IntervalCoverageTests</c>; nothing here has been checked
/// against real ratings (none exist). Deterministic: no random numbers, so a snapshot can be reproduced from its input.
/// </summary>
public static class MeanInterval
{
    public const double Level = 0.95;
    public const string Method = "regularised-t-v1";

    /// <summary>Prior weight (pseudo-degrees of freedom).</summary>
    public const double PriorWeight = 6;

    /// <summary>Prior variance of one rating on the 1-5 scale. The largest possible variance is 4 (all ratings at the two ends).</summary>
    public const double PriorVariance = 2.5;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, double> Critical = new();

    /// <summary>The t critical value for a cell of <paramref name="n"/> ratings. The degrees of freedom are integers, so the value is cached by n.</summary>
    private static double CriticalValue(int n)
        => n > 5000 ? StudentT.Quantile975(PriorWeight + n - 1) : Critical.GetOrAdd(n, k => StudentT.Quantile975(PriorWeight + k - 1));

    public static MeanEstimate Estimate(ReadOnlySpan<int> countsByLevel)
    {
        if (countsByLevel.Length != Vocabulary.MaxRating)
        {
            throw new ArgumentException("Counts for the five levels 1-5 are expected.", nameof(countsByLevel));
        }
        var n = 0;
        double sum = 0;
        for (var i = 0; i < countsByLevel.Length; i++)
        {
            n += countsByLevel[i];
            sum += (double)(i + 1) * countsByLevel[i];
        }
        if (n == 0)
        {
            throw new ArgumentException("An empty cell has no mean.", nameof(countsByLevel));
        }
        var mean = sum / n;
        double sumSquares = 0;
        for (var i = 0; i < countsByLevel.Length; i++)
        {
            var d = i + 1 - mean;
            sumSquares += countsByLevel[i] * d * d;
        }
        var sampleVariance = n > 1 ? sumSquares / (n - 1) : 0;
        var df = PriorWeight + n - 1;
        var regularised = (PriorWeight * PriorVariance + (n - 1) * sampleVariance) / df;
        var half = CriticalValue(n) * Math.Sqrt(regularised / n);
        return new MeanEstimate(
            n,
            Math.Round(mean, 2, MidpointRounding.AwayFromZero),
            Math.Max(Vocabulary.MinRating, Math.Floor((mean - half) * 100) / 100),
            Math.Min(Vocabulary.MaxRating, Math.Ceiling((mean + half) * 100) / 100));
    }
}
