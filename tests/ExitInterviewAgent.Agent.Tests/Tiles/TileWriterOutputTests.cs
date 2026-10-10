using System.Text.Json;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

public class TileWriterOutputTests
{
    private static string Tile(string kind = "overview", string title = "Title", string text = "A short, neutral sentence.", string basedOn = "\"culture\"") =>
        $$"""{"kind":"{{kind}}","title":"{{title}}","text":"{{text}}","basedOn":[{{basedOn}}]}""";

    private static string Output(params string[] tiles) => $"{{\"tiles\":[{string.Join(",", tiles)}]}}";

    private static IReadOnlyList<string> Errors(string? text)
    {
        TileWriterOutput.TryParse(text, out _, out var codes);
        return codes;
    }

    [Fact]
    public void A_conforming_object_maps_kinds_titles_and_topic_lists()
    {
        var text = Output(Tile("overview", "Overview"), Tile("short_note", "Note", "Short.", "\"culture\",\"growth\""));

        Assert.True(TileWriterOutput.TryParse(text, out var o, out var codes));

        Assert.Empty(codes);
        Assert.Equal([TileKind.Overview, TileKind.ShortNote], o!.Tiles.Select(t => t.Kind));
        Assert.Equal("Overview", o.Tiles[0].Title);
        Assert.Equal(["culture", "growth"], o.Tiles[1].BasedOn);
    }

    [Fact]
    public void An_empty_tile_list_is_valid() =>
        Assert.True(TileWriterOutput.TryParse(Output(), out var o, out var codes) && o!.Tiles.Count == 0 && codes.Count == 0);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I could not write these tiles.")]
    [InlineData("{ not json }")]
    [InlineData("{\"tiles\":")]
    public void Not_json_is_reported_as_a_schema_code_only(string? text) =>
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(text));

    [Fact]
    public void Duplicate_keys_are_rejected() =>
        Assert.Equal([TileDropReason.SchemaInvalid], Errors("""{"tiles":[],"tiles":[]}"""));

    [Theory]
    [InlineData("""{"tiles":[],"facts":"extra"}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"T","text":"x","basedOn":["culture"],"score":1}]}""")]
    [InlineData("""{"tiles":[{"kind":"facts","title":"T","text":"x","basedOn":["culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"sentiment","title":"T","text":"x","basedOn":["culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"T","text":"x"}]}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"T","text":"x","basedOn":[]}]}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"T","text":"x","basedOn":["salary"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"T","text":"x","basedOn":["culture","culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"","text":"x","basedOn":["culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"overview","title":"T","text":"","basedOn":["culture"]}]}""")]
    public void Unknown_fields_kinds_and_shapes_are_rejected(string text) =>
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(text));

    [Fact]
    public void Two_tiles_of_the_same_kind_are_rejected() =>
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Tile("overview"), Tile("overview"))));

    [Fact]
    public void More_than_five_tiles_are_rejected() =>
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Tile("overview"), Tile("what_worked"), Tile("what_could_improve"), Tile("for_the_next_person"), Tile("short_note"), Tile("overview"))));

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Title_length_is_checked_in_characters(int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Tile(title: new string('t', length))), out _, out _));

    [Theory]
    [InlineData("overview", 600, true)]
    [InlineData("overview", 601, false)]
    [InlineData("short_note", 280, true)]
    [InlineData("short_note", 281, false)]
    public void Text_length_is_checked_per_kind(string kind, int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Tile(kind, text: new string('w', length))), out _, out _));

    [Fact]
    public void Errors_carry_codes_never_the_rejected_content()
    {
        const string secret = "SECRET-PHRASE-IN-THE-MODEL-REPLY";

        Assert.Equal([TileDropReason.SchemaInvalid], Errors($"{secret} {{\"tiles\":[{{\"kind\":\"{secret}\"}}]}}"));
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Tile("overview", text: new string('x', 601) + secret))));
        Assert.All(Errors(secret), c => Assert.DoesNotContain(secret, c));
    }

    [Fact]
    public void The_schema_text_limit_is_the_largest_kind_limit_and_each_kind_has_its_own_clause() =>
        // ADR-0075: the base limit is the Reddit limit; the other kinds are narrowed by if/then clauses in allOf.
        Assert.Equal(TileLimits.MaxTextCharsFor(TileKind.Reddit), Schema().GetProperty("$defs").GetProperty("tile").GetProperty("properties").GetProperty("text").GetProperty("maxLength").GetInt32());

    [Fact]
    public void The_schema_topic_list_is_the_record_topics()
    {
        var names = Schema().GetProperty("$defs").GetProperty("tile").GetProperty("properties").GetProperty("basedOn").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.Equal(Wire.Names<Topic>(), names);
    }

    [Fact]
    public void The_schema_kinds_are_the_model_kinds_and_never_facts()
    {
        var kinds = Schema().GetProperty("$defs").GetProperty("tile").GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        var expected = Enum.GetValues<TileKind>().Where(k => k != TileKind.Facts).Select(TileWriterOutput.KindName).ToList();
        Assert.Equal(expected, kinds);
        Assert.DoesNotContain("facts", kinds);
    }

    [Fact]
    public void The_published_schema_file_is_the_embedded_one()
    {
        var onDisk = File.ReadAllText(Path.Combine(TileTestSupport.RepoRoot(), "schemas", "tile-writer-output.v1.schema.json"));

        Assert.Equal(onDisk, TileWriterOutput.SchemaText);
    }

    private static JsonElement Schema() => JsonDocument.Parse(TileWriterOutput.SchemaText).RootElement;
}
