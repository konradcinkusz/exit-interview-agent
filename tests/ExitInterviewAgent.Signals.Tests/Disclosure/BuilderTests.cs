using System.Reflection;
using ExitInterviewAgent.Signals.Tests.Support;

namespace ExitInterviewAgent.Signals.Tests.Disclosure;

public sealed class BuilderTests
{
    private const string Acme = "acme";

    private static IEnumerable<Observation> Population(string employer, int count, int rating = 4) =>
        Data.Many(count, i => Data.Rated(employer, rating, Data.Tenures[i % 3], Data.Seniorities[i % 2], Data.Functions[i % 3]));

    private static TopicView Topic(EmployerView view, string topic = "culture") => view.Topics.Single(t => t.Topic == topic);

    // ---- the threshold: exactly k and k - 1 --------------------------------------------------------------------------------

    [Fact]
    public async Task An_employer_with_exactly_k_rated_records_is_displayed()
    {
        var view = Assert.Single(await Data.BuildAsync(Population(Acme, 5)));

        Assert.Equal(5, Topic(view).Overall!.N);
    }

    [Fact]
    public async Task An_employer_with_k_minus_one_rated_records_shows_nothing_at_all()
        => Assert.Empty(await Data.BuildAsync(Population(Acme, 4)));

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public async Task K_is_configurable_and_applies_to_the_topic_cell(int k)
    {
        Assert.Empty(await Data.BuildAsync(Population(Acme, k - 1), k));
        Assert.Single(await Data.BuildAsync(Population(Acme, k), k));
    }

    [Fact]
    public void A_k_below_the_floor_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotBuilder(new DisclosureRules(2)));

    [Fact]
    public async Task K_applies_per_topic_not_per_employer()
    {
        // Ten respondents; only four rated "growth", ten rated "culture".
        var records = Data.Many(10, i => new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked,
            [new TopicRating("culture", 4), .. (i < 4 ? [new TopicRating("growth", 2)] : Array.Empty<TopicRating>())]));

        var view = Assert.Single(await Data.BuildAsync(records));

        Assert.NotNull(Topic(view, "culture").Overall);
        Assert.Null(Topic(view, "growth").Overall); // insufficient data: 4 < k, and not one number of it is returned
        Assert.Empty(Topic(view, "growth").Cuts);
    }

    [Fact]
    public async Task A_covered_topic_without_a_number_counts_as_a_respondent_and_never_as_a_rating()
    {
        var records = Data.Many(9, i => new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked,
            [new TopicRating("culture", i < 4 ? 4 : null)]));

        Assert.Empty(await Data.BuildAsync(records)); // four ratings, five without a number: nothing to show
    }

    [Fact]
    public async Task A_topic_listed_twice_in_one_record_counts_once()
    {
        var records = Data.Many(5, _ => new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked,
            [new TopicRating("culture", 4), new TopicRating("culture", 4)]));

        Assert.Equal(5, Topic(Assert.Single(await Data.BuildAsync(records))).Overall!.N);
    }

    // ---- ties, unanimity, coverage ------------------------------------------------------------------------------------------

    [Fact]
    public async Task All_the_same_rating_is_shown_with_a_wide_interval_not_a_point()
    {
        var overall = Topic(Assert.Single(await Data.BuildAsync(Population(Acme, 6, rating: 5)))).Overall!;

        Assert.Equal(5.0, overall.Mean);
        Assert.True(overall.Upper - overall.Lower > 1.0, $"interval {overall.Lower}-{overall.Upper} is too precise for six identical ratings");
    }

    [Fact]
    public async Task A_perfect_tie_between_two_ratings_gives_the_mean_in_the_middle()
    {
        var records = Data.Many(10, i => Data.Rated(Acme, i % 2 == 0 ? 1 : 5, topics: "culture"));

        var overall = Topic(Assert.Single(await Data.BuildAsync(records))).Overall!;

        Assert.Equal(3.0, overall.Mean);
        Assert.True(overall.Upper - overall.Lower > 1.5);
    }

    [Fact]
    public async Task Reliability_moves_with_n_and_coverage_is_shown_next_to_every_rating()
    {
        var small = Topic(Assert.Single(await Data.BuildAsync(Population("a-small", 5)))).Overall!;
        var middle = Topic(Assert.Single(await Data.BuildAsync(Population("b-middle", 12)))).Overall!;
        var large = Topic(Assert.Single(await Data.BuildAsync(Population("c-large", 40)))).Overall!;

        Assert.Equal([Reliabilities.Low, Reliabilities.Moderate, Reliabilities.High], [small.Reliability, middle.Reliability, large.Reliability]);
        Assert.All(new[] { small, middle, large }, s => Assert.Equal(Coverages.High, s.Coverage));
    }

    [Fact]
    public async Task Coverage_reports_non_response_as_a_band_next_to_the_rating()
    {
        // Twenty respondents, six of whom rated the topic: coverage is "low", and no count of non-respondents is returned.
        var records = Data.Many(20, i => new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked, i < 6 ? [new TopicRating("culture", 3)] : []));

        Assert.Equal(Coverages.Low, Topic(Assert.Single(await Data.BuildAsync(records))).Overall!.Coverage);
    }

    [Fact]
    public async Task The_respondent_count_is_a_band_never_an_exact_number()
    {
        var view = Assert.Single(await Data.BuildAsync(Population(Acme, 13)));

        Assert.Equal("10-24", view.RespondentsBand);
    }

    // ---- cuts: one band at a time, clean partitions only --------------------------------------------------------------------

    [Fact]
    public async Task A_cut_with_a_small_band_is_withheld_whole_not_just_in_that_band()
    {
        // Tenure: 12 + 11 + 3. The three-person band must be hidden, and so must the others, or subtraction would recover it.
        var records = Data.Many(26, i => Data.Rated(Acme, 4, i < 12 ? "1y_3y" : i < 23 ? "3y_5y" : "gt_10y", null, null));

        var tenure = Topic(Assert.Single(await Data.BuildAsync(records))).Cuts.Single(c => c.Dimension == "tenure");

        Assert.False(tenure.Published);
        Assert.Empty(tenure.Cells);
    }

    [Fact]
    public async Task A_clean_cut_publishes_its_bands_and_marks_empty_ones_as_none()
    {
        var records = Data.Many(20, i => Data.Rated(Acme, 4, i < 10 ? "1y_3y" : "3y_5y", null, null));

        var tenure = Topic(Assert.Single(await Data.BuildAsync(records))).Cuts.Single(c => c.Dimension == "tenure");

        Assert.True(tenure.Published);
        Assert.Equal(6, tenure.Cells.Count);
        Assert.Equal([10, 10], tenure.Cells.Where(c => c.Status == BandStatuses.Ok).Select(c => c.Stats!.N));
        Assert.Equal(4, tenure.Cells.Count(c => c.Status == BandStatuses.None));
        Assert.All(tenure.Cells.Where(c => c.Status == BandStatuses.None), c => Assert.Null(c.Stats));
    }

    [Fact]
    public async Task Records_that_left_an_optional_band_out_are_a_group_of_their_own_and_obey_k_too()
    {
        // 18 say "mid", 2 say nothing: the two are recoverable by subtraction unless the whole seniority cut is withheld.
        var records = Data.Many(20, i => Data.Rated(Acme, 4, "1y_3y", i < 18 ? "mid" : null, null));

        var topic = Topic(Assert.Single(await Data.BuildAsync(records)));

        Assert.False(topic.Cuts.Single(c => c.Dimension == "seniority").Published);
        Assert.True(topic.Cuts.Single(c => c.Dimension == "tenure").Published);
    }

    [Fact]
    public async Task There_is_no_cross_product_of_bands()
    {
        var topic = Topic(Assert.Single(await Data.BuildAsync(Population(Acme, 30))));

        Assert.Equal(["tenure", "seniority", "function"], topic.Cuts.Select(c => c.Dimension));
        Assert.All(topic.Cuts.SelectMany(c => c.Cells), c => Assert.DoesNotContain('x', c.Band.Replace("product_design", "").Replace("sales_marketing", "")));
    }

    [Fact]
    public async Task Distribution_and_verification_are_published_only_as_clean_partitions()
    {
        // 15 ratings split 5 / 5 / 5 into the three bins: clean. Verification 5 / 5 / 5: clean.
        var clean = Data.Many(15, i => Data.Rated(Acme, new[] { 1, 3, 5 }[i % 3], verification: (VerificationState)(i % 3)));
        var overall = Topic(Assert.Single(await Data.BuildAsync(clean))).Overall!;
        Assert.Equal([5, 5, 5], overall.Distribution!.Select(g => g.Count));
        Assert.Equal([5, 5, 5], overall.Verification!.Select(g => g.Count));

        // 11 ratings split 6 / 3 / 2: the small bins would be exposed, so no distribution is shown at all, and no verification either.
        var skewed = Data.Many(11, i => Data.Rated("other", i < 6 ? 1 : i < 9 ? 3 : 5, verification: i < 6 ? VerificationState.Verified : i < 9 ? VerificationState.Unverified : VerificationState.Unchecked));
        var withheld = Topic(Assert.Single(await Data.BuildAsync(skewed))).Overall!;
        Assert.Null(withheld.Distribution);
        Assert.Null(withheld.Verification);
        Assert.Equal(11, withheld.N); // the cell itself is still displayed
    }

    [Fact]
    public async Task Band_cells_never_carry_a_distribution_or_a_verification_breakdown()
    {
        var topic = Topic(Assert.Single(await Data.BuildAsync(Population(Acme, 30))));

        Assert.All(topic.Cuts.SelectMany(c => c.Cells).Where(c => c.Stats is not null), c =>
        {
            Assert.Null(c.Stats!.Distribution);
            Assert.Null(c.Stats.Verification);
        });
    }

    // ---- the feed contract -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_feed_that_is_not_grouped_by_employer_fails_the_build_instead_of_publishing_wrong_numbers()
    {
        var interleaved = Population("one", 3).Concat(Population("two", 3)).Concat(Population("one", 3));
        var builder = new SnapshotBuilder(new DisclosureRules(5));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in builder.BuildAsync(Data.Feed(interleaved), new BuildReport()))
            {
            }
        });
    }

    [Fact]
    public async Task Observations_outside_the_vocabulary_are_rejected_and_counted_never_described()
    {
        var good = Population(Acme, 5).ToList();
        var bad = new[]
        {
            Data.Rated(Acme, 4, tenure: "ancient"),
            Data.Rated(Acme, 4, seniority: "wizard"),
            Data.Rated(Acme, 4, function: "x"),
            new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked, [new TopicRating("weather", 3)]),
            new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked, [new TopicRating("culture", 9)]),
        };
        var report = new BuildReport();
        var builder = new SnapshotBuilder(new DisclosureRules(5));
        var views = new List<EmployerView>();
        await foreach (var v in builder.BuildAsync(Data.Feed([.. good, .. bad]), report))
        {
            views.Add(v);
        }

        Assert.Equal(5, report.RejectedObservations);
        Assert.Equal(5, Topic(Assert.Single(views)).Overall!.N);
    }

    [Fact]
    public async Task The_build_is_deterministic_and_does_not_depend_on_record_order()
    {
        var records = Population(Acme, 25).ToList();
        var shuffled = records.OrderBy(r => r.GetHashCode()).Reverse().ToList();

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(await Data.BuildAsync(records)),
            System.Text.Json.JsonSerializer.Serialize(await Data.BuildAsync(shuffled)));
    }

    // ---- anti-goals enforced by the shape of the data ----------------------------------------------------------------------

    [Fact]
    public void There_is_no_score_no_composite_no_rank_and_no_percentile_anywhere_in_the_published_shape()
    {
        var allowed = new HashSet<string>
        {
            "EmployerRef", "RespondentsBand", "Topics", "Topic", "Overall", "Cuts", "Dimension", "Published", "Cells", "Band", "Status", "Stats",
            "N", "Mean", "Lower", "Upper", "Reliability", "Coverage", "Distribution", "Verification", "Key", "Count",
        };
        var types = new[] { typeof(EmployerView), typeof(TopicView), typeof(CutView), typeof(BandView), typeof(StatsView), typeof(GroupView) };

        var properties = types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name).Distinct().ToArray();

        Assert.Empty(properties.Except(allowed));
        Assert.DoesNotContain(properties, p => p.Contains("Score", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Rank", StringComparison.OrdinalIgnoreCase) || p.Contains("Percentile", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Best", StringComparison.OrdinalIgnoreCase) || p.Contains("Overall", StringComparison.OrdinalIgnoreCase) && p != "Overall");
    }

    [Fact]
    public async Task Each_topic_stands_alone_there_is_no_average_across_topics()
    {
        var view = Assert.Single(await Data.BuildAsync(Population(Acme, 10)));

        Assert.Equal(Vocabulary.Topics, view.Topics.Select(t => t.Topic));
        Assert.All(view.Topics, t => Assert.NotNull(t.Overall));
    }

    // ---- mutants at the pipeline level --------------------------------------------------------------------------------------

    [Fact]
    public async Task Mutant_that_displays_k_minus_one_records_is_caught_by_the_threshold_test()
    {
        // Five respondents, four of whom rated the topic.
        var records = Data.Many(5, i => new Observation(Acme, "1y_3y", null, null, VerificationState.Unchecked, i < 4 ? [new TopicRating("culture", 4)] : []));

        var leaked = Assert.Single(await Data.BuildAsync(records, policy: new DisplaysBelowK()));

        Assert.Equal(4, Topic(leaked).Overall!.N);               // the mutant publishes a group of four ...
        Assert.Empty(await Data.BuildAsync(records));            // ... which the real policy never does
    }

    [Fact]
    public async Task Mutant_without_complementary_suppression_exposes_a_small_band_through_subtraction()
    {
        var records = Data.Many(26, i => Data.Rated(Acme, 4, i < 12 ? "1y_3y" : i < 23 ? "3y_5y" : "gt_10y", null, null)).ToList();

        var mutantTenure = Topic(Assert.Single(await Data.BuildAsync(records, policy: new PerCellOnly()))).Cuts.Single(c => c.Dimension == "tenure");
        var realTenure = Topic(Assert.Single(await Data.BuildAsync(records))).Cuts.Single(c => c.Dimension == "tenure");

        // The mutant publishes 12 + 11 of 26 and so hands out "3 more": exactly the leak the real policy closes.
        var shown = mutantTenure.Cells.Where(c => c.Status == BandStatuses.Ok).Sum(c => c.Stats!.N);
        Assert.Equal(26 - 3, shown);
        Assert.False(realTenure.Published);
    }
}
