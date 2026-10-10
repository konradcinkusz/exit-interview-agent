using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Tiles.TileTestSupport;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

public class TileGeneratorTests
{
    private static readonly TileKind[] ModelKinds = Enum.GetValues<TileKind>().Where(k => k != TileKind.Facts).ToArray();

    private static TileGenerator Scripted() => new(new ModelTileWriter(new ScriptedChatClient()), new FakeGuard());

    private static async Task<string> ScriptedOutput(InterviewRecord record) =>
        await new ModelTileWriter(new ScriptedChatClient()).WriteAsync(record, [], CancellationToken.None);

    private static string One(string kind, string text) =>
        $$"""{"tiles":[{"kind":"{{kind}}","title":"Title","text":"{{text}}","basedOn":["culture"]}]}""";

    [Fact]
    public async Task A_golden_record_yields_the_facts_tile_and_the_scripted_tiles_with_no_drops()
    {
        var set = await Scripted().GenerateAsync(Full(), CancellationToken.None);

        Assert.Equal(new[] { TileKind.Facts, TileKind.Overview, TileKind.WhatWorked, TileKind.WhatCouldImprove, TileKind.ForTheNextPerson, TileKind.ShortNote }, set.Tiles.Select(t => t.Kind));
        Assert.Empty(set.Dropped);
        Assert.Equal(TileSet.CurrentVersion, set.TilesVersion);
        Assert.Equal("en", set.Language);
    }

    public static IEnumerable<object[]> Golden_files() => GoldenRecords();

    [Theory]
    [MemberData(nameof(Golden_files))]
    public async Task Every_golden_record_gives_model_tiles_that_cite_only_covered_topics(string name)
    {
        var record = Golden(name);

        var set = await Scripted().GenerateAsync(record, CancellationToken.None);

        Assert.Empty(set.Dropped);
        foreach (var tile in set.Tiles.Where(t => t.Kind != TileKind.Facts))
            Assert.All(tile.BasedOn, wire =>
            {
                Assert.True(Wire.TryParse<Topic>(wire, out var topic));
                Assert.Equal(TopicStatus.Covered, record.Topics[topic].Status);
            });
    }

    [Fact]
    public async Task A_no_data_topic_is_never_cited_by_any_tile()
    {
        var record = WithTopic(Full(), Topic.Growth, TopicEntry.NoData);

        var set = await Scripted().GenerateAsync(record, CancellationToken.None);

        Assert.All(set.Tiles, t => Assert.DoesNotContain("growth", t.BasedOn));
    }

    [Fact]
    public async Task The_same_record_gives_the_same_tiles_every_time()
    {
        var record = Full();
        var generator = Scripted();

        var a = await generator.GenerateAsync(record, CancellationToken.None);
        var b = await generator.GenerateAsync(record, CancellationToken.None);

        Assert.Equal(Describe(a), Describe(b));
        Assert.Equal(a.Dropped, b.Dropped);
    }

    [Fact]
    public async Task An_unusable_first_answer_is_retried_once_with_its_code_and_the_second_answer_is_used()
    {
        var record = Full();
        var valid = await ScriptedOutput(record);
        var writer = new StubWriter((n, _) => n == 1 ? "not json" : valid);

        var set = await new TileGenerator(writer, new FakeGuard()).GenerateAsync(record, CancellationToken.None);

        Assert.Equal(2, writer.Calls.Count);
        Assert.Empty(writer.Calls[0]);
        Assert.Equal([TileDropReason.SchemaInvalid], writer.Calls[1]);
        Assert.Contains(set.Tiles, t => t.Kind == TileKind.Overview);
        Assert.Empty(set.Dropped);
    }

    [Fact]
    public async Task A_guard_rejection_is_retried_once_with_its_code_and_a_second_answer_can_fill_the_kind()
    {
        var record = Full();
        var valid = await ScriptedOutput(record);
        var writer = new StubWriter((n, _) => n == 1 ? One("overview", "BANNED wording") : valid);

        var set = await new TileGenerator(writer, new FakeGuard(c => c.Text.Contains("BANNED", StringComparison.Ordinal) ? TileDropReason.BannedTerm : null))
            .GenerateAsync(record, CancellationToken.None);

        Assert.Equal(2, writer.Calls.Count);
        Assert.Equal([TileDropReason.BannedTerm], writer.Calls[1]);
        var overview = set.Tiles.Single(t => t.Kind == TileKind.Overview);
        Assert.DoesNotContain("BANNED", overview.Text);
        Assert.Empty(set.Dropped);
    }

    [Fact]
    public async Task The_retry_happens_once_only_and_the_remaining_model_tiles_are_recorded_as_dropped()
    {
        var writer = new StubWriter((_, _) => "not json");

        var set = await new TileGenerator(writer, new FakeGuard()).GenerateAsync(Full(), CancellationToken.None);

        Assert.Equal(2, writer.Calls.Count);
        Assert.Equal([TileKind.Facts], set.Tiles.Select(t => t.Kind));
        Assert.Equal(ModelKinds.OrderBy(k => k), set.Dropped.Select(d => d.Kind).OrderBy(k => k));
        Assert.All(set.Dropped, d => Assert.Equal(TileDropReason.SchemaInvalid, d.ReasonCode));
    }

    [Fact]
    public async Task A_provider_failure_gives_the_facts_tile_alone_is_not_retried_and_carries_no_provider_text()
    {
        var writer = new StubWriter((_, _) => throw new InvalidOperationException("CANARY provider text"));

        var set = await new TileGenerator(writer, new FakeGuard()).GenerateAsync(Full(), CancellationToken.None);

        Assert.Single(writer.Calls);
        Assert.Equal([TileKind.Facts], set.Tiles.Select(t => t.Kind));
        Assert.All(set.Dropped, d => Assert.Equal(TileDropReason.SchemaInvalid, d.ReasonCode));
        Assert.DoesNotContain("CANARY", Describe(set));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Scripted().GenerateAsync(Full(), cts.Token));
    }

    [Fact]
    public async Task The_facts_tile_is_built_by_code_and_never_contains_model_text()
    {
        var record = Full();
        var writer = new StubWriter((_, _) => One("overview", "MODEL-WROTE-THIS"));

        var set = await new TileGenerator(writer, new FakeGuard()).GenerateAsync(record, CancellationToken.None);

        var facts = set.Tiles.Single(t => t.Kind == TileKind.Facts);
        Assert.Equal(FactsTile.Build(record).Text, facts.Text);
        Assert.DoesNotContain("MODEL-WROTE-THIS", facts.Text);
        Assert.Contains("MODEL-WROTE-THIS", set.Tiles.Single(t => t.Kind == TileKind.Overview).Text);
    }

    [Fact]
    public async Task The_facts_tile_goes_through_the_guard_too()
    {
        var writer = new StubWriter((_, _) => "{\"tiles\":[]}");

        var set = await new TileGenerator(writer, new FakeGuard(c => c.Kind == TileKind.Facts ? TileDropReason.PiiFound : null)).GenerateAsync(Full(), CancellationToken.None);

        Assert.DoesNotContain(set.Tiles, t => t.Kind == TileKind.Facts);
        Assert.Equal([(TileKind.Facts, TileDropReason.PiiFound)], set.Dropped.Select(d => (d.Kind, d.ReasonCode)));
        Assert.Single(writer.Calls);
    }

    [Fact]
    public async Task The_language_of_the_set_follows_the_record_and_tile_ids_are_unique()
    {
        var set = await Scripted().GenerateAsync(WithLanguage(Full(), "pl"), CancellationToken.None);

        Assert.Equal("pl", set.Language);
        Assert.Equal(set.Tiles.Count, set.Tiles.Select(t => t.Id).Distinct().Count());
    }

    private static string Describe(TileSet set) =>
        string.Join("\n", set.Tiles.Select(t => $"{t.Id}|{t.Kind}|{t.Title}|{t.Text}|{string.Join(',', t.BasedOn)}"))
        + "\n" + string.Join(";", set.Dropped.Select(d => $"{d.Kind}:{d.ReasonCode}"));
}

/// <summary>A writer double: answers with <paramref name="respond"/> and records the codes each call was given.</summary>
internal sealed class StubWriter(Func<int, IReadOnlyList<string>, string> respond) : ITileWriter
{
    public List<string[]> Calls { get; } = [];

    public Task<string> WriteAsync(InterviewRecord record, IReadOnlyList<string> previousErrorCodes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Add(previousErrorCodes.ToArray());
        return Task.FromResult(respond(Calls.Count, previousErrorCodes));
    }
}

/// <summary>A guard double: drops a candidate when the rule names a code for it. Accepts everything else.</summary>
internal sealed class FakeGuard(Func<CandidateTile, string?>? rule = null) : ITileGuard
{
    public TileGuardResult Check(InterviewRecord record, IReadOnlyList<CandidateTile> candidates)
    {
        var accepted = new List<Tile>();
        var dropped = new List<DroppedTile>();
        foreach (var c in candidates)
        {
            if (rule?.Invoke(c) is { } code) dropped.Add(new DroppedTile(c.Kind, code));
            else accepted.Add(new Tile("guard", c.Kind, c.Title, c.Text, c.BasedOn));
        }
        return new TileGuardResult(accepted, dropped);
    }
}
