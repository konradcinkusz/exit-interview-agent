namespace ExitInterviewAgent.Records.Tests;

public class ModelTests
{
    [Fact]
    public void No_data_is_distinct_from_a_low_rating()
    {
        var low = TopicEntry.Covered(1, Confidence.High, ["It was awful."]);

        Assert.NotEqual(TopicEntry.NoData, low);
        Assert.Equal(TopicStatus.NoData, TopicEntry.NoData.Status);
        Assert.Null(TopicEntry.NoData.Rating);
        Assert.Empty(TopicEntry.NoData.Quotes);
        Assert.Equal(1, low.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_outside_one_to_five_cannot_be_constructed(int rating) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TopicEntry.Covered(rating, Confidence.Low, ["q"]));

    [Fact]
    public void Covered_needs_between_one_and_five_non_blank_bounded_quotes()
    {
        Assert.Throws<ArgumentException>(() => TopicEntry.Covered(3, Confidence.Low, []));
        Assert.Throws<ArgumentException>(() => TopicEntry.Covered(3, Confidence.Low, Enumerable.Repeat("q", 6)));
        Assert.Throws<ArgumentException>(() => TopicEntry.Covered(3, Confidence.Low, [" "]));
        Assert.Throws<ArgumentException>(() => TopicEntry.Covered(3, Confidence.Low, [new string('a', 401)]));
        Assert.NotNull(TopicEntry.Covered(null, Confidence.Low, ["fine"]));
    }

    [Fact]
    public void Quotes_cannot_be_changed_through_the_source_collection()
    {
        var source = new List<string> { "a" };
        var entry = TopicEntry.Covered(3, Confidence.Low, source);

        source.Add("b");

        Assert.Single(entry.Quotes);
    }

    [Fact]
    public void Interview_ids_are_random_well_formed_and_unique()
    {
        var ids = Enumerable.Range(0, 2000).Select(_ => InterviewId.NewRandom().Value).ToArray();

        Assert.Equal(2000, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{32}$", id));
        // 128 random bits: every hex position is exercised across the sample.
        Assert.True(ids.SelectMany(i => i).Distinct().Count() == 16);
    }

    [Fact]
    public void Interview_id_parsing_is_strict()
    {
        Assert.False(InterviewId.TryParse("ABCDEF0123456789ABCDEF0123456789", out _));
        Assert.False(InterviewId.TryParse("abc", out _));
        Assert.False(InterviewId.TryParse(null, out _));
        Assert.False(InterviewId.TryParse("0f3c9a1e-7b2d-4c58-a6e1-903fd2b4", out _));
        Assert.True(InterviewId.TryParse("0f3c9a1e7b2d4c58a6e1903fd2b47c11", out var id));
        Assert.Equal("0f3c9a1e7b2d4c58a6e1903fd2b47c11", id.ToString());
        Assert.Throws<InvalidOperationException>(() => default(InterviewId).Value);
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("acme-sp-zoo", true)]
    [InlineData("a1b", true)]
    [InlineData("ab", false)]
    [InlineData("Acme", false)]
    [InlineData("acme--x", false)]
    [InlineData("-acme", false)]
    [InlineData("acme corp", false)]
    [InlineData("acme@x.pl", false)]
    public void Employer_ref_pattern_is_strict(string value, bool valid) => Assert.Equal(valid, EmployerRef.IsValid(value));

    [Fact]
    public void Topic_set_indexer_covers_every_topic()
    {
        var record = Fixtures.Sample();

        Assert.Equal(6, record.Topics.Enumerate().Count());
        Assert.Same(record.Topics.Culture, record.Topics[Topic.Culture]);
        Assert.Same(record.Topics.PayVsPromises, record.Topics[Topic.PayVsPromises]);
    }
}
