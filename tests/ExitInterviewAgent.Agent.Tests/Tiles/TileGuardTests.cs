using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Privacy;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

public class TileGuardTests
{
    private static readonly TileGuard Guard = new(new PiiGuard(["Widgetron"]));

    // Covered: onboarding, growth, culture. NoData: management, pay_vs_promises, reason_for_leaving.
    private static readonly InterviewRecord Record = new(
        InterviewId.Parse(new string('c', 32)),
        "widgetron-ltd",
        new RecordContext(TenureBand.OneToThreeYears, SeniorityBand.Mid, FunctionBand.Engineering),
        new TopicSet(
            Onboarding: TopicEntry.Covered(4, Confidence.High, ["The first weeks were chaotic and nobody had a plan"]),
            Management: TopicEntry.NoData,
            Growth: TopicEntry.Covered(2, Confidence.Medium, ["I never got a real path to grow in the company"]),
            PayVsPromises: TopicEntry.NoData,
            Culture: TopicEntry.Covered(3, Confidence.Low, ["People helped each other when deadlines slipped"]),
            ReasonForLeaving: TopicEntry.NoData),
        PiiMasked: true,
        Interview: new InterviewMetadata("1.0", "en", true, DurationBand.TenToTwentyMinutes, TurnBand.TenToTwenty));

    private const string CleanText = "The first weeks were hard because nobody had a plan for them.";

    private static CandidateTile Cand(TileKind kind, string text, string title = "Notes", string[]? basedOn = null) =>
        new(kind, title, text, basedOn ?? ["onboarding"]);

    private static string? Reason(CandidateTile candidate)
    {
        var result = Guard.Check(Record, [candidate]);
        return result.Dropped.Count == 0 ? null : result.Dropped.Single().ReasonCode;
    }

    private static Tile Accepted(CandidateTile candidate)
    {
        var result = Guard.Check(Record, [candidate]);
        Assert.Empty(result.Dropped);
        return result.Accepted.Single();
    }

    private sealed class ScriptedPii(bool ok, bool findings, bool hasFindings) : IPiiGuard
    {
        public PiiGuardResult Mask(string text) => new(ok, text, findings ? [new PiiFinding(PiiKind.Email, 0, 1, PiiBasis.Pattern)] : []);

        public bool HasFindings(string maskedText) => hasFindings;
    }

    [Fact]
    public void A_clean_tile_passes_unchanged_with_a_deterministic_id()
    {
        var tile = Accepted(Cand(TileKind.Overview, CleanText, title: "Onboarding", basedOn: ["onboarding"]));

        Assert.Equal("overview", tile.Id);
        Assert.Equal(TileKind.Overview, tile.Kind);
        Assert.Equal("Onboarding", tile.Title);
        Assert.Equal(CleanText, tile.Text);
        Assert.Equal(["onboarding"], tile.BasedOn);
    }

    [Fact]
    public void The_guard_never_edits_text_even_when_it_has_surrounding_spaces()
    {
        var text = "  The first weeks were hard.  ";

        Assert.Equal(text, Accepted(Cand(TileKind.Overview, text)).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    public void Empty_or_whitespace_text_is_dropped_as_empty(string text) =>
        Assert.Equal(TileDropReason.Empty, Reason(Cand(TileKind.Overview, text)));

    [Fact]
    public void An_empty_title_is_dropped_as_empty() =>
        Assert.Equal(TileDropReason.Empty, Reason(Cand(TileKind.Overview, CleanText, title: " ")));

    [Fact]
    public void A_title_over_the_limit_is_dropped_as_too_long()
    {
        var title = new string('a', TileLimits.MaxTitleChars + 1);

        Assert.Equal(TileDropReason.TooLong, Reason(Cand(TileKind.Overview, CleanText, title: title)));
    }

    [Fact]
    public void A_text_over_the_limit_is_dropped_as_too_long()
    {
        var text = new string('a', TileLimits.MaxTextChars + 1);

        Assert.Equal(TileDropReason.TooLong, Reason(Cand(TileKind.Overview, text)));
    }

    [Fact]
    public void A_short_note_is_limited_to_280_characters_and_the_boundary_is_exact()
    {
        var atLimit = new string('a', TileLimits.ShortNoteMaxChars);
        var overLimit = new string('a', TileLimits.ShortNoteMaxChars + 1);

        Assert.Equal(atLimit, Accepted(Cand(TileKind.ShortNote, atLimit)).Text);
        Assert.Equal(TileDropReason.TooLong, Reason(Cand(TileKind.ShortNote, overLimit)));
    }

    [Fact]
    public void Length_is_counted_in_code_points_not_utf16_units()
    {
        // Each emoji is two UTF-16 units but one code point: 60 of them fit the title limit, 61 do not.
        var fits = string.Concat(Enumerable.Repeat("😀", TileLimits.MaxTitleChars));
        var tooLong = string.Concat(Enumerable.Repeat("😀", TileLimits.MaxTitleChars + 1));

        Assert.Equal(fits, Accepted(Cand(TileKind.Overview, CleanText, title: fits)).Title);
        Assert.Equal(TileDropReason.TooLong, Reason(Cand(TileKind.Overview, CleanText, title: tooLong)));
    }

    [Theory]
    [InlineData("This was illegal and everyone knew it.")]
    [InlineData("The management committed fraud in the review.")]
    [InlineData("I was harassed on a daily basis.")]
    [InlineData("They discriminated against people who were new.")]
    [InlineData("The culture was the worst I have seen.")]
    [InlineData("The pay process was terrible for everyone.")]
    [InlineData("The team was toxic from the first day.")]
    [InlineData("The last quarter was a nightmare.")]
    [InlineData("I got burnout after the first year.")]
    [InlineData("I was depressed for most of the year.")]
    [InlineData("The reviews were unfair and he did it on purpose.")]
    [InlineData("The bonus was intentionally delayed.")]
    public void English_banned_terms_are_dropped_as_banned_term(string text) =>
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Overview, text)));

    [Theory]
    [InlineData("To było nielegalne i wszyscy o tym wiedzieli.")]
    [InlineData("Doszło do oszustwa przy rozliczeniu premii.")]
    [InlineData("Doszło do molestowania w zespole.")]
    [InlineData("To była dyskryminacja nowych osób.")]
    [InlineData("Był to mobbing na co dzień.")]
    [InlineData("Ktoś próbował dać łapówkę kierownikowi.")]
    [InlineData("Kultura była najgorsza, jaką widziałem.")]
    [InlineData("Ostatni kwartał był koszmarem.")]
    [InlineData("Miałem wypalenie zawodowe po roku.")]
    [InlineData("Choroba wpłynęła na moją pracę.")]
    [InlineData("On celowo chciał mi utrudnić pracę.")]
    public void Polish_banned_terms_are_dropped_as_banned_term(string text) =>
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Overview, text)));

    [Fact]
    public void A_banned_term_in_the_title_is_dropped_too()
    {
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Overview, CleanText, title: "Worst onboarding")));
    }

    [Fact]
    public void Banned_terms_match_whole_words_only() =>
        // "worsted" starts with "worst" but is a different word: the word boundary keeps it from matching.
        Assert.Null(Reason(Cand(TileKind.Overview, "The yarn was worsted weight and the lilliput set was fine.")));

    [Fact]
    public void Failed_pii_detection_fails_closed_and_drops_a_clean_tile()
    {
        var guard = new TileGuard(new BrokenPiiGuard());

        var result = guard.Check(Record, [Cand(TileKind.Overview, CleanText)]);

        Assert.Empty(result.Accepted);
        Assert.Equal(TileDropReason.PiiFound, result.Dropped.Single().ReasonCode);
    }

    [Fact]
    public void A_pii_guard_that_reports_findings_drops_the_tile()
    {
        var guard = new TileGuard(new ScriptedPii(ok: true, findings: true, hasFindings: false));

        Assert.Equal(TileDropReason.PiiFound, guard.Check(Record, [Cand(TileKind.Overview, CleanText)]).Dropped.Single().ReasonCode);
    }

    [Fact]
    public void A_pii_guard_that_finds_something_in_the_text_drops_the_tile()
    {
        var guard = new TileGuard(new ScriptedPii(ok: true, findings: false, hasFindings: true));

        Assert.Equal(TileDropReason.PiiFound, guard.Check(Record, [Cand(TileKind.Overview, CleanText)]).Dropped.Single().ReasonCode);
    }

    [Fact]
    public void A_pii_guard_with_no_findings_lets_the_tile_through()
    {
        var guard = new TileGuard(new ScriptedPii(ok: true, findings: false, hasFindings: false));

        Assert.Single(guard.Check(Record, [Cand(TileKind.Overview, CleanText)]).Accepted);
    }

    [Fact]
    public void Personal_data_in_the_real_detector_drops_the_text()
    {
        Assert.Equal(TileDropReason.PiiFound, Reason(Cand(TileKind.Overview, "Reach me at anna.kowalska@example.com about this.")));
    }

    [Fact]
    public void Personal_data_in_the_title_drops_the_tile()
    {
        Assert.Equal(TileDropReason.PiiFound, Reason(Cand(TileKind.Overview, CleanText, title: "anna.kowalska@example.com")));
    }

    [Fact]
    public void A_tile_with_no_basis_is_dropped_as_ungrounded() =>
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Overview, CleanText, basedOn: [])));

    [Fact]
    public void A_facts_tile_may_have_an_empty_basis()
    {
        Assert.Equal("facts", Accepted(Cand(TileKind.Facts, CleanText, basedOn: [])).Id);
    }

    [Fact]
    public void A_facts_tile_citing_only_covered_topics_passes()
    {
        Assert.Single(Guard.Check(Record, [Cand(TileKind.Facts, CleanText, basedOn: ["onboarding", "culture"])]).Accepted);
    }

    [Fact]
    public void A_facts_tile_citing_a_no_data_topic_is_dropped_as_ungrounded() =>
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Facts, CleanText, basedOn: ["management"])));

    [Fact]
    public void An_unknown_topic_key_is_dropped_as_ungrounded() =>
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Overview, CleanText, basedOn: ["salary"])));

    [Fact]
    public void Citing_a_no_data_topic_is_dropped_as_ungrounded() =>
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Overview, CleanText, basedOn: ["onboarding", "management"])));

    [Fact]
    public void More_than_the_maximum_number_of_basis_entries_is_dropped_as_ungrounded()
    {
        var seven = Enumerable.Repeat("onboarding", TileLimits.MaxBasedOn + 1).ToArray();

        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Overview, CleanText, basedOn: seven)));
    }

    [Theory]
    [InlineData("The management was fine for the team.")]
    [InlineData("Management was not discussed here.")]
    [InlineData("Our manager helped with planning.")]
    [InlineData("Mój przełożony był wspierający.")]
    [InlineData("Kierownik nie miał czasu.")]
    [InlineData("Pay was higher than promised.")]
    [InlineData("Wynagrodzenie było niskie.")]
    [InlineData("Płaca nie wzrosła.")]
    [InlineData("Obietnice nie zostały spełnione.")]
    [InlineData("I decided to leave after a year.")]
    [InlineData("Powód odejścia był prosty.")]
    public void Naming_a_no_data_topic_in_the_text_is_dropped_as_ungrounded(string text) =>
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Overview, text, basedOn: ["onboarding"])));

    [Fact]
    public void Naming_a_no_data_topic_is_ungrounded_even_when_the_title_names_it()
    {
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(TileKind.Overview, CleanText, title: "Management", basedOn: ["onboarding"])));
    }

    [Fact]
    public void Naming_a_covered_topic_that_is_cited_is_accepted()
    {
        const string text = "The onboarding was chaotic in the first weeks.";

        Assert.Equal(text, Accepted(Cand(TileKind.Overview, text, basedOn: ["onboarding"])).Text);
    }

    [Fact]
    public void A_text_that_copies_seven_consecutive_words_from_a_quote_is_dropped()
    {
        // The quote is "I never got a real path to grow in the company": "never got a real path to grow" is seven words.
        Assert.Equal(TileDropReason.CopiedQuote, Reason(Cand(TileKind.Overview, "Honestly, never got a real path to grow there.", basedOn: ["growth"])));
    }

    [Fact]
    public void Six_consecutive_words_from_a_quote_are_allowed()
    {
        // "real path to grow in the" is six words of the quote and the next word differs from "company".
        var text = "We talked about real path to grow in the team every month.";

        Assert.Equal(text, Accepted(Cand(TileKind.Overview, text, basedOn: ["growth"])).Text);
    }

    [Fact]
    public void Quote_matching_ignores_case_and_punctuation()
    {
        Assert.Equal(TileDropReason.CopiedQuote, Reason(Cand(TileKind.Overview, "NEVER got, a real path... to GROW!!", basedOn: ["growth"])));
    }

    [Fact]
    public void A_title_that_copies_seven_consecutive_words_from_a_quote_is_dropped()
    {
        Assert.Equal(TileDropReason.CopiedQuote, Reason(Cand(TileKind.Overview, CleanText, title: "never got a real path to grow", basedOn: ["growth"])));
    }

    [Fact]
    public void The_first_failing_check_decides_empty_before_banned_term()
    {
        Assert.Equal(TileDropReason.Empty, Reason(Cand(TileKind.Overview, "", title: "worst")));
    }

    [Fact]
    public void Too_long_is_checked_before_banned_term()
    {
        var text = "worst " + new string('a', TileLimits.MaxTextChars);

        Assert.Equal(TileDropReason.TooLong, Reason(Cand(TileKind.Overview, text)));
    }

    [Fact]
    public void Banned_term_is_checked_before_pii()
    {
        var guard = new TileGuard(new BrokenPiiGuard());

        Assert.Equal(TileDropReason.BannedTerm, guard.Check(Record, [Cand(TileKind.Overview, "This was the worst.")]).Dropped.Single().ReasonCode);
    }

    [Fact]
    public void Pii_is_checked_before_grounding()
    {
        var guard = new TileGuard(new BrokenPiiGuard());

        Assert.Equal(TileDropReason.PiiFound, guard.Check(Record, [Cand(TileKind.Overview, CleanText, basedOn: ["management"])]).Dropped.Single().ReasonCode);
    }

    [Fact]
    public void Grounding_is_checked_before_copied_quote()
    {
        Assert.Equal(
            TileDropReason.Ungrounded,
            Reason(Cand(TileKind.Overview, "never got a real path to grow in the company", basedOn: ["management"])));
    }

    [Fact]
    public void A_second_candidate_of_the_same_kind_is_dropped_as_schema_invalid_and_the_first_wins()
    {
        var result = Guard.Check(Record, [
            Cand(TileKind.Overview, CleanText),
            Cand(TileKind.Overview, "Another clean sentence about the first weeks at work."),
        ]);

        Assert.Equal("overview", result.Accepted.Single().Id);
        Assert.Equal(CleanText, result.Accepted.Single().Text);
        Assert.Equal(TileDropReason.SchemaInvalid, result.Dropped.Single().ReasonCode);
        Assert.Equal(TileKind.Overview, result.Dropped.Single().Kind);
    }

    [Fact]
    public void Accepted_and_dropped_tiles_keep_the_order_of_the_candidates()
    {
        var result = Guard.Check(Record, [
            Cand(TileKind.Overview, CleanText),
            Cand(TileKind.WhatWorked, "This was the worst thing."),
            Cand(TileKind.ShortNote, CleanText),
        ]);

        Assert.Equal([TileKind.Overview, TileKind.ShortNote], result.Accepted.Select(t => t.Kind));
        Assert.Equal([TileKind.WhatWorked], result.Dropped.Select(d => d.Kind));
    }

    [Fact]
    public void The_id_is_kebab_case_per_kind()
    {
        Assert.Equal("what-could-improve", Accepted(Cand(TileKind.WhatCouldImprove, CleanText)).Id);
        Assert.Equal("for-the-next-person", Accepted(Cand(TileKind.ForTheNextPerson, CleanText)).Id);
        Assert.Equal("short-note", Accepted(Cand(TileKind.ShortNote, CleanText)).Id);
    }
}
