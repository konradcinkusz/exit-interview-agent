using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// The deterministic, model-free check on every candidate tile (ADR-0074, amended by ADR-0075 and ADR-0083). Checks run in this order
/// and the first failure decides the drop code: empty, too long or too short (per section for a family), banned term (absolute,
/// then the tiered experience terms), PII (fail closed), identifying detail, grounding, copied quote. A failing tile is dropped,
/// never edited: <see cref="Tile.Text"/>, <see cref="Tile.Title"/> and each section are the candidate's own strings.
/// </summary>
public sealed partial class TileGuard(IPiiGuard pii) : ITileGuard
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>The topic keys as they appear in the record and in <c>BasedOn</c>.</summary>
    private static readonly Dictionary<string, Topic> TopicByKey = Enum.GetValues<Topic>().ToDictionary(t => Wire.Name(t), t => t);

    /// <summary>
    /// The experience terms, one family per root. Allowed only in the narrative family, and only when the same family occurs in the
    /// interviewee's own words and every sentence that uses it is framed as the author's experience (see <see cref="FirstPerson"/>).
    /// </summary>
    private static readonly Regex[] ExperienceFamilies =
    [
        new(@"\bmobb\w*", Opt),
        new(@"\bbull(?:y|ied|ying|ies)\w*", Opt),
        new(@"\b(?:prześladow|prześladuj)\w*", Opt),
        new(@"\bharass\w*", Opt),
        new(@"\bmolestow\w*", Opt),
        new(@"\bdiscriminat\w*", Opt),
        new(@"\bdyskrymin\w*", Opt),
        new(@"\btoxic\w*", Opt),
        new(@"\btoksyczn\w*", Opt),
    ];

    /// <summary>Text of the company token the writer may leave in place of the employer's name.</summary>
    private static readonly string[] CompanyTokens = ["[COMPANY]", "[FIRMA]"];

    public TileGuardResult Check(InterviewRecord record, IReadOnlyList<CandidateTile> candidates) =>
        Check(new TileInput(record), candidates);

    public TileGuardResult Check(TileInput input, IReadOnlyList<CandidateTile> candidates)
    {
        var accepted = new List<Tile>();
        var dropped = new List<DroppedTile>();
        var seenKinds = new HashSet<TileKind>();
        var haystack = Haystack.For(input);

        foreach (var candidate in candidates)
        {
            // Structural rule first: one tile per kind, the first candidate of a kind wins.
            var reason = seenKinds.Add(candidate.Kind) ? Evaluate(input.Record, candidate, haystack) : TileDropReason.SchemaInvalid;
            if (reason is null) accepted.Add(new Tile(IdFor(candidate.Kind), candidate.Kind, candidate.Title, candidate.Text, (candidate.BasedOn ?? []).ToArray(), candidate.Sections?.ToArray()));
            else dropped.Add(new DroppedTile(candidate.Kind, reason));
        }
        return new TileGuardResult(accepted, dropped);
    }

    /// <summary>The reason code for the first failing check, or null when the candidate passes.</summary>
    private string? Evaluate(InterviewRecord record, CandidateTile candidate, Haystack haystack)
    {
        var title = candidate.Title ?? string.Empty;
        var text = candidate.Text ?? string.Empty;

        // A family kind is checked section by section; the two post families have no title, so their title may be empty.
        var lengthReason = TileFamilies.Find(candidate.Kind) is { } family
            ? FamilyLength(family, candidate.Sections ?? [], text)
            : GenericLength(title, text, candidate.Kind);
        if (lengthReason is not null) return lengthReason;

        var combined = $"{title}\n{text}";
        if (AbsoluteBannedTerms().IsMatch(combined)) return TileDropReason.BannedTerm;
        if (UsesExperienceTermWrongly(candidate.Kind, combined, haystack.Interviewee)) return TileDropReason.BannedTerm;

        if ((title.Length > 0 && HasPii(title)) || HasPii(text)) return TileDropReason.PiiFound;

        if (IdentifyingDetail().IsMatch(combined)) return TileDropReason.IdentifyingDetail;

        if (IsUngrounded(record, candidate, combined)) return TileDropReason.Ungrounded;

        var runs = haystack.RunsFor(candidate.Kind);
        var copyLength = TileLimits.QuoteRunWordsForbiddenFor(candidate.Kind);
        if (CopiesQuote(runs, title, copyLength) || CopiesQuote(runs, text, copyLength)) return TileDropReason.CopiedQuote;

        return null;
    }

    /// <summary>
    /// The tiered experience rule. Any family that occurs in a tile outside the narrative family is a ban. In the narrative, the
    /// family must occur in the interviewee's words, and every sentence that uses it must carry a first-person framing ("in my
    /// experience", "moim zdaniem"). Interviewer lines are never evidence.
    /// </summary>
    private static bool UsesExperienceTermWrongly(TileKind kind, string text, string interviewee)
    {
        var allowed = TileFamilies.Find(kind) is { ExperienceTerms: true };
        foreach (var family in ExperienceFamilies)
        {
            if (!family.IsMatch(text)) continue;
            if (!allowed) return true;
            if (!family.IsMatch(interviewee)) return true;
            foreach (var sentence in Sentences(text))
                if (family.IsMatch(sentence) && !FirstPerson().IsMatch(sentence)) return true;
        }
        return false;
    }

    /// <summary>The length rule of a generic kind: a title and a text, each within its limit.</summary>
    private static string? GenericLength(string title, string text, TileKind kind)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text)) return TileDropReason.Empty;
        if (CodePoints(title) > TileLimits.MaxTitleChars || CodePoints(text) > TileLimits.MaxTextCharsFor(kind)) return TileDropReason.TooLong;
        return null;
    }

    /// <summary>
    /// The length rule of a family draft (ADR-0083). A required section must be present and non-empty (<c>empty</c>); a
    /// non-empty section is checked against its own minimum (<c>too_short</c>) and maximum (<c>too_long</c>); a blank optional
    /// section is left out; the sections together must fit the family's whole-draft limit. Keys must be unique and known.
    /// </summary>
    private static string? FamilyLength(FamilySpec family, IReadOnlyList<TileSection> sections, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TileDropReason.Empty;
        if (sections.Select(s => s.Key).Distinct().Count() != sections.Count) return TileDropReason.SchemaInvalid;

        foreach (var rule in family.Sections.Where(r => r.Required))
            if (!sections.Any(s => s.Key == rule.Key && !string.IsNullOrWhiteSpace(s.Text))) return TileDropReason.Empty;

        var total = 0;
        foreach (var section in sections)
        {
            var rule = family.Sections.FirstOrDefault(r => r.Key == section.Key);
            if (rule is null) return TileDropReason.SchemaInvalid;
            if (string.IsNullOrWhiteSpace(section.Text)) continue;

            var length = CodePoints(section.Text);
            if (length > rule.Max) return TileDropReason.TooLong;
            if (length < rule.Min) return TileDropReason.TooShort;
            total += length;
        }
        return total > family.TotalMax ? TileDropReason.TooLong : null;
    }

    private static IEnumerable<string> Sentences(string text) => SentenceEnd().Split(text);

    /// <summary>Fail closed: a detector that fails, or any finding, drops the text. The company token is not personal data.</summary>
    private bool HasPii(string text)
    {
        var checkedText = CompanyTokens.Aggregate(text, (t, token) => t.Replace(token, " ", StringComparison.Ordinal));
        var masked = pii.Mask(checkedText);
        return !masked.Ok || masked.Findings.Count > 0 || pii.HasFindings(checkedText);
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

        // A tile may not name a no_data topic it does not cite: the record says nothing about it. The Facts tile is exempt: the
        // program writes it from every topic, and it says "no data" for those, so naming them is its job.
        if (candidate.Kind != TileKind.Facts)
            foreach (var (topic, entry) in record.Topics.Enumerate())
                if (entry.Status == TopicStatus.NoData && !cited.Contains(topic) && TopicName(topic).IsMatch(text)) return true;

        return false;
    }

    private static bool CopiesQuote(HashSet<string> runs, string text, int length)
    {
        var words = Words(text);
        for (var i = 0; i + length <= words.Length; i++)
            if (runs.Contains(string.Join(' ', words, i, length))) return true;
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
        TileKind.StructuredReview => "structured-review",
        TileKind.Narrative => "narrative",
        TileKind.ProfessionalPost => "professional-post",
        TileKind.ShortPost => "short-post",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// What the copy check compares against: the record's quotes and the interviewee's lines of the masked transcript, as runs
    /// of words of each length a kind may not repeat. Built once per check.
    /// </summary>
    private sealed record Haystack(string Interviewee, HashSet<string> ShortRuns, HashSet<string> NarrativeRuns)
    {
        public HashSet<string> RunsFor(TileKind kind) => kind == TileKind.Narrative ? NarrativeRuns : ShortRuns;

        public static Haystack For(TileInput input)
        {
            var quotes = input.Record.Topics.Enumerate().SelectMany(p => p.Item2.Quotes).ToArray();
            var interviewee = IntervieweeText(input.MaskedTranscript);
            var sources = quotes.Append(interviewee).ToArray();
            // The interviewee's words are the transcript's interviewee lines and the record's quotes, which are their words too.
            return new Haystack(string.Join('\n', quotes.Append(interviewee)), Runs(sources, TileLimits.QuoteRunWordsForbidden), Runs(sources, TileLimits.NarrativeQuoteRunWordsForbidden));
        }

        /// <summary>Every run of <paramref name="length"/> words that occurs in any source.</summary>
        private static HashSet<string> Runs(IEnumerable<string> sources, int length)
        {
            var runs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                var words = Words(source);
                for (var i = 0; i + length <= words.Length; i++)
                    runs.Add(string.Join(' ', words, i, length));
            }
            return runs;
        }

        /// <summary>The interviewee's lines of a masked transcript. A turn that starts with another prefix is not theirs.</summary>
        private static string IntervieweeText(string? transcript)
        {
            if (string.IsNullOrEmpty(transcript)) return string.Empty;
            var turns = new List<string>();
            var inInterviewee = false;
            foreach (var raw in transcript.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith(IntervieweePrefix, StringComparison.Ordinal))
                {
                    inInterviewee = true;
                    turns.Add(line[IntervieweePrefix.Length..]);
                }
                else if (line.StartsWith(InterviewerPrefix, StringComparison.Ordinal)) inInterviewee = false;
                else if (inInterviewee) turns.Add(line);
            }
            return string.Join('\n', turns);
        }
    }

    /// <summary>The line prefixes of <c>Transcript.Render()</c>.</summary>
    private const string IntervieweePrefix = "Interviewee: ";
    private const string InterviewerPrefix = "Interviewer: ";

    /// <summary>
    /// Terms no tile may use, whatever its kind: accusations of crime or illegality stated as fact, health, superlatives and value
    /// words, and motive phrases, in English and Polish, matched case-insensitively on word boundaries.
    /// This is a lexical floor, not a guarantee: a paraphrase that avoids these words passes. The writer prompt and the
    /// fixed notice carry the rest, and the person reviews what they publish.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:" +
        // accusations of crime or illegality, stated as fact
        @"illegal\w*|nielegaln\w*|criminal\w*|fraud\w*|oszustw\w*|corruption\w*|korupcj\w*|theft\w*|kradzie[żz]\w*|przestępstw\w*|bribe\w*|łapówk\w*|" +
        // superlatives and value words
        @"worst|najgorsz\w*|terrible|okropn\w*|horrible|awful|nightmare\w*|koszmar\w*|beznadziejn\w*|" +
        // health
        @"depress\w*|burnout|wypalen\w*|illness\w*|choro\w*|diagnoz\w*|diagnos(?:is|es)|" +
        // motives
        @"on\s+purpose|celowo|deliberately|intentionally" +
        @")\b", Opt)]
    private static partial Regex AbsoluteBannedTerms();

    /// <summary>
    /// A full date (numeric, or a month name with a day, in English or Polish) or a team size. Either could point at a particular
    /// person or group, so the tile is dropped (spec §3.3). A lexical floor, like the banned terms.
    /// </summary>
    [GeneratedRegex(
        @"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b" +
        @"|\b(?:" + Months + @")\s+\d{1,2}\b|\b\d{1,2}\s+(?:" + Months + @")\b" +
        @"|\bteam of \d+\b|\bzesp[oó][łl]\s+\d+\b|\b\d+-osobow\w*", Opt)]
    private static partial Regex IdentifyingDetail();

    private const string Months =
        "january|february|march|april|may|june|july|august|september|october|november|december|" +
        "stycznia|lutego|marca|kwietnia|maja|czerwca|lipca|sierpnia|wrze[śs]nia|pa[źz]dziernika|listopada|grudnia";

    /// <summary>A first-person framing or hedge: the sentence reports the author's own experience, not a fact about others.</summary>
    [GeneratedRegex(
        @"moim zdaniem|według mnie|w mojej ocenie|z mojej perspektywy|" +
        @"odczuwa[łl](?:em|am)|czu[łl](?:em|am)|do[śs]wiadczy[łl](?:em|am)|" +
        @"\bin my experience\b|\bI felt\b|\bI experienced\b|\bfrom my perspective\b|\bin my view\b", Opt)]
    private static partial Regex FirstPerson();

    [GeneratedRegex(@"(?<=[.!?…])\s+|[\r\n]+")]
    private static partial Regex SentenceEnd();

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

    [GeneratedRegex(@"\b(?:pay\w*|salar\w*|promis\w*|wynagrodzeni\w*|p[łl]ac(?!e)\w*|obietnic\w*)", Opt)]
    private static partial Regex PayName();

    [GeneratedRegex(@"\b(?:culture|kultur\w*)", Opt)]
    private static partial Regex CultureName();

    [GeneratedRegex(@"\b(?:leav\w*|odej[śs]\w*)", Opt)]
    private static partial Regex LeavingName();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separator();
}
