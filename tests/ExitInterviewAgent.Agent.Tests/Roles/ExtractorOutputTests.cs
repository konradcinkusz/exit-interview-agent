using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Roles;

public class ExtractorOutputTests
{
    private static string Topics(string management) => """
        {"topics":{"onboarding":{"status":"no_data"},"management":MANAGEMENT,"growth":{"status":"no_data"},"pay_vs_promises":{"status":"no_data"},"culture":{"status":"no_data"},"reason_for_leaving":{"status":"no_data"}}}
        """.Replace("MANAGEMENT", management);

    private const string Covered = """{"status":"covered","rating":4,"confidence":"high","quotes":["My manager was supportive."]}""";

    private static IReadOnlyList<string> Errors(string text)
    {
        ExtractorOutput.TryParse(text, out _, out var codes);
        return codes;
    }

    [Fact]
    public void A_conforming_object_is_mapped_to_typed_values()
    {
        Assert.True(ExtractorOutput.TryParse(Topics(Covered), out var o, out var codes));

        Assert.Empty(codes);
        var m = o!.Topics[Topic.Management];
        Assert.True(m.Covered);
        Assert.Equal(4, m.Rating);
        Assert.Equal(Confidence.High, m.Confidence);
        Assert.Equal(["My manager was supportive."], m.Quotes);
        Assert.False(o.Topics[Topic.Growth].Covered);
    }

    [Fact]
    public void A_null_rating_is_allowed_for_a_covered_topic()
    {
        Assert.True(ExtractorOutput.TryParse(Topics("""{"status":"covered","rating":null,"confidence":"low","quotes":["Some words here."]}"""), out var o, out _));
        Assert.Null(o!.Topics[Topic.Management].Rating);
    }

    [Fact]
    public void Code_fences_and_surrounding_prose_are_tolerated()
    {
        var wrapped = "Here is the result:\n```json\n" + Topics(Covered) + "\n```\nHope that helps.";

        Assert.True(ExtractorOutput.TryParse(wrapped, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I could not do that.")]
    [InlineData("{ not json }")]
    [InlineData("{\"topics\":")]
    public void Not_json_is_reported_with_a_code_only(string? text) => Assert.Equal([ExtractorErrors.NotJson], Errors(text!));

    [Fact]
    public void Duplicate_keys_are_rejected() =>
        Assert.Equal([ExtractorErrors.NotJson], Errors("""{"topics":{},"topics":{}}"""));

    [Theory]
    [InlineData("""{"status":"covered","rating":4,"confidence":"high","quotes":["x"],"sentiment":"angry"}""")]
    [InlineData("""{"status":"covered","rating":4,"confidence":"high","quotes":["x"],"mood":"low"}""")]
    [InlineData("""{"status":"covered","rating":4,"confidence":"high","quotes":["x"],"name":"someone"}""")]
    [InlineData("""{"status":"covered","rating":6,"confidence":"high","quotes":["x"]}""")]
    [InlineData("""{"status":"covered","rating":0,"confidence":"high","quotes":["x"]}""")]
    [InlineData("""{"status":"covered","rating":3.5,"confidence":"high","quotes":["x"]}""")]
    [InlineData("""{"status":"covered","rating":4,"confidence":"certain","quotes":["x"]}""")]
    [InlineData("""{"status":"covered","rating":4,"confidence":"high","quotes":[]}""")]
    [InlineData("""{"status":"covered","rating":4,"confidence":"high"}""")]
    [InlineData("""{"status":"covered","rating":4,"confidence":"high","quotes":["a","b","c","d","e","f"]}""")]
    [InlineData("""{"status":"no_data","rating":4}""")]
    [InlineData("""{"status":"no_data","quotes":["x"]}""")]
    [InlineData("""{"status":"maybe"}""")]
    [InlineData("""{}""")]
    public void Anything_outside_the_extractor_schema_is_rejected(string management) =>
        Assert.Equal([ExtractorErrors.SchemaViolation], Errors(Topics(management)));

    [Fact]
    public void A_quote_over_400_characters_is_rejected() =>
        Assert.Equal([ExtractorErrors.SchemaViolation], Errors(Topics($$"""{"status":"covered","rating":4,"confidence":"high","quotes":["{{new string('a', 401)}}"]}""")));

    [Fact]
    public void A_missing_topic_or_an_unknown_topic_or_an_extra_top_level_field_is_rejected()
    {
        Assert.Equal([ExtractorErrors.SchemaViolation], Errors("""{"topics":{"onboarding":{"status":"no_data"}}}"""));
        Assert.Equal([ExtractorErrors.SchemaViolation], Errors(Topics(Covered).Replace("\"growth\"", "\"salary\"")));
        Assert.Equal([ExtractorErrors.SchemaViolation], Errors(Topics(Covered)[..^1] + ",\"overall_sentiment\":\"positive\"}"));
    }

    [Fact]
    public void Errors_never_contain_the_text_they_reject()
    {
        const string secret = "Brunhilda-Fogwhistle-secret";

        var codes = Errors(Topics($$"""{"status":"covered","rating":9,"confidence":"high","quotes":["{{secret}}"]}"""));

        Assert.DoesNotContain(codes, c => c.Contains(secret, StringComparison.Ordinal));
        Assert.All(codes, c => Assert.Matches("^extractor\\.[a-z_]+$", c));
    }

    [Fact]
    public void Oversized_or_too_deep_output_is_not_parsed()
    {
        Assert.Equal([ExtractorErrors.NotJson], Errors("{" + new string('x', 70_000) + "}"));
        Assert.Equal([ExtractorErrors.NotJson], Errors(string.Concat(Enumerable.Repeat("{\"a\":", 20)) + "1" + new string('}', 20)));
    }

    [Fact]
    public void The_valid_extraction_helper_used_elsewhere_in_the_tests_conforms() =>
        Assert.True(ExtractorOutput.TryParse(ValidExtraction(t => t == Topic.Culture ? "People shared knowledge freely here." : null), out _, out _));
}
