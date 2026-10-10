using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// Builds the tile set from the record. The Facts tile is built by code; the others come from the writer, are parsed,
/// and pass the guard. A rejected tile is dropped, never edited. If the writer's answer had rejected tiles or was
/// unusable, the writer is asked ONCE more, given only the codes. A provider failure is not retried.
/// Never throws for a model failure: the set then holds the Facts tile and the dropped codes. Cancellation propagates.
/// </summary>
public sealed class TileGenerator(ITileWriter writer, ITileGuard guard)
{
    private static readonly TileKind[] ModelKinds = Enum.GetValues<TileKind>().Where(k => k != TileKind.Facts).ToArray();

    public async Task<TileSet> GenerateAsync(InterviewRecord record, CancellationToken ct)
    {
        var facts = guard.Check(record, [FactsTile.Build(record)]);

        var first = await AttemptAsync(record, [], ct).ConfigureAwait(false);
        var final = first;
        var accepted = new List<Tile>(first.Accepted);
        var dropped = new Dictionary<TileKind, string>();
        foreach (var d in first.Dropped) dropped[d.Kind] = d.ReasonCode;

        if (first.Codes.Count > 0 && !first.Threw)
        {
            final = await AttemptAsync(record, first.Codes, ct).ConfigureAwait(false);
            foreach (var t in final.Accepted)
                if (accepted.All(a => a.Kind != t.Kind)) accepted.Add(t);
            foreach (var d in final.Dropped) dropped[d.Kind] = d.ReasonCode;
        }

        var acceptedKinds = accepted.Select(t => t.Kind).ToHashSet();
        foreach (var kind in acceptedKinds) dropped.Remove(kind);
        if (final.Unusable)
            foreach (var kind in ModelKinds.Where(k => !acceptedKinds.Contains(k)))
                dropped.TryAdd(kind, TileDropReason.SchemaInvalid);

        var tiles = facts.Accepted.Concat(accepted.OrderBy(t => t.Kind))
            .Select((t, i) => t with { Id = $"t{i + 1}" })
            .ToArray();
        var droppedAll = facts.Dropped
            .Concat(dropped.OrderBy(p => p.Key).Select(p => new DroppedTile(p.Key, p.Value)))
            .ToArray();
        return new TileSet(TileSet.CurrentVersion, TileWording.Language(record.Interview.Language), tiles, droppedAll);
    }

    private async Task<Attempt> AttemptAsync(InterviewRecord record, IReadOnlyList<string> previousCodes, CancellationToken ct)
    {
        string? text;
        try
        {
            text = await writer.WriteAsync(record, previousCodes, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return Attempt.Failed(threw: true);
        }

        if (!TileWriterOutput.TryParse(text, out var output, out _)) return Attempt.Failed(threw: false);

        var result = guard.Check(record, output!.Tiles);
        var codes = result.Dropped.Select(d => d.ReasonCode).Distinct().ToArray();
        return new Attempt(result.Accepted, result.Dropped, codes, Unusable: false, Threw: false);
    }

    /// <summary>
    /// One writer call after the guard. <see cref="Codes"/> are the reasons to retry (never content). <see cref="Unusable"/>:
    /// the answer was not usable at all, so the model kinds it did not provide count as dropped.
    /// </summary>
    private sealed record Attempt(IReadOnlyList<Tile> Accepted, IReadOnlyList<DroppedTile> Dropped, IReadOnlyList<string> Codes, bool Unusable, bool Threw)
    {
        public static Attempt Failed(bool threw) => new([], [], [TileDropReason.SchemaInvalid], Unusable: true, Threw: threw);
    }
}
