using System.Text;

namespace ExitInterviewAgent.Records.Tests;

public class SerializationTests
{
    private static readonly RecordValidator Validator = new();

    [Theory]
    [MemberData(nameof(Fixtures.Valid), MemberType = typeof(Fixtures))]
    public void Canonical_form_round_trips_to_an_equal_record(string file)
    {
        var record = Validator.Validate(Fixtures.Read("valid/" + file)).Record!;

        var canonical = RecordSerializer.SerializeCanonical(record);
        var back = Validator.Validate(canonical);

        Assert.True(back.IsValid, string.Join(", ", back.Errors));
        Assert.Equal(record, back.Record);
        Assert.Equal(canonical, RecordSerializer.SerializeCanonical(back.Record!));
    }

    [Fact]
    public void Canonical_bytes_do_not_depend_on_input_formatting_or_property_order()
    {
        var pretty = Fixtures.Read("valid/minimal-all-no-data.json");
        var reordered = "{\"piiMasked\":true,\"topics\":" + Extract(pretty, "topics") + ",\"interview\":" + Extract(pretty, "interview") +
                        ",\"context\":{\"tenureBand\":\"lt_6m\"},\"employerRef\":\"abc\",\"interviewId\":\"ffffffffffffffffffffffffffffffff\",\"schemaVersion\":\"1\"}";

        var a = RecordSerializer.SerializeCanonical(Validator.Validate(pretty).Record!);
        var b = RecordSerializer.SerializeCanonical(Validator.Validate(reordered).Record!);

        Assert.Equal(a, b);
        Assert.StartsWith(
            "{\"schemaVersion\":\"1\",\"interviewId\":\"ffffffffffffffffffffffffffffffff\",\"employerRef\":\"abc\",\"context\":{\"tenureBand\":\"lt_6m\"},\"topics\":{\"onboarding\":",
            Encoding.UTF8.GetString(a), StringComparison.Ordinal);
        Assert.DoesNotContain((byte)'\n', a);
    }

    [Fact]
    public void Optional_bands_are_omitted_when_absent_and_never_written_as_null()
    {
        var text = RecordSerializer.SerializeCanonicalString(Validator.Validate(Fixtures.Read("valid/minimal-all-no-data.json")).Record!);

        Assert.DoesNotContain("seniorityBand", text);
        Assert.DoesNotContain("functionBand", text);
    }

    [Fact]
    public void Non_ascii_text_is_escaped_so_the_form_is_encoder_independent()
    {
        var text = RecordSerializer.SerializeCanonicalString(Validator.Validate(Fixtures.Read("valid/full.json")).Record!);

        Assert.All(text, c => Assert.True(c < 128));
    }

    [Fact]
    public void Serialized_records_always_pass_the_schema()
    {
        var record = Fixtures.Sample(r => r with
        {
            Context = new RecordContext(TenureBand.OverTenYears, null, FunctionBand.Other),
            Topics = r.Topics with { Growth = TopicEntry.NoData, Culture = TopicEntry.Covered(null, Confidence.Low, ["q"]) },
        });

        Assert.True(Validator.Validate(RecordSerializer.SerializeCanonical(record)).IsValid);
    }

    private static string Extract(string json, string key)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(key).GetRawText();
    }
}
