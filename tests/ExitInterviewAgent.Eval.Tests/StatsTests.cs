using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Tests;

public class StatsTests
{
    [Theory]
    [InlineData(0, 10, 0.0, 0.2775)]
    [InlineData(10, 10, 0.7225, 1.0)]
    [InlineData(5, 10, 0.2366, 0.7634)]
    [InlineData(1, 1, 0.2065, 1.0)]
    public void The_wilson_interval_matches_published_values(int k, int n, double low, double high)
    {
        var (l, h) = Count.Wilson(k, n)!.Value;

        Assert.Equal(low, l, 4);
        Assert.Equal(high, h, 4);
    }

    [Fact]
    public void A_rate_over_no_observations_is_undefined_not_one_and_not_zero()
    {
        var c = new Count(0, 0);

        Assert.Null(c.Rate);
        Assert.Null(c.Interval);
        Assert.Contains("n/a", c.ToString());
    }

    [Fact]
    public void Counts_add_and_overlapping_intervals_are_indistinguishable()
    {
        Assert.Equal(new Count(3, 7), new Count(1, 3) + new Count(2, 4));
        Assert.True(Count.Indistinguishable(new Count(9, 10), new Count(8, 10)));
        Assert.False(Count.Indistinguishable(new Count(100, 100), new Count(0, 100)));
    }

    [Fact]
    public void Cohens_kappa_matches_a_hand_computed_two_by_two_table()
    {
        // 20 both-yes, 15 both-no, 5 (yes,no), 10 (no,yes): observed 0.7, expected 0.5, kappa 0.4.
        var pairs = Enumerable.Repeat((1, 1), 20).Concat(Enumerable.Repeat((0, 0), 15)).Concat(Enumerable.Repeat((1, 0), 5)).Concat(Enumerable.Repeat((0, 1), 10)).ToList();

        Assert.Equal(0.4, Agreement.Kappa(pairs)!.Value, 9);
    }

    [Fact]
    public void Kappa_is_undefined_never_one_when_everything_falls_in_one_category_or_there_are_too_few_pairs()
    {
        Assert.Null(Agreement.Kappa(Enumerable.Repeat((2, 2), 10).ToList()));
        Assert.Null(Agreement.Kappa([(1, 1)]));
        Assert.Null(Agreement.Kappa([]));
    }

    [Fact]
    public void The_bootstrap_interval_is_reproducible_and_brackets_the_estimate()
    {
        var pairs = Enumerable.Repeat((1, 1), 20).Concat(Enumerable.Repeat((0, 0), 15)).Concat(Enumerable.Repeat((1, 0), 5)).Concat(Enumerable.Repeat((0, 1), 10)).ToList();

        var a = Agreement.KappaInterval(pairs)!.Value;
        var b = Agreement.KappaInterval(pairs)!.Value;

        Assert.Equal(a, b);
        Assert.True(a.Low < 0.4 && 0.4 < a.High);
    }

    [Fact]
    public void The_mean_interval_brackets_the_mean_and_is_reproducible()
    {
        var m = Agreement.MeanInterval([0, 1, 2, 2, 2, 1, 2, 0, 1, 2])!.Value;

        Assert.Equal(1.3, m.Mean, 9);
        Assert.True(m.Low <= m.Mean && m.Mean <= m.High);
        Assert.Equal(m, Agreement.MeanInterval([0, 1, 2, 2, 2, 1, 2, 0, 1, 2])!.Value);
        Assert.Null(Agreement.MeanInterval([]));
    }
}
