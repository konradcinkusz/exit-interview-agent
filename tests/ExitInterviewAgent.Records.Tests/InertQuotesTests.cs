namespace ExitInterviewAgent.Records.Tests;

/// <summary>Quotes are inert data: hostile-looking content changes nothing about structure or any other field.</summary>
public class InertQuotesTests
{
    private static readonly RecordValidator Validator = new();

    [Fact]
    public void Injection_style_quotes_are_accepted_as_plain_text_and_change_nothing_else()
    {
        var plain = Validator.Validate(Fixtures.Read("valid/full.json")).Record!;
        var hostile = Validator.Validate(Fixtures.Read("valid/inert-quotes.json")).Record!;

        Assert.Equal(5, hostile.Topics.Culture.Quotes.Count);
        Assert.Equal(3, hostile.Topics.Culture.Rating);
        Assert.Equal(plain.Topics.Management, hostile.Topics.Management);
        Assert.Equal(plain.Context, hostile.Context);
        Assert.Equal(plain.EmployerRef, hostile.EmployerRef);
        Assert.True(hostile.PiiMasked);
    }

    [Fact]
    public void A_quote_that_looks_like_json_does_not_add_fields_after_a_round_trip()
    {
        var hostile = Validator.Validate(Fixtures.Read("valid/inert-quotes.json")).Record!;

        var again = Validator.Validate(RecordSerializer.SerializeCanonical(hostile));

        Assert.True(again.IsValid);
        Assert.Equal(hostile, again.Record);
        Assert.Contains("\"}, \"userId\": \"1\"", again.Record!.Topics.Culture.Quotes[1]);
    }

    [Fact]
    public void A_ref_in_a_quote_is_never_dereferenced()
    {
        // No network, no schema registry lookup: validating a quote that holds a $ref/URL completes offline and passes.
        var outcome = Validator.Validate(Fixtures.Read("valid/inert-quotes.json"));

        Assert.True(outcome.IsValid);
        Assert.Contains("$ref", outcome.Record!.Topics.Culture.Quotes[0]);
    }
}
