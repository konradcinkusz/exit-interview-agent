using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

/// <summary>Platform tiles (ADR-0075): per-kind limits, tiered banned terms, the transcript as input, quote copying in the transcript.</summary>
public class PlatformTileGuardTests
{
    private static readonly TileGuard Guard = new(new PiiGuard(["Widgetron"]));

    // Covered: onboarding, growth, culture. NoData: management, pay_vs_promises, reason_for_leaving.
    private static readonly InterviewRecord Record = RecordWithCulture("People helped each other when deadlines slipped");

    private const string Neutral = "The first weeks were hard because nobody had a plan for them.";

    private const string Transcript =
        "Interviewer: What was the first month like?\n" +
        "Interviewee: The weekly planning meeting never once started on time for anyone on our small team at all.\n" +
        "Interviewer: What did you do about it?\n" +
        "Interviewee: I was bullied by my lead in the second month.\n";

    private static InterviewRecord RecordWithCulture(string culture) => new(
        InterviewId.Parse(new string('c', 32)),
        "widgetron-ltd",
        new RecordContext(TenureBand.OneToThreeYears, SeniorityBand.Mid, FunctionBand.Engineering),
        new TopicSet(
            Onboarding: TopicEntry.Covered(4, Confidence.High, ["The first weeks were chaotic and nobody had a plan"]),
            Management: TopicEntry.NoData,
            Growth: TopicEntry.Covered(2, Confidence.Medium, ["I never got a real path to grow in the company"]),
            PayVsPromises: TopicEntry.NoData,
            Culture: TopicEntry.Covered(3, Confidence.Low, [culture]),
            ReasonForLeaving: TopicEntry.NoData),
        PiiMasked: true,
        Interview: new InterviewMetadata("1.0", "en", true, DurationBand.TenToTwentyMinutes, TurnBand.TenToTwenty));

    private static CandidateTile Cand(TileKind kind, string text, string title = "Notes", string[]? basedOn = null) =>
        new(kind, title, text, basedOn ?? ["onboarding"]);

    private static string? Reason(CandidateTile candidate, string? transcript = null, InterviewRecord? record = null)
    {
        var result = Guard.Check(new TileInput(record ?? Record, transcript), [candidate]);
        return result.Dropped.Count == 0 ? null : result.Dropped.Single().ReasonCode;
    }

    private static bool Accepted(CandidateTile candidate, string? transcript = null, InterviewRecord? record = null) =>
        Reason(candidate, transcript, record) is null;

    private static readonly TileKind[] AllModelKinds = Enum.GetValues<TileKind>().Where(k => k != TileKind.Facts).ToArray();

    [Fact]
    public void Each_platform_kind_gets_its_own_length_limit_as_a_named_design_constant()
    {
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.Facts));
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.Overview));
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.WhatWorked));
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.WhatCouldImprove));
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.ForTheNextPerson));
        Assert.Equal(280, TileLimits.MaxTextCharsFor(TileKind.ShortNote));
        Assert.Equal(900, TileLimits.MaxTextCharsFor(TileKind.Glassdoor));
        Assert.Equal(500, TileLimits.MaxTextCharsFor(TileKind.GoogleReview));
        Assert.Equal(4500, TileLimits.MaxTextCharsFor(TileKind.Reddit));
    }

    [Theory]
    [InlineData(TileKind.Glassdoor, 900)]
    [InlineData(TileKind.GoogleReview, 500)]
    [InlineData(TileKind.Reddit, 4500)]
    [InlineData(TileKind.Overview, 600)]
    [InlineData(TileKind.ShortNote, 280)]
    public void Text_at_the_kind_limit_passes_and_one_character_over_is_too_long(TileKind kind, int limit)
    {
        var atLimit = new string('a', limit);
        var overLimit = new string('a', limit + 1);

        Assert.Null(Reason(Cand(kind, atLimit)));
        Assert.Equal(TileDropReason.TooLong, Reason(Cand(kind, overLimit)));
    }

    [Fact]
    public void The_platform_tiles_have_their_own_ids()
    {
        var result = Guard.Check(new TileInput(Record), [Cand(TileKind.Glassdoor, Neutral), Cand(TileKind.GoogleReview, Neutral), Cand(TileKind.Reddit, Neutral)]);

        Assert.Equal(["glassdoor", "google-review", "reddit"], result.Accepted.Select(t => t.Id));
    }

    [Theory]
    [InlineData("This was illegal and everyone knew it.")]
    [InlineData("To było nielegalne i wszyscy o tym wiedzieli.")]
    [InlineData("The management committed fraud in the review.")]
    [InlineData("Doszło do oszustwa przy rozliczeniu premii.")]
    [InlineData("There was corruption in the hiring process.")]
    [InlineData("Była korupcja przy przetargach.")]
    [InlineData("Someone reported a theft of equipment.")]
    [InlineData("Doszło do kradzieży sprzętu.")]
    [InlineData("To był przestępstwo w biurze.")]
    [InlineData("Ktoś próbował dać łapówkę kierownikowi.")]
    [InlineData("I was depressed for most of the year.")]
    [InlineData("I got burnout after the first year.")]
    [InlineData("Miałem wypalenie zawodowe po roku.")]
    [InlineData("The doctor gave me a diagnosis of a chronic illness.")]
    [InlineData("Postawiono mi diagnozę po roku.")]
    [InlineData("Choroba wpłynęła na moją pracę.")]
    public void Absolute_terms_are_banned_in_every_kind_even_with_the_transcript_and_a_hedge(string text)
    {
        // The transcript and the first-person hedge are present on purpose: the absolute list must not depend on them.
        foreach (var kind in AllModelKinds)
        {
            var reason = Reason(Cand(kind, text), Transcript);
            Assert.True(reason == TileDropReason.BannedTerm, $"{kind}: {reason}");
        }
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Reddit, $"In my experience, {text}"), Transcript));
    }

    [Theory]
    [InlineData("I was bullied by my lead.")]
    [InlineData("Był to mobbing na co dzień.")]
    [InlineData("I was harassed on a daily basis.")]
    [InlineData("Doszło do molestowania w zespole.")]
    [InlineData("They discriminated against people who were new.")]
    [InlineData("To była dyskryminacja nowych osób.")]
    [InlineData("The team was toxic from the first day.")]
    [InlineData("Atmosfera była toksyczna od pierwszego dnia.")]
    [InlineData("Byłem prześladowany przez przełożonego.")]
    public void Experience_terms_are_banned_in_every_short_kind(string text)
    {
        foreach (var kind in AllModelKinds.Where(k => k != TileKind.Reddit))
        {
            var reason = Reason(Cand(kind, $"In my experience. {text}"), Transcript);
            Assert.True(reason == TileDropReason.BannedTerm, $"{kind}: {reason}");
        }
    }

    [Fact]
    public void Reddit_may_use_an_experience_term_when_the_interviewee_used_it_and_the_sentence_is_framed_as_their_own()
    {
        Assert.True(Accepted(Cand(TileKind.Reddit, "In my experience, I was bullied by my lead in the second month.", basedOn: ["onboarding"]), Transcript));
    }

    [Theory]
    [InlineData("Moim zdaniem to był mobbing w zespole.", "Interviewee: Odczuwałem mobbing w zespole od pierwszego dnia.\n")]
    [InlineData("Czułem się prześladowany przez kolegę.", "Interviewee: Czułem się prześladowany przez kolegę.\n")]
    [InlineData("Z mojej perspektywy zespół był toksyczny.", "Interviewee: Zespół był toksyczny.\n")]
    public void Reddit_accepts_the_polish_first_person_framings(string text, string line) =>
        Assert.True(Accepted(Cand(TileKind.Reddit, text), line));

    [Fact]
    public void Reddit_drops_an_experience_term_without_a_first_person_framing()
    {
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Reddit, "The lead bullied me in the second month.", basedOn: ["onboarding"]), Transcript));
    }

    [Fact]
    public void Reddit_drops_an_experience_term_the_interviewee_never_used_even_with_a_hedge()
    {
        const string transcriptWithoutTheWord = "Interviewer: Anything else?\nInterviewee: The lunch was fine most days.\n";

        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Reddit, "In my experience, I was bullied by my lead."), transcriptWithoutTheWord));
    }

    [Fact]
    public void Reddit_drops_an_experience_term_when_no_transcript_is_given_and_the_record_does_not_quote_it()
    {
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Reddit, "In my experience, I was bullied by my lead.")));
    }

    [Fact]
    public void Reddit_accepts_an_experience_term_the_record_quotes_when_it_is_framed_as_the_interviewees_own()
    {
        var record = RecordWithCulture("I was harassed by a colleague in the second week");

        Assert.True(Accepted(Cand(TileKind.Reddit, "In my experience, I was harassed by a colleague in the second week."), record: record));
    }

    [Fact]
    public void A_hedge_in_one_sentence_does_not_cover_the_term_in_another()
    {
        var text = "In my experience the team was fine. The lead bullied me in the second month.";

        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(TileKind.Reddit, text), Transcript));
    }

    [Theory]
    [InlineData(TileKind.Glassdoor)]
    [InlineData(TileKind.GoogleReview)]
    public void Short_public_kinds_never_use_an_experience_term_even_with_a_hedge_and_the_transcript(TileKind kind) =>
        Assert.Equal(TileDropReason.BannedTerm, Reason(Cand(kind, "In my experience, I was bullied by my lead.", basedOn: ["onboarding"]), Transcript));

    [Fact]
    public void A_platform_tile_must_cite_at_least_one_covered_topic()
    {
        foreach (var kind in new[] { TileKind.Glassdoor, TileKind.GoogleReview, TileKind.Reddit })
            Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(kind, Neutral, basedOn: [])));
    }

    [Theory]
    [InlineData(TileKind.Glassdoor)]
    [InlineData(TileKind.GoogleReview)]
    [InlineData(TileKind.Reddit)]
    public void A_platform_tile_may_not_cite_a_no_data_topic(TileKind kind) =>
        Assert.Equal(TileDropReason.Ungrounded, Reason(Cand(kind, Neutral, basedOn: ["management"])));

    [Fact]
    public void Reddit_may_copy_up_to_twelve_consecutive_words_of_the_interviewee_and_not_thirteen()
    {
        const string twelve = "Recalling that the weekly planning meeting never once started on time for anyone on";
        const string thirteen = "Recalling that the weekly planning meeting never once started on time for anyone on our";

        Assert.True(Accepted(Cand(TileKind.Reddit, twelve), Transcript));
        Assert.Equal(TileDropReason.CopiedQuote, Reason(Cand(TileKind.Reddit, thirteen), Transcript));
    }

    [Fact]
    public void Other_kinds_may_not_copy_seven_consecutive_words_of_the_interviewee()
    {
        Assert.Equal(TileDropReason.CopiedQuote, Reason(Cand(TileKind.Overview, "the weekly planning meeting never once started"), Transcript));
        Assert.True(Accepted(Cand(TileKind.Overview, "the weekly planning meeting never once"), Transcript));
    }

    [Fact]
    public void Only_the_interviewees_lines_count_as_copied_material()
    {
        const string interviewerOnly = "Interviewer: the weekly planning meeting never once started on time for anyone\nInterviewee: Yes.\n";

        Assert.True(Accepted(Cand(TileKind.Overview, "the weekly planning meeting never once started"), interviewerOnly));
    }

    [Fact]
    public void A_platform_kind_may_not_copy_seven_words_or_more_from_the_record_quotes()
    {
        // The record's own quote has 10 words: copying all of it is refused by the 7-word rule of the short kinds.
        Assert.Equal(TileDropReason.CopiedQuote, Reason(Cand(TileKind.Glassdoor, "The first weeks were chaotic and nobody had a plan")));
    }

    [Theory]
    [InlineData("[COMPANY] was the first place I worked where the first weeks were hard.")]
    [InlineData("[FIRMA] była pierwszym miejscem, gdzie pierwsze tygodnie były trudne.")]
    public void The_company_token_passes_the_pii_guard_in_every_kind(string text)
    {
        foreach (var kind in AllModelKinds)
            Assert.True(Accepted(Cand(kind, text)), kind.ToString());
    }

    [Fact]
    public void A_company_token_does_not_hide_real_personal_data_in_the_same_text()
    {
        Assert.Equal(TileDropReason.PiiFound, Reason(Cand(TileKind.Glassdoor, "[COMPANY] was hard. Reach me at anna.kowalska@example.com.")));
    }

    [Fact]
    public void Check_with_a_bare_record_behaves_like_check_with_a_tile_input_without_transcript()
    {
        var candidates = new[] { Cand(TileKind.Reddit, "In my experience, I was bullied by my lead.") };

        var bare = Guard.Check(Record, candidates);
        var input = Guard.Check(new TileInput(Record), candidates);

        Assert.Equal(bare.Dropped.Select(d => d.ReasonCode), input.Dropped.Select(d => d.ReasonCode));
    }
}
