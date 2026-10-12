using System.Text.Json;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

public class TileWriterOutputTests
{
    private static string Tile(string kind = "overview", string title = "Title", string text = "A short, neutral sentence.", string basedOn = "\"culture\"") =>
        $$"""{"kind":"{{kind}}","title":"{{title}}","text":"{{text}}","basedOn":[{{basedOn}}]}""";

    /// <summary>A family element (ADR-0083): its sections, written by the serializer so that no quoting is hand-made.</summary>
    private static string Family(string kind, params (string Key, string Text)[] sections) =>
        JsonSerializer.Serialize(new
        {
            kind,
            sections = sections.Select(s => new { key = s.Key, text = s.Text }),
            basedOn = new[] { "culture" },
        });

    private static string Output(params string[] tiles) => $"{{\"tiles\":[{string.Join(",", tiles)}]}}";

    private static IReadOnlyList<string> Errors(string? text)
    {
        TileWriterOutput.TryParse(text, out _, out var codes);
        return codes;
    }

    private static string Chars(int n) => new('w', n);

    [Fact]
    public void A_conforming_object_maps_kinds_titles_and_topic_lists()
    {
        var text = Output(Tile("overview", "Overview"), Tile("what_worked", "Note", "Short.", "\"culture\",\"growth\""));

        Assert.True(TileWriterOutput.TryParse(text, out var o, out var codes));

        Assert.Empty(codes);
        Assert.Equal([TileKind.Overview, TileKind.WhatWorked], o!.Tiles.Select(t => t.Kind));
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
    [InlineData("""{"tiles":[{"kind":"short_note","title":"T","text":"x","basedOn":["culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"glassdoor","title":"T","text":"x","basedOn":["culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"google_review","title":"T","text":"x","basedOn":["culture"]}]}""")]
    [InlineData("""{"tiles":[{"kind":"reddit","title":"T","text":"x","basedOn":["culture"]}]}""")]
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
    public void More_than_eight_tiles_are_rejected()
    {
        var nine = Enumerable.Range(0, 9).Select(_ => Tile("overview")).ToArray();

        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(nine)));
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Title_length_is_checked_in_characters(int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Tile(title: Chars(length))), out _, out _));

    [Theory]
    [InlineData("overview", 600, true)]
    [InlineData("overview", 601, false)]
    [InlineData("what_worked", 600, true)]
    [InlineData("what_worked", 601, false)]
    public void Text_length_is_checked_for_a_generic_kind(string kind, int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Tile(kind, text: Chars(length))), out _, out _));

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void A_structured_review_section_is_limited_to_500(int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Family("structured_review", ("title", "T"), ("pros", Chars(length)), ("cons", "Some cons here."))), out _, out _));

    [Theory]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public void A_narrative_body_is_limited_to_4000(int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Family("narrative", ("title", "T"), ("body", Chars(length)))), out _, out _));

    [Theory]
    [InlineData(1300, true)]
    [InlineData(1301, false)]
    public void A_professional_post_body_is_limited_to_1300(int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Family("professional_post", ("body", Chars(length)))), out _, out _));

    [Theory]
    [InlineData(270, true)]
    [InlineData(271, false)]
    public void A_short_post_body_is_limited_to_270(int length, bool valid) =>
        Assert.Equal(valid, TileWriterOutput.TryParse(Output(Family("short_post", ("body", Chars(length)))), out _, out _));

    [Fact]
    public void A_family_kind_keeps_its_sections_and_its_text_is_them_joined_by_a_blank_line()
    {
        var text = Output(Family("structured_review", ("title", "Notes"), ("pros", "Good team."), ("cons", "Slow onboarding.")));

        Assert.True(TileWriterOutput.TryParse(text, out var o, out var codes));

        Assert.Empty(codes);
        var tile = Assert.Single(o!.Tiles);
        Assert.Equal(TileKind.StructuredReview, tile.Kind);
        Assert.Equal("Notes", tile.Title);
        Assert.Equal("Notes\n\nGood team.\n\nSlow onboarding.", tile.Text);
        Assert.Equal(
            [new TileSection("title", "Notes"), new TileSection("pros", "Good team."), new TileSection("cons", "Slow onboarding.")],
            tile.Sections!);
    }

    [Fact]
    public void A_post_family_has_no_title()
    {
        Assert.True(TileWriterOutput.TryParse(Output(Family("short_post", ("body", "One short post."))), out var o, out _));

        Assert.Equal(string.Empty, Assert.Single(o!.Tiles).Title);
    }

    [Theory]
    [InlineData("structured_review", new[] { "title", "pros" })]
    [InlineData("structured_review", new[] { "pros", "cons" })]
    [InlineData("narrative", new[] { "title" })]
    [InlineData("narrative", new[] { "title", "pros" })]
    [InlineData("professional_post", new[] { "title" })]
    [InlineData("short_post", new[] { "body", "advice" })]
    public void A_family_missing_a_required_section_or_carrying_a_foreign_one_is_rejected(string kind, string[] keys) =>
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Family(kind, keys.Select(k => (k, "Some text here.")).ToArray()))));

    [Fact]
    public void A_family_with_a_title_or_text_field_of_its_own_is_rejected()
    {
        var withText = $$"""{"kind":"narrative","title":"T","text":"x","sections":[{"key":"title","text":"T"},{"key":"body","text":"Body."}],"basedOn":["culture"]}""";

        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(withText)));
    }

    [Fact]
    public void A_section_key_repeated_within_a_family_is_rejected()
    {
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Family("narrative", ("body", "One."), ("body", "Two.")))));
    }

    [Fact]
    public void Errors_carry_codes_never_the_rejected_content()
    {
        const string secret = "SECRET-PHRASE-IN-THE-MODEL-REPLY";

        Assert.Equal([TileDropReason.SchemaInvalid], Errors($"{secret} {{\"tiles\":[{{\"kind\":\"{secret}\"}}]}}"));
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Tile("overview", text: new string('x', 601) + secret))));
        Assert.Equal([TileDropReason.SchemaInvalid], Errors(Output(Family("narrative", ("title", "T"), ("body", new string('x', 4001) + secret)))));
        Assert.All(Errors(secret), c => Assert.DoesNotContain(secret, c));
    }

    [Fact]
    public void The_schema_base_text_limit_is_the_generic_limit_and_the_families_are_narrowed_by_their_own_clauses()
    {
        var text = Schema().GetProperty("$defs").GetProperty("tile").GetProperty("properties").GetProperty("text").GetProperty("maxLength").GetInt32();

        Assert.Equal(TileLimits.MaxTextChars, text);
    }

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

        // Version 2 writes the four general kinds and the four family kinds (ADR-0083). The Facts tile is built by code.
        var expected = Enum.GetValues<TileKind>().Where(k => k != TileKind.Facts).Select(TileWriterOutput.KindName).ToList();
        Assert.Equal(expected, kinds);
        Assert.DoesNotContain("facts", kinds);
    }

    [Fact]
    public void The_published_schema_file_is_the_embedded_one()
    {
        var onDisk = File.ReadAllText(Path.Combine(TileTestSupport.RepoRoot(), "schemas", "tile-writer-output.v2.schema.json"));

        Assert.Equal(onDisk, TileWriterOutput.SchemaText);
    }

    private static JsonElement Schema() => JsonDocument.Parse(TileWriterOutput.SchemaText).RootElement;
}
