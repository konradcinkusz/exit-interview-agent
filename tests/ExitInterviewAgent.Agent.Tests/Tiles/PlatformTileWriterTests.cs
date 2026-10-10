using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Tiles.TileTestSupport;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

/// <summary>Platform tiles (ADR-0075): the writer's transcript input, the output schema and parser, the scripted mock, the generator.</summary>
public class PlatformTileWriterTests
{
    private const string Transcript =
        "Interviewer: What was the first month like?\n" +
        "Interviewee: I was bullied by my lead in the second month.\n";

    private static string Tile(string kind, string text, string basedOn = "\"culture\"") =>
        $$"""{"kind":"{{kind}}","title":"Title","text":"{{text}}","basedOn":[{{basedOn}}]}""";

    private static string Output(params string[] tiles) => $"{{\"tiles\":[{string.Join(",", tiles)}]}}";

    private static string Chars(int n) => new('a', n);

    [Fact]
    public async Task With_a_transcript_the_writer_sends_it_inside_its_own_data_markers_and_still_sends_the_record()
    {
        var model = new FakeChatClient((_, _) => "{\"tiles\":[]}");

        await new ModelTileWriter(model).WriteAsync(new TileInput(Full(), Transcript), [], CancellationToken.None);

        var call = Assert.Single(model.Calls);
        Assert.Contains(TilePrompts.TranscriptBegin, call.User, StringComparison.Ordinal);
        Assert.Contains(TilePrompts.TranscriptEnd, call.User, StringComparison.Ordinal);
        // Each line is one JSON string, so a line of interviewee text cannot open a new marker or a new turn.
        Assert.Contains("\"Interviewee: I was bullied by my lead in the second month.\"", call.User, StringComparison.Ordinal);
        Assert.Contains(TilePrompts.RecordBegin, call.User, StringComparison.Ordinal);
        Assert.Contains("TRUST BOUNDARY", call.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_transcript_the_writer_sends_no_transcript_block()
    {
        var model = new FakeChatClient((_, _) => "{\"tiles\":[]}");

        await new ModelTileWriter(model).WriteAsync(new TileInput(Full()), [], CancellationToken.None);

        Assert.DoesNotContain(TilePrompts.TranscriptBegin, Assert.Single(model.Calls).User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_record_only_overload_still_works_and_sends_no_transcript()
    {
        var model = new FakeChatClient((_, _) => "{\"tiles\":[]}");

        await new ModelTileWriter(model).WriteAsync(Full(), [], CancellationToken.None);

        Assert.DoesNotContain(TilePrompts.TranscriptBegin, Assert.Single(model.Calls).User, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("glassdoor", TileKind.Glassdoor)]
    [InlineData("google_review", TileKind.GoogleReview)]
    [InlineData("reddit", TileKind.Reddit)]
    public void The_three_platform_kinds_parse_to_their_tile_kinds(string wire, TileKind kind)
    {
        Assert.True(TileWriterOutput.TryParse(Output(Tile(wire, "A short, neutral sentence.")), out var o, out var codes));

        Assert.Empty(codes);
        Assert.Equal(kind, Assert.Single(o!.Tiles).Kind);
    }

    [Theory]
    [InlineData("glassdoor", 900, true)]
    [InlineData("glassdoor", 901, false)]
    [InlineData("google_review", 500, true)]
    [InlineData("google_review", 501, false)]
    [InlineData("reddit", 4500, true)]
    [InlineData("reddit", 4501, false)]
    [InlineData("overview", 600, true)]
    [InlineData("overview", 601, false)]
    [InlineData("short_note", 280, true)]
    [InlineData("short_note", 281, false)]
    public void The_schema_limits_each_kind_to_its_own_length(string kind, int length, bool accepted)
    {
        var ok = TileWriterOutput.TryParse(Output(Tile(kind, Chars(length))), out _, out var codes);

        Assert.Equal(accepted, ok);
        if (!accepted) Assert.Equal([TileDropReason.SchemaInvalid], codes);
    }

    [Fact]
    public void The_schema_allows_at_most_eight_model_tiles()
    {
        var nine = Enumerable.Range(0, 9).Select(_ => Tile("reddit", "A short sentence.")).ToArray();

        Assert.False(TileWriterOutput.TryParse(Output(nine), out _, out var codes));
        Assert.Equal([TileDropReason.SchemaInvalid], codes);
    }

    [Fact]
    public void The_schema_text_names_the_three_platform_kinds_and_the_eight_model_kinds()
    {
        var schema = TileWriterOutput.SchemaText;

        Assert.Contains("\"glassdoor\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"google_review\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"reddit\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"maxItems\": 8", schema, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Golden_files_with_a_covered_topic))]
    public async Task The_scripted_writer_offers_the_three_platform_tiles_for_every_golden_record_with_a_covered_topic(string name)
    {
        var text = await new ModelTileWriter(new ScriptedChatClient()).WriteAsync(Golden(name), [], CancellationToken.None);

        Assert.True(TileWriterOutput.TryParse(text, out var output, out var codes), string.Join(",", codes));
        var kinds = output!.Tiles.Select(t => t.Kind).ToHashSet();
        Assert.Contains(TileKind.Glassdoor, kinds);
        Assert.Contains(TileKind.GoogleReview, kinds);
        Assert.Contains(TileKind.Reddit, kinds);
        Assert.All(output.Tiles.Where(t => t.Kind is TileKind.Glassdoor or TileKind.GoogleReview or TileKind.Reddit), t => Assert.NotEmpty(t.BasedOn));
    }

    public static IEnumerable<object[]> Golden_files() => GoldenRecords();

    /// <summary>The golden records with at least one covered topic: the mock writes no model tiles for a record with none.</summary>
    public static IEnumerable<object[]> Golden_files_with_a_covered_topic() =>
        GoldenRecords().Where(f => Golden((string)f[0]).Topics.Enumerate().Any(p => p.Item2.Status == TopicStatus.Covered));

    [Fact]
    public async Task The_scripted_platform_tiles_use_no_experience_words_and_no_protected_terms()
    {
        var text = await new ModelTileWriter(new ScriptedChatClient()).WriteAsync(Full(), [], CancellationToken.None);

        Assert.DoesNotMatch(new Regex(@"mobb|bull|harass|molestow|discrimin|dyskrymin|toxic|toksyczn|prześladow|illegal|nielegaln", RegexOptions.IgnoreCase), text);
    }

    [Fact]
    public async Task The_scripted_glassdoor_entry_has_the_three_blocks_in_english_and_in_polish()
    {
        var en = await GlassdoorText(Full());
        var pl = await GlassdoorText(WithLanguage(Full(), "pl"));

        Assert.Contains("Pros:", en, StringComparison.Ordinal);
        Assert.Contains("Cons:", en, StringComparison.Ordinal);
        Assert.Contains("Advice to management:", en, StringComparison.Ordinal);
        Assert.Contains("Plusy:", pl, StringComparison.Ordinal);
        Assert.Contains("Minusy:", pl, StringComparison.Ordinal);
        Assert.Contains("Rada dla zarządu:", pl, StringComparison.Ordinal);
    }

    /// <summary>The decoded text of the scripted glassdoor tile (the JSON escapes non-ASCII letters, so the raw reply is not compared).</summary>
    private static async Task<string> GlassdoorText(InterviewRecord record)
    {
        var text = await new ModelTileWriter(new ScriptedChatClient()).WriteAsync(record, [], CancellationToken.None);
        Assert.True(TileWriterOutput.TryParse(text, out var output, out _));
        return output!.Tiles.Single(t => t.Kind == TileKind.Glassdoor).Text;
    }

    [Fact]
    public async Task The_generator_with_the_real_guard_yields_the_facts_tile_and_up_to_eight_model_tiles_with_no_drops()
    {
        var generator = new TileGenerator(new ModelTileWriter(new ScriptedChatClient()), new TileGuard(new PiiGuard()));

        var set = await generator.GenerateAsync(new TileInput(Full(), Transcript), CancellationToken.None);

        Assert.Empty(set.Dropped);
        Assert.Contains(TileKind.Glassdoor, set.Tiles.Select(t => t.Kind));
        Assert.Contains(TileKind.GoogleReview, set.Tiles.Select(t => t.Kind));
        Assert.Contains(TileKind.Reddit, set.Tiles.Select(t => t.Kind));
        Assert.InRange(set.Tiles.Count, 1, 9);
        Assert.Equal(set.Tiles.Count, set.Tiles.Select(t => t.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Golden_files))]
    public async Task Every_golden_record_with_the_real_guard_gives_no_drops(string name)
    {
        var generator = new TileGenerator(new ModelTileWriter(new ScriptedChatClient()), new TileGuard(new PiiGuard()));

        var set = await generator.GenerateAsync(new TileInput(Golden(name), Transcript), CancellationToken.None);

        Assert.Empty(set.Dropped);
    }

    [Fact]
    public async Task The_record_overload_and_the_input_overload_give_the_same_set_without_a_transcript()
    {
        var generator = new TileGenerator(new ModelTileWriter(new ScriptedChatClient()), new TileGuard(new PiiGuard()));

        var a = await generator.GenerateAsync(Full(), CancellationToken.None);
        var b = await generator.GenerateAsync(new TileInput(Full()), CancellationToken.None);

        Assert.Equal(a.Tiles.Select(t => (t.Id, t.Kind, t.Text)), b.Tiles.Select(t => (t.Id, t.Kind, t.Text)));
    }

    [Fact]
    public async Task The_default_input_overload_of_the_writer_delegates_to_the_record_overload()
    {
        var writer = new StubWriter((_, _) => "{\"tiles\":[]}");

        await ((ITileWriter)writer).WriteAsync(new TileInput(Full(), Transcript), [], CancellationToken.None);

        Assert.Single(writer.Calls);
    }

    [Fact]
    public void The_default_input_overload_of_the_guard_delegates_to_the_record_overload()
    {
        ITileGuard guard = new FakeGuard(c => c.Kind == TileKind.Reddit ? TileDropReason.BannedTerm : null);

        var result = guard.Check(new TileInput(Full()), [new CandidateTile(TileKind.Reddit, "T", "Text.", ["culture"])]);

        Assert.Equal([TileDropReason.BannedTerm], result.Dropped.Select(d => d.ReasonCode));
    }
}
