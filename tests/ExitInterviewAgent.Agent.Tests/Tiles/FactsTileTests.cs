using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Tiles.TileTestSupport;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

public class FactsTileTests
{
    [Fact]
    public void The_facts_tile_gives_rating_and_confidence_per_covered_topic_in_english()
    {
        var facts = FactsTile.Build(Full());

        Assert.Equal(TileKind.Facts, facts.Kind);
        Assert.Equal("Facts from your ratings", facts.Title);
        Assert.Contains("Onboarding: rating 2 of 5, confidence medium.", facts.Text);
        Assert.Contains("Management: rating 1 of 5, confidence high.", facts.Text);
        Assert.Contains("Culture: rating 4 of 5, confidence medium.", facts.Text);
    }

    [Fact]
    public void A_covered_topic_without_a_rating_says_so_and_gives_no_number()
    {
        var line = FactsTile.Build(Full()).Text.Split('\n').Single(l => l.StartsWith("Growth:", StringComparison.Ordinal));

        Assert.Equal("Growth: no rating, confidence low.", line);
    }

    [Fact]
    public void A_no_data_topic_is_listed_as_no_data_with_no_rating_and_is_not_cited()
    {
        var record = WithTopic(Full(), Topic.Growth, TopicEntry.NoData);

        var facts = FactsTile.Build(record);

        var line = facts.Text.Split('\n').Single(l => l.StartsWith("Growth:", StringComparison.Ordinal));
        Assert.Equal("Growth: no data.", line);
        Assert.DoesNotContain("rating", line);
        Assert.DoesNotContain("growth", facts.BasedOn);
    }

    [Fact]
    public void The_facts_tile_cites_every_covered_topic_in_record_order_and_no_other()
    {
        Assert.Equal(Wire.Names<Topic>(), FactsTile.Build(Full()).BasedOn);

        var partial = FactsTile.Build(WithTopic(Full(), Topic.Culture, TopicEntry.NoData));
        Assert.DoesNotContain("culture", partial.BasedOn);
        Assert.Equal(5, partial.BasedOn.Count);
    }

    [Fact]
    public void The_facts_tile_never_contains_a_quote_from_the_record()
    {
        var record = Full();
        var text = FactsTile.Build(record).Text;

        foreach (var (_, entry) in record.Topics.Enumerate())
            foreach (var quote in entry.Quotes)
                Assert.DoesNotContain(quote, text);
    }

    [Fact]
    public void The_facts_tile_is_the_same_on_every_call()
    {
        var record = Full();

        var a = FactsTile.Build(record);
        var b = FactsTile.Build(record);
        Assert.Equal((a.Kind, a.Title, a.Text, string.Join(',', a.BasedOn)), (b.Kind, b.Title, b.Text, string.Join(',', b.BasedOn)));
    }

    [Fact]
    public void A_polish_record_gets_a_polish_facts_tile()
    {
        var facts = FactsTile.Build(WithLanguage(Full(), "pl"));

        Assert.Equal("Fakty z twoich ocen", facts.Title);
        Assert.Contains("Wdrożenie: ocena 2 z 5, pewność średnia.", facts.Text);
        Assert.Contains("Rozwój: bez oceny, pewność niska.", facts.Text);
    }

    [Fact]
    public void A_polish_no_data_topic_says_brak_danych()
    {
        var facts = FactsTile.Build(WithLanguage(WithTopic(Full(), Topic.Growth, TopicEntry.NoData), "pl"));

        Assert.Contains("Rozwój: brak danych.", facts.Text);
    }

    [Fact]
    public void Any_language_other_than_pl_gets_english()
    {
        var facts = FactsTile.Build(WithLanguage(Full(), "de"));

        Assert.Equal("Facts from your ratings", facts.Title);
        Assert.Contains("Onboarding: rating 2 of 5, confidence medium.", facts.Text);
    }
}
