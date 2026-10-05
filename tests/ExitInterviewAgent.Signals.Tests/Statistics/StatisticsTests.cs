namespace ExitInterviewAgent.Signals.Tests.Statistics;

public sealed class StudentTTests
{
    // The textbook table of two-sided 95% critical values (97.5th percentiles).
    [Theory]
    [InlineData(1, 12.706)]
    [InlineData(2, 4.303)]
    [InlineData(4, 2.776)]
    [InlineData(5, 2.571)]
    [InlineData(9, 2.262)]
    [InlineData(10, 2.228)]
    [InlineData(20, 2.086)]
    [InlineData(30, 2.042)]
    [InlineData(60, 2.000)]
    [InlineData(120, 1.980)]
    public void Quantile975_matches_the_published_table(int df, double expected)
        => Assert.Equal(expected, StudentT.Quantile975(df), 3);

    [Fact]
    public void Quantile_approaches_the_normal_for_large_degrees_of_freedom()
        => Assert.Equal(1.95996, StudentT.Quantile975(100000), 3);

    [Fact]
    public void Cdf_is_symmetric_and_monotone()
    {
        Assert.Equal(0.5, StudentT.Cdf(0, 7), 12);
        Assert.Equal(1 - StudentT.Cdf(1.3, 7), StudentT.Cdf(-1.3, 7), 12);
        Assert.True(StudentT.Cdf(2, 7) > StudentT.Cdf(1, 7));
    }

    [Fact]
    public void Quantile_rejects_nonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StudentT.Quantile(1.0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => StudentT.Quantile(0.9, 0));
    }
}

public sealed class MeanIntervalTests
{
    private static MeanEstimate Of(params int[] ratings)
    {
        var counts = new int[5];
        foreach (var r in ratings)
        {
            counts[r - 1]++;
        }
        return MeanInterval.Estimate(counts);
    }

    [Fact]
    public void Identical_ratings_do_not_give_a_falsely_precise_interval()
    {
        var unanimous = Of(5, 5, 5, 5, 5);

        Assert.Equal(5.0, unanimous.Mean);
        Assert.Equal(5.0, unanimous.Upper);        // clipped to the scale
        Assert.InRange(unanimous.Lower, 3.5, 4.0); // not a point: about 3.8 with the documented prior
    }

    [Fact]
    public void Identical_ratings_in_the_middle_still_have_a_wide_interval()
    {
        var threes = Of(3, 3, 3, 3, 3, 3);

        Assert.Equal(3.0, threes.Mean);
        Assert.True(threes.Upper - threes.Lower > 1.0, "six identical ratings must not read as certainty");
    }

    [Fact]
    public void The_interval_narrows_as_the_sample_grows()
    {
        double Width(int n) { var e = Of([.. Enumerable.Range(0, n).Select(i => 2 + i % 3)]); return e.Upper - e.Lower; }

        Assert.True(Width(5) > Width(10));
        Assert.True(Width(10) > Width(40));
        Assert.True(Width(40) > Width(400));
    }

    [Fact]
    public void The_interval_contains_the_mean_and_stays_on_the_scale()
    {
        foreach (var ratings in new[] { new[] { 1, 1, 1, 1, 1 }, [5, 5, 5, 5, 5], [1, 5, 1, 5, 1, 5], [2, 3, 4, 3, 2, 4, 3] })
        {
            var e = Of(ratings);
            Assert.InRange(e.Mean, e.Lower, e.Upper);
            Assert.InRange(e.Lower, 1.0, 5.0);
            Assert.InRange(e.Upper, 1.0, 5.0);
        }
    }

    [Fact]
    public void The_estimate_is_deterministic()
        => Assert.Equal(Of(1, 2, 3, 4, 5, 5), Of(5, 5, 4, 3, 2, 1));

    [Fact]
    public void Counts_of_the_wrong_shape_or_an_empty_cell_are_refused()
    {
        Assert.Throws<ArgumentException>(() => MeanInterval.Estimate(new int[4]));
        Assert.Throws<ArgumentException>(() => MeanInterval.Estimate(new int[5]));
    }
}

/// <summary>
/// The interval is not exact for a bounded ordinal variable, so its coverage is MEASURED, on synthetic distributions with a fixed seed,
/// and the table in docs/privacy/AGGREGATION.md is what this test prints. The assertions are the floor the documentation promises.
/// </summary>
public sealed class IntervalCoverageTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly (string Name, double[] P)[] Battery =
    [
        ("uniform", [.2, .2, .2, .2, .2]),
        ("skewed-high", [.02, .05, .13, .35, .45]),
        ("skewed-low", [.45, .35, .13, .05, .02]),
        ("polarised", [.4, .05, .1, .05, .4]),
        ("extreme-50-50", [.5, 0, 0, 0, .5]),
        ("concentrated", [0, 0, .05, .9, .05]),
        ("peaked-3", [.1, .2, .4, .2, .1]),
        ("near-degenerate", [0, 0, 0, .05, .95]),
        ("degenerate", [0, 0, 0, 0, 1]),
        ("rare-low-tail", [.1, 0, 0, .1, .8]),
    ];

    public static double Coverage(double[] p, int n, int reps, int seed, bool regularised = true)
    {
        var rng = new Random(seed);
        var truth = Enumerable.Range(0, 5).Sum(i => (i + 1) * p[i]);
        var covered = 0;
        for (var r = 0; r < reps; r++)
        {
            var counts = new int[5];
            for (var i = 0; i < n; i++)
            {
                var u = rng.NextDouble();
                var acc = 0.0;
                var level = 0;
                for (; level < 4; level++)
                {
                    acc += p[level];
                    if (u < acc)
                    {
                        break;
                    }
                }
                counts[level]++;
            }
            double lower, upper;
            if (regularised)
            {
                var e = MeanInterval.Estimate(counts);
                (lower, upper) = (e.Lower, e.Upper);
            }
            else
            {
                (lower, upper) = PlainT(counts);
            }
            if (truth >= lower - 1e-9 && truth <= upper + 1e-9)
            {
                covered++;
            }
        }
        return (double)covered / reps;
    }

    /// <summary>The textbook interval with no prior, kept only to show why the prior exists.</summary>
    private static (double, double) PlainT(int[] counts)
    {
        var n = counts.Sum();
        var mean = Enumerable.Range(0, 5).Sum(i => (i + 1.0) * counts[i]) / n;
        var variance = Enumerable.Range(0, 5).Sum(i => counts[i] * Math.Pow(i + 1 - mean, 2)) / (n - 1);
        var half = StudentT.Quantile975(n - 1) * Math.Sqrt(variance / n);
        return (Math.Max(1, mean - half), Math.Min(5, mean + half));
    }

    [Fact]
    public void Coverage_stays_above_ninety_percent_on_every_distribution_of_the_battery()
    {
        foreach (var n in new[] { 5, 8, 12, 20, 40 })
        {
            var row = new List<string>();
            foreach (var (name, p) in Battery)
            {
                var coverage = Coverage(p, n, reps: 4000, seed: 12345 + n);
                row.Add($"{name}={coverage:P1}");
                Assert.True(coverage >= 0.90, $"n={n} {name}: measured coverage {coverage:P1} is below the documented 90% floor");
            }
            output.WriteLine($"n={n}: {string.Join("  ", row)}");
        }
    }

    [Fact]
    public void Without_the_prior_a_near_unanimous_sample_is_badly_overconfident()
    {
        var plain = Coverage(Battery.Single(b => b.Name == "near-degenerate").P, 5, reps: 4000, seed: 7, regularised: false);
        var ours = Coverage(Battery.Single(b => b.Name == "near-degenerate").P, 5, reps: 4000, seed: 7);

        output.WriteLine($"near-degenerate, n=5: plain t {plain:P1}, regularised {ours:P1}");
        Assert.True(plain < 0.5);
        Assert.True(ours >= 0.9);
    }
}
