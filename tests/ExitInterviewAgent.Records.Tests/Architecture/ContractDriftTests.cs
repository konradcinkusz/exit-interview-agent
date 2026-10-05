using System.Text.Json;

namespace ExitInterviewAgent.Records.Tests.Architecture;

/// <summary>The schema file, the embedded copy and the model's wire names must be one contract.</summary>
public class ContractDriftTests
{
    private static JsonElement Prop(JsonDocument d, params string[] path)
    {
        var e = d.RootElement;
        foreach (var p in path) e = e.GetProperty("properties").GetProperty(p);
        return e;
    }

    private static string[] Enum(JsonElement schema) => schema.GetProperty("enum").EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();

    [Fact]
    public void The_embedded_schema_is_byte_identical_to_the_published_file()
    {
        var published = File.ReadAllText(Path.Combine(Fixtures.RepoRoot(), "schemas", "exit-interview-record.v1.schema.json"));

        Assert.Equal(published, RecordSchema.JsonText);
    }

    [Fact]
    public void The_schema_declares_draft_2020_12_and_the_stable_id()
    {
        using var doc = JsonDocument.Parse(RecordSchema.JsonText);

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", doc.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(RecordSchema.Id, doc.RootElement.GetProperty("$id").GetString());
        Assert.Equal("1", Prop(doc, "schemaVersion").GetProperty("const").GetString());
        Assert.Equal(InterviewRecord.SchemaVersion, Prop(doc, "schemaVersion").GetProperty("const").GetString());
    }

    [Fact]
    public void Enum_wire_names_equal_the_schema_enums()
    {
        using var doc = JsonDocument.Parse(RecordSchema.JsonText);

        Assert.Equal(Wire.Names<TenureBand>(), Enum(Prop(doc, "context", "tenureBand")));
        Assert.Equal(Wire.Names<SeniorityBand>(), Enum(Prop(doc, "context", "seniorityBand")));
        Assert.Equal(Wire.Names<FunctionBand>(), Enum(Prop(doc, "context", "functionBand")));
        Assert.Equal(Wire.Names<DurationBand>(), Enum(Prop(doc, "interview", "durationBand")));
        Assert.Equal(Wire.Names<TurnBand>(), Enum(Prop(doc, "interview", "turnBand")));
        var topic = doc.RootElement.GetProperty("$defs").GetProperty("topic").GetProperty("properties");
        Assert.Equal(Wire.Names<TopicStatus>(), Enum(topic.GetProperty("status")));
        Assert.Equal(Wire.Names<Confidence>(), Enum(topic.GetProperty("confidence")));
        Assert.Equal(Wire.Names<Topic>(), Prop(doc, "topics").GetProperty("required").EnumerateArray().Select(x => x.GetString()!));
    }

    [Fact]
    public void Model_caps_equal_the_schema_caps()
    {
        using var doc = JsonDocument.Parse(RecordSchema.JsonText);
        var topic = doc.RootElement.GetProperty("$defs").GetProperty("topic").GetProperty("properties");
        var quote = doc.RootElement.GetProperty("$defs").GetProperty("quote");

        Assert.Equal(TopicEntry.MaxQuotes, topic.GetProperty("quotes").GetProperty("maxItems").GetInt32());
        Assert.Equal(TopicEntry.MaxQuoteLength, quote.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Every_error_code_is_unique_upper_snake_case()
    {
        Assert.Equal(RecordErrorCodes.All.Count, RecordErrorCodes.All.Distinct().Count());
        Assert.All(RecordErrorCodes.All, c => Assert.Matches("^[A-Z]+(_[A-Z]+)*$", c));
    }

    [Fact]
    public void Records_depends_on_nothing_but_the_framework_and_the_schema_validator()
    {
        var refs = typeof(InterviewRecord).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(refs, r => r.StartsWith("ExitInterviewAgent.", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.Contains("AspNetCore", StringComparison.Ordinal) || r.Contains("EntityFramework", StringComparison.Ordinal));
    }
}
