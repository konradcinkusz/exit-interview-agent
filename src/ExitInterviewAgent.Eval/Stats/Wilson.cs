namespace ExitInterviewAgent.Eval.Stats;

/// <summary>k successes out of n observations, with the 95% Wilson score interval. The pair travels together everywhere: a bare rate is never reported.</summary>
public readonly record struct Count(int K, int N)
{
    public const double Z95 = 1.959963984540054;

    /// <summary>The point estimate, or null when there are no observations (undefined, never 1.0 and never 0.0).</summary>
    public double? Rate => N == 0 ? null : (double)K / N;

    public (double Low, double High)? Interval => Wilson(K, N);

    public static Count operator +(Count a, Count b) => new(a.K + b.K, a.N + b.N);

    public static Count Zero => new(0, 0);

    public static (double Low, double High)? Wilson(int k, int n, double z = Z95)
    {
        if (n <= 0) return null;
        var p = (double)k / n;
        var z2 = z * z;
        var denom = 1 + z2 / n;
        var centre = (p + z2 / (2 * n)) / denom;
        var half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>True when the two intervals overlap, i.e. the data do not distinguish the two values.</summary>
    public static bool Indistinguishable(Count a, Count b) =>
        a.Interval is { } x && b.Interval is { } y ? x.Low <= y.High && y.Low <= x.High : true;

    public override string ToString() => N == 0 ? "n/a (n=0)" : FormattableString.Invariant($"{K}/{N} = {Rate:P1} [{Interval!.Value.Low:P1}, {Interval!.Value.High:P1}]");
}
