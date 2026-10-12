using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Tiles.TileTestSupport;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

/// <summary>
/// Family drafts (ADR-0075, amended by ADR-0083): the writer's transcript input, the output parser for the family sections, the
/// scripted mock in both languages, and the generator with the real guard.
/// </summary>
public class PlatformTileWriterTests
{
    private const string Transcript =
        "Interviewer: What was the first month like?\n" +
        "Interviewee: I was bullied by my lead in the second month.\n";

    private static string Output(params string[] tiles) => $"{{\"tiles\":[{string.Join(",", tiles)}]}}";

    /// <summary>A family element with the sections the family needs, long enough for the guard's minimums.</summary>
    private static string Family(string kind, string body) => kind switch
    {
        "structured_review" =>
            $$"""{"kind":"structured_review","sections":[{"key":"title","text":"Notes"},{"key":"pros","text":"{{body}}"},{"key":"cons","text":"{{body}}"}],"basedOn":["culture"]}""",
        "narrative" =>
            $$"""{"kind":"narrative","sections":[{"key":"title","text":"Notes"},{"key":"body","text":"{{body}}"}],"basedOn":["culture"]}""",
        _ => $$"""{"kind":"{{kind}}","sections":[{"key":"body","text":"{{body}}"}],"basedOn":["culture"]}""",
    };

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

    [Fact]
    public void The_writer_prompt_lists_the_four_families_from_the_table_with_their_limits()
    {
        var system = TilePrompts.WriterSystem;

        Assert.Contains("- structured_review:", system, StringComparison.Ordinal);
        Assert.Contains("- narrative:", system, StringComparison.Ordinal);
        Assert.Contains("- professional_post:", system, StringComparison.Ordinal);
        Assert.Contains("- short_post:", system, StringComparison.Ordinal);
        Assert.Contains("\"pros\" 40-500 characters", system, StringComparison.Ordinal);
        Assert.Contains("Whole draft at most 1100 characters", system, StringComparison.Ordinal);
        Assert.Contains("\"body\" 600-4000 characters", system, StringComparison.Ordinal);
        Assert.Contains("\"body\" 80-270 characters", system, StringComparison.Ordinal);
        Assert.DoesNotContain("glassdoor", system, StringComparison.Ordinal);
        Assert.DoesNotContain("google_review", system, StringComparison.Ordinal);
        Assert.DoesNotContain("short_note", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_prompt_allows_the_experience_words_only_in_the_narrative()
    {
        var system = TilePrompts.WriterSystem;

        Assert.Contains("ONLY in a narrative", system, StringComparison.Ordinal);
        Assert.Contains("Do not use the experience words listed above.", system, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("structured_review", TileKind.StructuredReview)]
    [InlineData("narrative", TileKind.Narrative)]
    [InlineData("professional_post", TileKind.ProfessionalPost)]
    [InlineData("short_post", TileKind.ShortPost)]
    public void The_four_family_kinds_parse_to_their_tile_kinds(string wire, TileKind kind)
    {
        Assert.True(TileWriterOutput.TryParse(Output(Family(wire, "A short, neutral sentence.")), out var o, out var codes), string.Join(",", codes));

        Assert.Empty(codes);
        Assert.Equal(kind, Assert.Single(o!.Tiles).Kind);
    }

    [Fact]
    public void The_schema_text_names_the_four_families_and_the_eight_model_tiles()
    {
        var schema = TileWriterOutput.SchemaText;

        Assert.Contains("\"structured_review\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"narrative\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"professional_post\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"short_post\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"maxItems\": 8", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"glassdoor\"", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"google_review\"", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reddit\"", schema, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Golden_files_with_a_covered_topic))]
    public async Task The_scripted_writer_offers_the_four_families_for_every_golden_record_with_a_covered_topic(string name)
    {
        var text = await new ModelTileWriter(new ScriptedChatClient()).WriteAsync(Golden(name), [], CancellationToken.None);

        Assert.True(TileWriterOutput.TryParse(text, out var output, out var codes), string.Join(",", codes));
        var kinds = output!.Tiles.Select(t => t.Kind).ToHashSet();
        Assert.Contains(TileKind.StructuredReview, kinds);
        Assert.Contains(TileKind.Narrative, kinds);
        Assert.Contains(TileKind.ProfessionalPost, kinds);
        Assert.Contains(TileKind.ShortPost, kinds);
        Assert.All(output.Tiles.Where(t => TileFamilies.Find(t.Kind) is not null), t => Assert.NotEmpty(t.BasedOn));
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
    public async Task The_scripted_structured_review_has_pros_cons_and_advice_in_english_and_in_polish()
    {
        var en = await SectionKeys(Full());
        var pl = await SectionKeys(WithLanguage(Full(), "pl"));

        Assert.Equal(["title", "pros", "cons", "advice"], en);
        Assert.Equal(["title", "pros", "cons", "advice"], pl);
    }

    /// <summary>The section keys of the scripted structured review, in the writer's order.</summary>
    private static async Task<string[]> SectionKeys(InterviewRecord record)
    {
        var text = await new ModelTileWriter(new ScriptedChatClient()).WriteAsync(record, [], CancellationToken.None);
        Assert.True(TileWriterOutput.TryParse(text, out var output, out _));
        return output!.Tiles.Single(t => t.Kind == TileKind.StructuredReview).Sections!.Select(s => s.Key).ToArray();
    }

    [Fact]
    public async Task The_generator_with_the_real_guard_yields_the_facts_tile_and_the_four_families_with_no_drops()
    {
        var generator = new TileGenerator(new ModelTileWriter(new ScriptedChatClient()), new TileGuard(new PiiGuard()));

        var set = await generator.GenerateAsync(new TileInput(Full(), Transcript), CancellationToken.None);

        Assert.Empty(set.Dropped);
        Assert.Contains(TileKind.StructuredReview, set.Tiles.Select(t => t.Kind));
        Assert.Contains(TileKind.Narrative, set.Tiles.Select(t => t.Kind));
        Assert.Contains(TileKind.ProfessionalPost, set.Tiles.Select(t => t.Kind));
        Assert.Contains(TileKind.ShortPost, set.Tiles.Select(t => t.Kind));
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
        ITileGuard guard = new FakeGuard(c => c.Kind == TileKind.Narrative ? TileDropReason.BannedTerm : null);

        var result = guard.Check(new TileInput(Full()), [new CandidateTile(TileKind.Narrative, "T", "Text.", ["culture"])]);

        Assert.Equal([TileDropReason.BannedTerm], result.Dropped.Select(d => d.ReasonCode));
    }
}
