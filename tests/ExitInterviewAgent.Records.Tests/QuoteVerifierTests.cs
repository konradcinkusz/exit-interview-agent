using System.Text.Json;

namespace ExitInterviewAgent.Records.Tests;

public class QuoteVerifierTests
{
    private const string Transcript =
        "Interviewer: What about onboarding?\nUser: The first two weeks   nobody told me who to ask about access.\n" +
        "Interviewer: And your manager?\nUser: My manager [PERSON] cancelled every one-to-one.\tFeedback only came at the yearly review.\r\n" +
        "User: Zastanawiałem się nad rozwojem, ale nie wiem, czy to było możliwe.";

    private static InterviewRecord WithQuote(string quote) =>
        Fixtures.Sample(r => r with { Topics = r.Topics with { Culture = TopicEntry.Covered(3, Confidence.Low, [quote]) } });

    [Fact]
    public void Verbatim_quotes_verify()
    {
        var result = QuoteVerifier.VerifyQuotes(Transcript, WithQuote("Feedback only came at the yearly review."));

        Assert.Contains(result.Mismatches, m => m.Topic != Topic.Culture); // other sample quotes are not in this transcript
        Assert.DoesNotContain(result.Mismatches, m => m.Topic == Topic.Culture);
    }

    [Fact]
    public void The_golden_record_verifies_against_a_transcript_that_contains_its_quotes()
    {
        var record = Fixtures.Sample();
        var transcript = string.Join("\n", record.Topics.Enumerate().SelectMany(t => t.Entry.Quotes).Select(q => "User: " + q));

        var result = QuoteVerifier.VerifyQuotes(transcript, record);

        Assert.True(result.AllVerbatim);
        Assert.Equal(7, result.QuotesChecked);
    }

    [Theory]
    [InlineData("The   first two\nweeks nobody told me who to ask about access.")]
    [InlineData("  The first two weeks nobody told me who to ask about access.  ")]
    [InlineData("Feedback only came\tat the yearly review.")]
    [InlineData("every one-to-one. Feedback only")]
    [InlineData("Zastanawiałem się nad rozwojem")]
    public void Whitespace_differences_are_normalized_away(string quote) =>
        Assert.DoesNotContain(QuoteVerifier.VerifyQuotes(Transcript, WithQuote(quote)).Mismatches, m => m.Topic == Topic.Culture);

    [Theory]
    [InlineData("the first two weeks nobody told me")]                 // case
    [InlineData("The first two weeks nobody told me who to ask for access.")] // one word changed
    [InlineData("The first two weeks no one told me who to ask about access.")] // paraphrase
    [InlineData("Zastanawialem sie nad rozwojem")]                      // diacritics stripped
    [InlineData("first two weeks nobody told me, who to ask")]          // punctuation added
    [InlineData("access. The first")]                                   // order swapped
    [InlineData("Interviewer: What about onboarding? And your manager?")] // skips a turn
    public void Anything_beyond_whitespace_is_a_mismatch(string quote) =>
        Assert.Contains(QuoteVerifier.VerifyQuotes(Transcript, WithQuote(quote)).Mismatches, m => m.Topic == Topic.Culture);

    [Fact]
    public void Empty_transcript_verifies_nothing()
    {
        var result = QuoteVerifier.VerifyQuotes("", Fixtures.Sample());

        Assert.Equal(result.QuotesChecked, result.Mismatches.Count);
    }

    [Fact]
    public void A_record_with_no_quotes_trivially_verifies()
    {
        var empty = new RecordValidator().Validate(Fixtures.Read("valid/minimal-all-no-data.json")).Record!;

        var result = QuoteVerifier.VerifyQuotes("anything", empty);

        Assert.True(result.AllVerbatim);
        Assert.Equal(0, result.QuotesChecked);
    }

    [Fact]
    public void Mismatches_report_where_not_what()
    {
        var result = QuoteVerifier.VerifyQuotes("nothing here", WithQuote("a secret quote body"));

        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result));
        Assert.Contains(new QuoteMismatch(Topic.Culture, 0), result.Mismatches);
    }

    [Fact]
    public void Normalize_collapses_and_trims()
    {
        Assert.Equal("a b c", QuoteVerifier.Normalize("  a \t\n b  c \r\n"));
        Assert.Equal("", QuoteVerifier.Normalize(" \n\t "));
    }
}
