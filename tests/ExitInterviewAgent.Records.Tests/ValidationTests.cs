using System.Text;
using System.Text.Json;

namespace ExitInterviewAgent.Records.Tests;

public class ValidationTests
{
    private static readonly RecordValidator Validator = new();

    [Theory]
    [MemberData(nameof(Fixtures.Valid), MemberType = typeof(Fixtures))]
    public void Golden_valid_record_is_accepted_and_mapped(string file)
    {
        var outcome = Validator.Validate(Fixtures.Read("valid/" + file));

        Assert.True(outcome.IsValid, string.Join(", ", outcome.Errors));
        Assert.NotNull(outcome.Record);
    }

    [Theory]
    [MemberData(nameof(Fixtures.InvalidCases), MemberType = typeof(Fixtures))]
    public void Golden_invalid_record_is_rejected_with_the_expected_codes_and_paths(string name)
    {
        var outcome = Validator.Validate(Fixtures.Read($"invalid/{name}.json"));

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.Record);
        var actual = outcome.Errors.Select(e => $"{e.Code}@{e.Path}").Order().ToArray();
        Assert.Equal(Fixtures.InvalidExpectations()[name].Order().ToArray(), actual);
    }

    [Fact]
    public void Every_invalid_fixture_on_disk_has_an_expectation_and_vice_versa()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid");
        var files = Directory.GetFiles(dir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Where(n => n != "expectations");
        Assert.Equal(Fixtures.InvalidExpectations().Keys.Order(), files.Order());
    }

    [Fact]
    public void Every_error_code_the_fixtures_expect_is_a_published_code()
    {
        var published = RecordErrorCodes.All.ToHashSet();
        foreach (var code in Fixtures.InvalidExpectations().Values.SelectMany(v => v).Select(e => e.Split('@')[0]))
            Assert.Contains(code, published);
    }

    [Fact]
    public void Errors_never_echo_submitted_text()
    {
        const string secret = "jan.kowalski@example.com";
        var json = Fixtures.Read("valid/full.json")
            .Replace("\"schemaVersion\": \"1\",", $"\"schemaVersion\": \"1\", \"{secret}\": \"{secret}\", ")
            .Replace("\"tenureBand\": \"1y_3y\"", $"\"tenureBand\": \"{secret}\"")
            .Replace("\"employerRef\": \"acme-sp-zoo\"", $"\"employerRef\": \"{secret}\"");

        var outcome = Validator.Validate(json);

        Assert.False(outcome.IsValid);
        Assert.NotEmpty(outcome.Errors);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(outcome.Errors));
        Assert.All(outcome.Errors, e => Assert.DoesNotContain("example", e.Path));
    }

    [Fact]
    public void Unknown_submitted_keys_never_appear_in_a_path_even_deeper_in_the_tree()
    {
        var json = Fixtures.Read("valid/full.json").Replace("\"quotes\": [", "\"x@y.pl\": 1, \"quotes\": [");

        var outcome = Validator.Validate(json);

        Assert.Contains(outcome.Errors, e => e.Code == RecordErrorCodes.UnknownField);
        Assert.All(outcome.Errors, e => Assert.DoesNotContain("x@y.pl", e.Path));
    }

    [Fact]
    public void Paths_replace_names_the_schema_does_not_define_with_a_wildcard()
    {
        Assert.Equal("/topics/*/quotes/0", RecordValidator.SafePath("/topics/secret@x.pl/quotes/0"));
        Assert.Equal("/topics/culture/quotes/12", RecordValidator.SafePath("/topics/culture/quotes/12"));
        Assert.Equal("/*", RecordValidator.SafePath("/1234"));
        Assert.Equal("", RecordValidator.SafePath(""));
    }

    [Fact]
    public void Oversized_payload_is_rejected_before_parsing()
    {
        var payload = new byte[RecordLimits.Default.MaxPayloadBytes + 1];
        Array.Fill(payload, (byte)'x');

        var outcome = Validator.Validate(payload);

        Assert.Equal(RecordErrorCodes.PayloadTooLarge, Assert.Single(outcome.Errors).Code);
    }

    [Fact]
    public void A_maximal_valid_record_fits_comfortably_inside_the_default_size_limit()
    {
        var record = Fixtures.Sample(r => r with
        {
            Topics = r.Topics with
            {
                Culture = TopicEntry.Covered(3, Confidence.High, Enumerable.Repeat(new string('ż', TopicEntry.MaxQuoteLength), TopicEntry.MaxQuotes)),
            },
        });
        var bytes = RecordSerializer.SerializeCanonical(record);

        Assert.True(new RecordValidator().Validate(bytes).IsValid);
        Assert.True(bytes.Length < RecordLimits.Default.MaxPayloadBytes);
    }

    [Fact]
    public void Limits_are_configurable()
    {
        var tiny = new RecordValidator(new RecordLimits { MaxPayloadBytes = 10 });

        Assert.Equal(RecordErrorCodes.PayloadTooLarge, tiny.Validate(Fixtures.Read("valid/full.json")).Errors.Single().Code);
    }

    [Fact]
    public void Pii_masked_requirement_can_be_relaxed_for_internal_tooling()
    {
        var json = Fixtures.Read("valid/full.json").Replace("\"piiMasked\": true", "\"piiMasked\": false");

        Assert.True(new RecordValidator(new RecordLimits { RequirePiiMasked = false }).Validate(json).IsValid);
        Assert.Equal(RecordErrorCodes.PiiNotMasked, Validator.Validate(json).Errors.Single().Code);
    }

    [Fact]
    public void Many_errors_are_capped_and_flagged()
    {
        var json = Fixtures.Read("valid/full.json");
        var broken = new StringBuilder("{");
        for (var i = 0; i < 200; i++) broken.Append($"\"k{i}\":1,");
        broken.Append(json.TrimStart()[1..]);

        var outcome = new RecordValidator(new RecordLimits { MaxErrors = 3 }).Validate(broken.ToString());

        Assert.True(outcome.Errors.Count <= 3);
    }

    [Fact]
    public void Empty_input_is_not_json()
    {
        Assert.Equal(RecordErrorCodes.NotJson, Validator.Validate("").Errors.Single().Code);
    }

    [Fact]
    public void Invalid_utf8_is_not_json()
    {
        Assert.Equal(RecordErrorCodes.NotJson, Validator.Validate([0xFF, 0xFE, 0x7B, 0x7D]).Errors.Single().Code);
    }

    [Fact]
    public void Validator_is_safe_to_share_across_threads()
    {
        var json = Fixtures.Read("valid/full.json");
        var bad = Fixtures.Read("invalid/rating-6.json");
        var results = new bool[200];

        Parallel.For(0, results.Length, i =>
        {
            var ok = Validator.Validate(json).IsValid;
            var rejected = !Validator.Validate(bad).IsValid;
            results[i] = ok && rejected;
        });

        Assert.All(results, Assert.True);
    }
}
