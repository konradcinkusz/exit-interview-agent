using ExitInterviewAgent.Signals.Tests.Support;

namespace ExitInterviewAgent.Signals.Tests.Disclosure;

/// <summary>
/// Property tests over random populations with a FIXED seed (a failure reproduces). They run the real pipeline (accumulator, policy,
/// statistics) and check, on what a reader of the published view can see, the same claims the exhaustive partition tests check on
/// counts: no group below k in one snapshot, none below k - 1 across two snapshots that differ by one record.
/// </summary>
public sealed class PropertyTests
{
    private const int Seed = 20261005;
    private const string Employer = "acme";

    private static Observation RandomRecord(Random rng, double[] ratingWeights, int heavy)
    {
        // Skewed bands: one band takes most records, the others share the rest (small cells are the interesting case).
        string Pick(string[] bands) => rng.NextDouble() < 0.6 ? bands[heavy % bands.Length] : bands[rng.Next(bands.Length)];
        var ratings = new List<TopicRating>();
        foreach (var topic in Vocabulary.Topics)
        {
            if (rng.NextDouble() < 0.15)
            {
                continue; // topic not covered
            }
            int? rating = rng.NextDouble() < 0.1 ? null : Draw(rng, ratingWeights);
            ratings.Add(new TopicRating(topic, rating));
        }
        return new Observation(
            Employer, Pick(Data.Tenures),
            rng.NextDouble() < 0.25 ? null : Pick(Data.Seniorities),
            rng.NextDouble() < 0.25 ? null : Pick(Data.Functions),
            (VerificationState)rng.Next(3), ratings);
    }

    private static int Draw(Random rng, double[] weights)
    {
        var u = rng.NextDouble() * weights.Sum();
        var acc = 0.0;
        for (var i = 0; i < 5; i++)
        {
            acc += weights[i];
            if (u < acc)
            {
                return i + 1;
            }
        }
        return 5;
    }

    private static List<Observation> RandomPopulation(Random rng)
    {
        var size = rng.Next(4) switch { 0 => rng.Next(0, 8), 1 => rng.Next(8, 20), 2 => rng.Next(20, 45), _ => rng.Next(1, 90) };
        var weights = rng.Next(4) switch
        {
            0 => new[] { 0.0, 0, 0, 0, 1 },          // unanimous
            1 => new[] { .4, .05, .1, .05, .4 },     // polarised
            2 => [.05, .1, .2, .35, .3],
            _ => [1, 1, 1, 1, 1],
        };
        var heavy = rng.Next(6);
        return [.. Enumerable.Range(0, size).Select(_ => RandomRecord(rng, weights, heavy))];
    }

    // ---- what the reader of one snapshot can compute ----------------------------------------------------------------------

    private static void AssertSnapshotInvariants(List<EmployerView> views, int k)
    {
        foreach (var view in views)
        {
            Assert.True(int.Parse(view.RespondentsBand.Split('-', '+')[0]) >= k, "respondent band edge below k");
            Assert.Equal(Vocabulary.Topics, view.Topics.Select(t => t.Topic));
            Assert.Contains(view.Topics, t => t.Overall is not null);
            foreach (var topic in view.Topics)
            {
                if (topic.Overall is null)
                {
                    Assert.Empty(topic.Cuts);
                    continue;
                }
                var overall = topic.Overall;
                AssertStats(overall, k);
                foreach (var groups in new[] { overall.Distribution, overall.Verification })
                {
                    if (groups is null)
                    {
                        continue;
                    }
                    Assert.Equal(overall.N, groups.Sum(x => x.Count));
                    Assert.All(groups, x => Assert.True(x.Count == 0 || x.Count >= k, $"group {x.Key} of {x.Count}"));
                }
                Assert.Equal(["tenure", "seniority", "function"], topic.Cuts.Select(c => c.Dimension));
                foreach (var cut in topic.Cuts)
                {
                    if (!cut.Published)
                    {
                        Assert.Empty(cut.Cells);
                        continue;
                    }
                    Assert.All(cut.Cells, c => Assert.NotEqual(BandStatuses.Suppressed, c.Status));
                    foreach (var cell in cut.Cells.Where(c => c.Status == BandStatuses.Ok))
                    {
                        AssertStats(cell.Stats!, k);
                    }
                    var remainder = overall.N - cut.Cells.Where(c => c.Status == BandStatuses.Ok).Sum(c => c.Stats!.N);
                    Assert.True(remainder == 0 || remainder >= k, $"{topic.Topic}/{cut.Dimension}: remainder {remainder} recoverable by subtraction");
                }
            }
        }
    }

    private static void AssertStats(StatsView s, int k)
    {
        Assert.True(s.N >= k, $"cell of {s.N} displayed");
        Assert.InRange(s.Mean, 1.0, 5.0);
        Assert.InRange(s.Mean, s.Lower, s.Upper);
        Assert.InRange(s.Lower, 1.0, 5.0);
        Assert.InRange(s.Upper, 1.0, 5.0);
        Assert.True(s.Upper > s.Lower, "an interval collapsed to a point");
        Assert.True(s.N >= 12 || s.Upper - s.Lower >= 0.5, $"n={s.N}: an interval of {s.Upper - s.Lower:0.00} is falsely precise");
        Assert.Contains(s.Reliability, new[] { Reliabilities.Low, Reliabilities.Moderate, Reliabilities.High });
        Assert.Contains(s.Coverage, new[] { Coverages.High, Coverages.Medium, Coverages.Low });
    }

    /// <summary>
    /// The adversary of two snapshots. Returns, for a topic and a dimension, the number of OTHER people (everyone but the adversary's
    /// own record) in the group the adversary can isolate by subtraction, or null when nothing can be isolated.
    /// </summary>
    private static int? IsolatedRemainder(int k, string topic, Dimension dimension, params (EmployerView? View, Observation? Own)[] snapshots)
    {
        var known = new Dictionary<string, int>();
        int? total = null;
        foreach (var (view, own) in snapshots)
        {
            if (view?.Topics.Single(t => t.Topic == topic).Overall is not { } overall)
            {
                continue;
            }
            var rated = own?.Ratings.FirstOrDefault(r => r.Topic == topic)?.Rating is not null;
            var ownBand = own is null || !rated ? null : BandOf(own, dimension);
            total = overall.N - (rated ? 1 : 0);
            var cut = view.Topics.Single(t => t.Topic == topic).Cuts.Single(c => c.Dimension == Vocabulary.Name(dimension));
            if (!cut.Published)
            {
                continue;
            }
            foreach (var cell in cut.Cells.Where(c => c.Status != BandStatuses.Suppressed))
            {
                known[cell.Band] = (cell.Stats?.N ?? 0) - (cell.Band == ownBand ? 1 : 0);
            }
        }
        return total is { } t ? t - known.Values.Sum() : null;
    }

    private static string? BandOf(Observation o, Dimension d) => d switch
    {
        Dimension.Tenure => o.TenureBand,
        Dimension.Seniority => o.SeniorityBand,
        _ => o.FunctionBand,
    };

    // ---- the properties ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task Every_published_number_of_a_random_population_respects_k(int k)
    {
        var rng = new Random(Seed + k);
        for (var trial = 0; trial < 300; trial++)
        {
            AssertSnapshotInvariants(await Data.BuildAsync(RandomPopulation(rng), k), k);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task One_record_added_or_removed_between_snapshots_never_isolates_fewer_than_k_minus_one_other_people(int k)
    {
        var rng = new Random(Seed * 3 + k);
        for (var trial = 0; trial < 250; trial++)
        {
            var before = RandomPopulation(rng);
            var own = RandomRecord(rng, [1, 1, 1, 1, 1], rng.Next(6));
            var withOwn = before.Append(own).ToList();

            var viewBefore = (await Data.BuildAsync(before, k)).SingleOrDefault();
            var viewWith = (await Data.BuildAsync(withOwn, k)).SingleOrDefault();

            // Added: the second snapshot holds the adversary's record. Removed: the first does. Both directions are checked.
            foreach (var pair in new[] { new[] { (viewBefore, (Observation?)null), (viewWith, own) }, [(viewWith, own), (viewBefore, null)] })
            {
                foreach (var topic in Vocabulary.Topics)
                {
                    foreach (var dimension in Vocabulary.Dimensions)
                    {
                        if (IsolatedRemainder(k, topic, dimension, pair) is { } remainder)
                        {
                            Assert.True(remainder == 0 || remainder >= k - 1,
                                $"trial {trial} k={k} {topic}/{dimension}: {remainder} other people isolated");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public async Task The_textbook_rule_is_flagged_by_the_same_two_snapshot_check_and_the_standard_rule_is_not()
    {
        // The scenario the exhaustive partition search finds for the textbook complementary suppression: seniority "mid" 5, "senior" 4,
        // and one record that left seniority out. The adversary adds a "senior" record of their own. Under the textbook rule the old
        // snapshot shows "mid" (5 of 10, remainder 5), the new one shows "senior" (5 of 11); subtracting isolates the one person who
        // left the band out. Without this test the property above could be passing for the wrong reason.
        const int k = 5;
        List<Observation> population =
        [
            .. Data.Many(5, _ => Data.Rated(Employer, 4, "1y_3y", "mid", "engineering", topics: "culture")),
            .. Data.Many(4, _ => Data.Rated(Employer, 4, "1y_3y", "senior", "engineering", topics: "culture")),
            Data.Rated(Employer, 2, "1y_3y", null, "engineering", topics: "culture"),
        ];
        var own = Data.Rated(Employer, 4, "1y_3y", "senior", "engineering", topics: "culture");

        async Task<int?> Isolated(IDisclosurePolicy policy)
        {
            var before = (await Data.BuildAsync(population, k, policy)).SingleOrDefault();
            var after = (await Data.BuildAsync(population.Append(own), k, policy)).SingleOrDefault();
            return IsolatedRemainder(k, "culture", Dimension.Seniority, (before, null), (after, own));
        }

        Assert.Equal(1, await Isolated(new ClassicComplementary()));
        var standard = await Isolated(StandardDisclosurePolicy.Instance);
        Assert.True(standard is null or 0 || standard >= k - 1, $"the standard rule isolated {standard} other people");
    }

    [Fact]
    public async Task Deleting_a_record_shrinks_the_next_snapshot_and_hides_what_falls_below_k()
    {
        var records = Data.Many(5, i => Data.Rated(Employer, 4, Data.Tenures[i % 2], null, null)).ToList();
        var published = await Data.BuildAsync(records);
        var afterDeletion = await Data.BuildAsync(records.Skip(1));

        Assert.Single(published);
        Assert.Empty(afterDeletion); // 4 < k: the whole employer disappears at the next batch, not a partial remainder
    }

    [Fact]
    public async Task Small_employers_skewed_bands_and_ties_never_publish_anything_below_k()
    {
        // Hand-made nasty cases, each with a known outcome.
        var cases = new Dictionary<string, List<Observation>>
        {
            ["one respondent"] = [Data.Rated(Employer, 5)],
            ["k - 1"] = [.. Data.Many(4, _ => Data.Rated(Employer, 3))],
            ["everyone in one band"] = [.. Data.Many(30, _ => Data.Rated(Employer, 2, "gt_10y", "management", "other"))],
            ["a lone outlier band"] = [.. Data.Many(29, _ => Data.Rated(Employer, 2, "1y_3y")), Data.Rated(Employer, 5, "gt_10y")],
            ["two bands of k - 1 each"] = [.. Data.Many(4, _ => Data.Rated(Employer, 2, "1y_3y")), .. Data.Many(4, _ => Data.Rated(Employer, 4, "3y_5y"))],
        };
        foreach (var (name, population) in cases)
        {
            var views = await Data.BuildAsync(population);
            AssertSnapshotInvariants(views, 5);
            if (name is "one respondent" or "k - 1")
            {
                Assert.Empty(views);
            }
        }
        // 30 records: 29 in one band, 1 alone in another: the cut must be withheld, not shown as "29 of 30".
        var outlier = Assert.Single(await Data.BuildAsync(cases["a lone outlier band"]));
        Assert.False(outlier.Topics[0].Cuts.Single(c => c.Dimension == "tenure").Published);
    }
}
