using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tiles;

// Contract for the "tiles" feature (ADR-0074, docs/architecture/draft-tiles.md). Tiles are short, neutral draft texts the
// interviewee may choose to publish. They are derived from the validated RECORD only, never from the transcript, and
// each one passes the code-side checks of ITileGuard before it is shown. Implementations live in separate files.

/// <summary>Which tile. <see cref="Facts"/> is built by code from the record; the others are worded by the model.</summary>
public enum TileKind { Facts, Overview, WhatWorked, WhatCouldImprove, ForTheNextPerson, ShortNote }

/// <summary>Why a candidate tile was not shown. Controlled vocabulary: letters and underscores, never content.</summary>
public static class TileDropReason
{
    public const string SchemaInvalid = "schema_invalid";
    public const string PiiFound = "pii_found";
    public const string Ungrounded = "ungrounded";
    public const string CopiedQuote = "copied_quote";
    public const string BannedTerm = "banned_term";
    public const string TooLong = "too_long";
    public const string Empty = "empty";
}

public static class TileLimits
{
    public const int MaxTitleChars = 60;
    public const int MaxTextChars = 600;
    /// <summary>The <see cref="TileKind.ShortNote"/> must fit a typical short public post.</summary>
    public const int ShortNoteMaxChars = 280;
    /// <summary>A tile may not repeat this many or more consecutive words from any quote of the record.</summary>
    public const int QuoteRunWordsForbidden = 7;
    /// <summary>At most this many topics may be named in one tile's <c>BasedOn</c>.</summary>
    public const int MaxBasedOn = 6;
}

/// <summary>
/// An untrusted proposal (model output after parsing). <paramref name="BasedOn"/> holds topic keys as they appear in the
/// record: onboarding, management, growth, pay_vs_promises, culture, reason_for_leaving.
/// </summary>
public sealed record CandidateTile(TileKind Kind, string Title, string Text, IReadOnlyList<string> BasedOn);

/// <summary>A tile that passed every check and may be shown.</summary>
public sealed record Tile(string Id, TileKind Kind, string Title, string Text, IReadOnlyList<string> BasedOn);

/// <summary>A tile that did not pass. Carries the kind and a <see cref="TileDropReason"/> code, never the text.</summary>
public sealed record DroppedTile(TileKind Kind, string ReasonCode);

/// <summary>What the user sees. <paramref name="Language"/> is the record's interview language.</summary>
public sealed record TileSet(string TilesVersion, string Language, IReadOnlyList<Tile> Tiles, IReadOnlyList<DroppedTile> Dropped)
{
    public const string CurrentVersion = "1";
}

/// <summary>Result of <see cref="ITileGuard.Check"/>: nothing is silently altered; a tile is accepted as is or dropped.</summary>
public sealed record TileGuardResult(IReadOnlyList<Tile> Accepted, IReadOnlyList<DroppedTile> Dropped);

/// <summary>
/// Words the model-written tiles from the record. Returns UNTRUSTED model output (JSON text); the caller parses it
/// against schemas/tile-writer-output.v1.schema.json. <paramref name="previousErrorCodes"/> lists
/// <see cref="TileDropReason"/> codes (never content) from a failed first attempt.
/// </summary>
public interface ITileWriter
{
    Task<string> WriteAsync(InterviewRecord record, IReadOnlyList<string> previousErrorCodes, CancellationToken ct);
}

/// <summary>
/// The code-side checks, in this order: length, banned terms, PII (fail closed), grounding (every <c>BasedOn</c> topic is
/// <c>covered</c> in the record; the tile may not discuss a <c>no_data</c> topic), quote copying. Deterministic, no model.
/// </summary>
public interface ITileGuard
{
    TileGuardResult Check(InterviewRecord record, IReadOnlyList<CandidateTile> candidates);
}
