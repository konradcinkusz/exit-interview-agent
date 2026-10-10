using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// The deterministic, model-free check on every candidate tile (ADR-0074). Checks run in this order and the first failure
/// decides the drop code: empty, too long, banned term, PII (fail closed), grounding, copied quote. A failing tile is
/// dropped, never edited: <see cref="Tile.Text"/> and <see cref="Tile.Title"/> are the candidate's own strings.
/// </summary>
public sealed partial class TileGuard(IPiiGuard pii) : ITileGuard
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>The topic keys as they appear in the record and in <c>BasedOn</c>.</summary>
    private static readonly Dictionary<string, Topic> TopicByKey = Enum.GetValues<Topic>().ToDictionary(t => Wire.Name(t), t => t);

    public TileGuardResult Check(InterviewRecord record, IReadOnlyList<CandidateTile> candidates)
    {
        var accepted = new List<Tile>();
        var dropped = new List<DroppedTile>();
        var seenKinds = new HashSet<TileKind>();
        var quoteRuns = QuoteRuns(record);

        foreach (var candidate in candidates)
        {
            // Structural rule first: one tile per kind, the first candidate of a kind wins.
            var reason = seenKinds.Add(candidate.Kind) ? Evaluate(record, candidate, quoteRuns) : TileDropReason.SchemaInvalid;
            if (reason is null) accepted.Add(new Tile(IdFor(candidate.Kind), candidate.Kind, candidate.Title, candidate.Text, (candidate.BasedOn ?? []).ToArray()));
            else dropped.Add(new DroppedTile(candidate.Kind, reason));
        }
        return new TileGuardResult(accepted, dropped);
    }

    /// <summary>The reason code for the first failing check, or null when the candidate passes.</summary>
    private string? Evaluate(InterviewRecord record, CandidateTile candidate, HashSet<string> quoteRuns)
    {
        var title = candidate.Title ?? string.Empty;
        var text = candidate.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text)) return TileDropReason.Empty;

        var textLimit = candidate.Kind == TileKind.ShortNote ? TileLimits.ShortNoteMaxChars : TileLimits.MaxTextChars;
        if (CodePoints(title) > TileLimits.MaxTitleChars || CodePoints(text) > textLimit) return TileDropReason.TooLong;

        if (BannedTerms().IsMatch(title) || BannedTerms().IsMatch(text)) return TileDropReason.BannedTerm;

        if (HasPii(title) || HasPii(text)) return TileDropReason.PiiFound;

        if (IsUngrounded(record, candidate, $"{title}\n{text}")) return TileDropReason.Ungrounded;

        if (CopiesQuote(quoteRuns, title) || CopiesQuote(quoteRuns, text)) return TileDropReason.CopiedQuote;

        return null;
    }

    /// <summary>Fail closed: a detector that fails, or any finding, drops the text.</summary>
    private bool HasPii(string text)
    {
        var masked = pii.Mask(text);
        return !masked.Ok || masked.Findings.Count > 0 || pii.HasFindings(text);
    }

    private static bool IsUngrounded(InterviewRecord record, CandidateTile candidate, string text)
    {
        var basedOn = candidate.BasedOn ?? [];
        // Facts is built by code from the covered topics and may cite none; every other kind must cite at least one.
        if (basedOn.Count == 0 && candidate.Kind != TileKind.Facts) return true;
        if (basedOn.Count > TileLimits.MaxBasedOn) return true;

        var cited = new HashSet<Topic>();
        foreach (var key in basedOn)
        {
            if (!TopicByKey.TryGetValue(key, out var topic)) return true;
            if (record.Topics[topic].Status != TopicStatus.Covered) return true;
            cited.Add(topic);
        }

        // A tile may not name a no_data topic it does not cite: the record says nothing about it.
        foreach (var (topic, entry) in record.Topics.Enumerate())
            if (entry.Status == TopicStatus.NoData && !cited.Contains(topic) && TopicName(topic).IsMatch(text)) return true;

        return false;
    }

    /// <summary>Every run of <see cref="TileLimits.QuoteRunWordsForbidden"/> words that occurs in any quote of the record.</summary>
    private static HashSet<string> QuoteRuns(InterviewRecord record)
    {
        var runs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, entry) in record.Topics.Enumerate())
            foreach (var quote in entry.Quotes)
            {
                var words = Words(quote);
                for (var i = 0; i + TileLimits.QuoteRunWordsForbidden <= words.Length; i++)
                    runs.Add(string.Join(' ', words, i, TileLimits.QuoteRunWordsForbidden));
            }
        return runs;
    }

    private static bool CopiesQuote(HashSet<string> quoteRuns, string text)
    {
        var words = Words(text);
        for (var i = 0; i + TileLimits.QuoteRunWordsForbidden <= words.Length; i++)
            if (quoteRuns.Contains(string.Join(' ', words, i, TileLimits.QuoteRunWordsForbidden))) return true;
        return false;
    }

    /// <summary>Lower case, punctuation and symbols removed: the words a quote is compared by.</summary>
    private static string[] Words(string text) =>
        Separator().Replace(text.ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static int CodePoints(string text) => text.EnumerateRunes().Count();

    private static string IdFor(TileKind kind) => kind switch
    {
        TileKind.Facts => "facts",
        TileKind.Overview => "overview",
        TileKind.WhatWorked => "what-worked",
        TileKind.WhatCouldImprove => "what-could-improve",
        TileKind.ForTheNextPerson => "for-the-next-person",
        TileKind.ShortNote => "short-note",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Terms a neutral tile must not use: accusations of crime or illegality, superlatives and value words, health, and
    /// motive phrases, in English and Polish, matched case-insensitively on word boundaries.
    /// This is a lexical floor, not a guarantee: a paraphrase that avoids these words passes. The writer prompt and the
    /// fixed notice carry the rest, and the person reviews what they publish.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:" +
        // accusations of crime or illegality
        @"illegal\w*|nielegaln\w*|criminal\w*|fraud\w*|oszustw\w*|harass\w*|molestow\w*|discrimin\w*|dyskrymin\w*|mobbing\w*|bribe\w*|łap[oó]\w*|" +
        // superlatives and value words
        @"worst|najgorsz\w*|terrible|okropn\w*|horrible|awful|toxic\w*|toksyczn\w*|nightmare\w*|koszmar\w*|beznadziejn\w*|" +
        // health
        @"depress\w*|burnout|wypalen\w*|illness\w*|choro\w*|" +
        // motives
        @"on\s+purpose|celowo|deliberately|intentionally" +
        @")\b", Opt)]
    private static partial Regex BannedTerms();

    /// <summary>How the record's topics are named in text, English and Polish. Used only for no_data topics.</summary>
    private static Regex TopicName(Topic topic) => topic switch
    {
        Topic.Onboarding => OnboardingName(),
        Topic.Management => ManagementName(),
        Topic.Growth => GrowthName(),
        Topic.PayVsPromises => PayName(),
        Topic.Culture => CultureName(),
        Topic.ReasonForLeaving => LeavingName(),
        _ => throw new ArgumentOutOfRangeException(nameof(topic)),
    };

    [GeneratedRegex(@"\b(?:onboard\w*|wdro[żz]eni\w*)", Opt)]
    private static partial Regex OnboardingName();

    [GeneratedRegex(@"\b(?:management|manager\w*|przełożon\w*|kierownik\w*)", Opt)]
    private static partial Regex ManagementName();

    [GeneratedRegex(@"\b(?:growth|rozw[oó]j\w*)", Opt)]
    private static partial Regex GrowthName();

    [GeneratedRegex(@"\b(?:pay\w*|salar\w*|promis\w*|wynagrodzeni\w*|p[łl]ac\w*|obietnic\w*)", Opt)]
    private static partial Regex PayName();

    [GeneratedRegex(@"\b(?:culture|kultur\w*)", Opt)]
    private static partial Regex CultureName();

    [GeneratedRegex(@"\b(?:leav\w*|odej[śs]\w*)", Opt)]
    private static partial Regex LeavingName();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separator();
}
