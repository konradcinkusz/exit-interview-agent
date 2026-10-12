using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

/// <summary>
/// The guard by family (ADR-0083, spec §3.3 and §4): per-section limits and <c>too_short</c>, the experience terms only in the
/// narrative, the copied-run limit per family, <c>identifying_detail</c>, the company token, and the family table itself.
/// </summary>
public class FamilyTileGuardTests
{
    private static readonly TileGuard Guard = new(new PiiGuard(["Widgetron"]));

    // Covered: onboarding, growth, culture. NoData: management, pay_vs_promises, reason_for_leaving.
    private static readonly InterviewRecord Record = RecordWithCulture("People helped each other when deadlines slipped");

    private const string Transcript =
        "Interviewer: What was the first month like?\n" +
        "Interviewee: The weekly planning meeting never once started on time for anyone on our small team at all.\n" +
        "Interviewer: What did you do about it?\n" +
        "Interviewee: I was bullied by my lead in the second month.\n";

    /// <summary>Neutral words that name no topic, no person and no experience term.</summary>
    private const string Filler = "Written in calm words from my own answers.";

    private static readonly TileKind[] Families = [TileKind.StructuredReview, TileKind.Narrative, TileKind.ProfessionalPost, TileKind.ShortPost];

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

    /// <summary>A family candidate. Its text is the sections joined by a blank line, as the writer's output parser builds it.</summary>
    private static CandidateTile Family(TileKind kind, string[] basedOn, params (string Key, string Text)[] sections) =>
        new(
            kind,
            sections.FirstOrDefault(s => s.Key == "title").Text ?? string.Empty,
            string.Join("\n\n", sections.Select(s => s.Text)),
            basedOn,
            sections.Select(s => new TileSection(s.Key, s.Text)).ToArray());

    /// <summary>The text padded with <see cref="Filler"/> sentences until it reaches <paramref name="min"/> code points.</summary>
    private static string Fill(string core, int min)
    {
        // A filler sentence starts after a full stop: a capitalised word mid-sentence reads as a name to the PII detector.
        var text = core;
        while (text.Length < min) text += (text.EndsWith('.') ? " " : ". ") + Filler;
        return text;
    }

    /// <summary>A family candidate whose one text-bearing section carries <paramref name="text"/>, padded to that family's minimum.</summary>
    private static CandidateTile WithBody(TileKind kind, string text) => kind switch
    {
        TileKind.StructuredReview => Family(kind, ["onboarding"], ("title", "Notes"), ("pros", Fill(text, 40)), ("cons", Fill("The first weeks were slow.", 40))),
        TileKind.Narrative => Family(kind, ["onboarding"], ("title", "My time"), ("body", Fill(text, 600))),
        TileKind.ProfessionalPost => Family(kind, ["onboarding"], ("body", Fill(text, 300))),
        TileKind.ShortPost => Family(kind, ["onboarding"], ("body", Fill(text, 80))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string? Reason(CandidateTile candidate, string? transcript = null, InterviewRecord? record = null)
    {
        var result = Guard.Check(new TileInput(record ?? Record, transcript), [candidate]);
        return result.Dropped.Count == 0 ? null : result.Dropped.Single().ReasonCode;
    }

    private static bool Accepted(CandidateTile candidate, string? transcript = null, InterviewRecord? record = null) =>
        Reason(candidate, transcript, record) is null;

    // ---- Per-section limits (spec §3.3 table) -------------------------------------------------------------------------------

    [Theory]
    [InlineData(39, TileDropReason.TooShort)]
    [InlineData(40, null)]
    [InlineData(500, null)]
    [InlineData(501, TileDropReason.TooLong)]
    public void A_structured_review_pro_is_40_to_500_characters(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"), ("pros", new string('a', length)), ("cons", Fill("Slow start.", 40)))));

    [Theory]
    [InlineData(39, TileDropReason.TooShort)]
    [InlineData(40, null)]
    public void A_structured_review_con_is_at_least_40_characters(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"), ("pros", Fill("Good team.", 40)), ("cons", new string('b', length)))));

    [Fact]
    public void An_advice_section_is_optional_but_when_present_must_be_40_to_500()
    {
        Assert.True(Accepted(Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"), ("pros", Fill("Good team.", 40)), ("cons", Fill("Slow start.", 40)))));
        Assert.Equal(TileDropReason.TooShort, Reason(Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"), ("pros", Fill("Good team.", 40)), ("cons", Fill("Slow start.", 40)), ("advice", "Too short."))));
    }

    [Fact]
    public void A_blank_optional_advice_section_is_left_out_and_does_not_drop_the_draft()
    {
        var candidate = Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"), ("pros", Fill("Good team.", 40)), ("cons", Fill("Slow start.", 40)), ("advice", "   "));

        Assert.True(Accepted(candidate));
    }

    [Theory]
    [InlineData(60, null)]
    [InlineData(61, TileDropReason.TooLong)]
    public void A_structured_review_title_is_at_most_60_characters(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.StructuredReview, ["onboarding"], ("title", new string('t', length)), ("pros", Fill("Good team.", 40)), ("cons", Fill("Slow start.", 40)))));

    [Fact]
    public void A_structured_review_whose_sections_together_exceed_1100_is_too_long()
    {
        var candidate = Family(TileKind.StructuredReview, ["onboarding"],
            ("title", new string('t', 60)), ("pros", new string('p', 500)), ("cons", new string('c', 500)), ("advice", new string('a', 500)));

        Assert.Equal(TileDropReason.TooLong, Reason(candidate));
    }

    [Theory]
    [InlineData(599, TileDropReason.TooShort)]
    [InlineData(600, null)]
    [InlineData(4000, null)]
    [InlineData(4001, TileDropReason.TooLong)]
    public void A_narrative_body_is_600_to_4000_characters(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.Narrative, ["onboarding"], ("title", "My time"), ("body", new string('a', length)))));

    [Theory]
    [InlineData(120, null)]
    [InlineData(121, TileDropReason.TooLong)]
    public void A_narrative_title_is_at_most_120_characters(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.Narrative, ["onboarding"], ("title", new string('t', length)), ("body", new string('a', 600)))));

    [Theory]
    [InlineData(299, TileDropReason.TooShort)]
    [InlineData(300, null)]
    [InlineData(1300, null)]
    [InlineData(1301, TileDropReason.TooLong)]
    public void A_professional_post_body_is_300_to_1300_characters(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.ProfessionalPost, ["onboarding"], ("body", new string('a', length)))));

    [Theory]
    [InlineData(79, TileDropReason.TooShort)]
    [InlineData(80, null)]
    [InlineData(270, null)]
    [InlineData(271, TileDropReason.TooLong)]
    public void A_short_post_body_is_80_to_270_characters_and_the_boundary_is_exact(int length, string? expected) =>
        Assert.Equal(expected, Reason(Family(TileKind.ShortPost, ["onboarding"], ("body", new string('a', length)))));

    [Fact]
    public void A_post_family_has_no_title_and_is_accepted_with_an_empty_title()
    {
        var candidate = WithBody(TileKind.ShortPost, "The first weeks were hard.");

        var tile = Assert.Single(Guard.Check(new TileInput(Record), [candidate]).Accepted);

        Assert.Equal(string.Empty, tile.Title);
        Assert.Equal(candidate.Text, tile.Text);
    }

    [Fact]
    public void A_family_tile_keeps_its_sections_and_its_text_exactly_as_the_writer_gave_them()
    {
        var candidate = WithBody(TileKind.Narrative, "The first weeks were hard because nobody had a plan.");

        var tile = Assert.Single(Guard.Check(new TileInput(Record), [candidate]).Accepted);

        Assert.Equal(candidate.Text, tile.Text);
        Assert.Equal(candidate.Sections!, tile.Sections!);
    }

    [Theory]
    [InlineData(TileKind.StructuredReview, "Notes", "")]
    [InlineData(TileKind.Narrative, "My time", "")]
    public void A_required_section_that_is_empty_drops_the_draft_as_empty(TileKind kind, string title, string body)
    {
        var candidate = kind == TileKind.StructuredReview
            ? Family(kind, ["onboarding"], ("title", title), ("pros", body), ("cons", Fill("Slow start.", 40)))
            : Family(kind, ["onboarding"], ("title", title), ("body", body));

        Assert.Equal(TileDropReason.Empty, Reason(candidate));
    }

    [Fact]
    public void A_family_with_no_sections_is_empty_and_a_family_missing_a_required_section_is_empty()
    {
        Assert.Equal(TileDropReason.Empty, Reason(Family(TileKind.Narrative, ["onboarding"])));
        Assert.Equal(TileDropReason.Empty, Reason(Family(TileKind.Narrative, ["onboarding"], ("title", "T"))));
    }

    [Fact]
    public void A_section_key_that_the_family_does_not_have_is_schema_invalid()
    {
        var candidate = Family(TileKind.Narrative, ["onboarding"], ("title", "T"), ("body", Fill("Body.", 600)), ("pros", "Not a narrative section."));

        Assert.Equal(TileDropReason.SchemaInvalid, Reason(candidate));
    }

    [Fact]
    public void A_repeated_section_key_is_schema_invalid()
    {
        var candidate = Family(TileKind.Narrative, ["onboarding"], ("title", "T"), ("body", Fill("One.", 600)), ("body", Fill("Two.", 600)));

        Assert.Equal(TileDropReason.SchemaInvalid, Reason(candidate));
    }

    // ---- Copied runs: 7 words for every family but the narrative, 13 for the narrative -----------------------------------

    [Fact]
    public void A_structured_review_may_not_copy_seven_consecutive_words_of_the_interviewee()
    {
        var copies = Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"),
            ("pros", Fill("the weekly planning meeting never once started", 40)), ("cons", Fill("Slow start.", 40)));
        var fine = Family(TileKind.StructuredReview, ["onboarding"], ("title", "Notes"),
            ("pros", Fill("the weekly planning meeting never once", 40)), ("cons", Fill("Slow start.", 40)));

        Assert.Equal(TileDropReason.CopiedQuote, Reason(copies, Transcript));
        Assert.True(Accepted(fine, Transcript));
    }

    [Fact]
    public void A_narrative_may_copy_twelve_consecutive_words_of_the_interviewee_and_not_thirteen()
    {
        const string twelve = "Recalling that the weekly planning meeting never once started on time for anyone on";
        const string thirteen = "Recalling that the weekly planning meeting never once started on time for anyone on our";

        Assert.True(Accepted(WithBody(TileKind.Narrative, twelve), Transcript));
        Assert.Equal(TileDropReason.CopiedQuote, Reason(WithBody(TileKind.Narrative, thirteen), Transcript));
    }

    [Theory]
    [InlineData(TileKind.StructuredReview)]
    [InlineData(TileKind.ProfessionalPost)]
    [InlineData(TileKind.ShortPost)]
    public void The_short_families_may_not_copy_a_twelve_word_run_from_the_interviewee(TileKind kind) =>
        Assert.Equal(TileDropReason.CopiedQuote, Reason(WithBody(kind, "Recalling that the weekly planning meeting never once started on time for anyone on"), Transcript));

    [Fact]
    public void A_family_may_not_copy_seven_words_of_a_record_quote()
    {
        // The record's quote has ten words: copying seven of them in a row is refused.
        Assert.Equal(TileDropReason.CopiedQuote, Reason(WithBody(TileKind.ProfessionalPost, "The first weeks were chaotic and nobody had")));
    }

    // ---- Experience terms: the narrative only, and only in the interviewee's own framing ------------------------------------

    [Theory]
    [InlineData("In my experience, I was bullied by my lead in the second month.")]
    [InlineData("Moim zdaniem to był mobbing w zespole.")]
    public void Experience_terms_are_banned_outside_the_narrative(string text)
    {
        foreach (var kind in Families.Where(k => k != TileKind.Narrative))
            Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(kind, text), Transcript));
    }

    [Fact]
    public void Experience_terms_are_banned_in_the_generic_kinds_too()
    {
        var overview = new CandidateTile(TileKind.Overview, "Notes", "In my experience, I was bullied by my lead.", ["onboarding"]);

        Assert.Equal(TileDropReason.BannedTerm, Reason(overview, Transcript));
    }

    [Fact]
    public void A_narrative_may_use_an_experience_term_when_the_interviewee_used_it_and_the_sentence_is_framed_as_their_own()
    {
        Assert.True(Accepted(WithBody(TileKind.Narrative, "In my experience, I was bullied by my lead in the second month."), Transcript));
    }

    [Theory]
    [InlineData("Moim zdaniem to był mobbing w zespole.", "Interviewee: Odczuwałem mobbing w zespole od pierwszego dnia.\n")]
    [InlineData("Czułem się prześladowany przez kolegę.", "Interviewee: Czułem się prześladowany przez kolegę.\n")]
    [InlineData("Z mojej perspektywy zespół był toksyczny.", "Interviewee: Zespół był toksyczny.\n")]
    public void A_narrative_accepts_the_polish_first_person_framings(string text, string line) =>
        Assert.True(Accepted(WithBody(TileKind.Narrative, text), line));

    [Fact]
    public void A_narrative_drops_an_experience_term_without_a_first_person_framing()
    {
        Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(TileKind.Narrative, "The lead bullied me in the second month."), Transcript));
    }

    [Fact]
    public void A_narrative_drops_an_experience_term_the_interviewee_never_used_even_with_a_hedge()
    {
        const string transcriptWithoutTheWord = "Interviewer: Anything else?\nInterviewee: The lunch was fine most days.\n";

        Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(TileKind.Narrative, "In my experience, I was bullied by my lead."), transcriptWithoutTheWord));
    }

    [Fact]
    public void A_narrative_drops_an_experience_term_when_no_transcript_is_given_and_the_record_does_not_quote_it()
    {
        Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(TileKind.Narrative, "In my experience, I was bullied by my lead.")));
    }

    [Fact]
    public void A_narrative_accepts_an_experience_term_the_record_quotes_when_it_is_framed_as_the_interviewees_own()
    {
        var record = RecordWithCulture("I was harassed by a colleague in the second week");

        Assert.True(Accepted(WithBody(TileKind.Narrative, "In my experience, I was harassed by a colleague in the second week."), record: record));
    }

    [Fact]
    public void A_hedge_in_one_sentence_does_not_cover_the_term_in_another()
    {
        Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(TileKind.Narrative, "In my experience the team was fine. The lead bullied me in the second month."), Transcript));
    }

    // ---- Absolute terms, PII, the company token, identifying detail -----------------------------------------------------------

    [Theory]
    [InlineData("This was illegal and everyone knew it.")]
    [InlineData("To było nielegalne i wszyscy o tym wiedzieli.")]
    [InlineData("The management committed fraud in the review.")]
    [InlineData("Doszło do oszustwa przy rozliczeniu premii.")]
    [InlineData("Someone reported a theft of equipment.")]
    [InlineData("Ktoś próbował dać łapówkę kierownikowi.")]
    [InlineData("I was depressed for most of the year.")]
    [InlineData("Miałem wypalenie zawodowe po roku.")]
    [InlineData("The doctor gave me a diagnosis of a chronic illness.")]
    [InlineData("Choroba wpłynęła na moją pracę.")]
    public void Absolute_terms_are_banned_in_every_model_kind_even_with_a_hedge(string text)
    {
        foreach (var kind in Families)
            Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(kind, text), Transcript));
        Assert.Equal(TileDropReason.BannedTerm, Reason(WithBody(TileKind.Narrative, $"In my experience, {text}"), Transcript));
    }

    [Theory]
    [InlineData("[COMPANY] was the first place I worked where the first weeks were hard.")]
    [InlineData("[FIRMA] była pierwszym miejscem, gdzie pierwsze tygodnie były trudne.")]
    public void The_company_token_passes_the_pii_guard_in_every_family(string text)
    {
        foreach (var kind in Families)
            Assert.True(Accepted(WithBody(kind, text)), kind.ToString());
    }

    [Fact]
    public void A_company_token_does_not_hide_real_personal_data_in_the_same_text()
    {
        Assert.Equal(TileDropReason.PiiFound, Reason(WithBody(TileKind.StructuredReview, "[COMPANY] was hard. Reach me at anna.kowalska@example.com.")));
    }

    [Fact]
    public void A_person_named_in_a_narrative_drops_the_draft_as_pii()
    {
        Assert.Equal(TileDropReason.PiiFound, Reason(WithBody(TileKind.Narrative, "Anna Wójcik, my manager, changed the targets.")));
    }

    [Theory]
    [InlineData("On 12/04/2023 the meeting ran late.")]
    [InlineData("On March 3 the meeting ran late.")]
    [InlineData("3 stycznia spotkanie trwało długo.")]
    [InlineData("The team of 12 people had one lead.")]
    [InlineData("Zespół 12 osób miał jednego lidera.")]
    [InlineData("Pracowałem w 12-osobowym zespole.")]
    public void A_full_date_or_a_team_size_is_identifying_detail(string text)
    {
        Assert.Equal(TileDropReason.IdentifyingDetail, Reason(WithBody(TileKind.Narrative, text)));
        Assert.Equal(TileDropReason.IdentifyingDetail, Reason(new CandidateTile(TileKind.Overview, "Notes", text, ["onboarding"])));
    }

    [Theory]
    [InlineData("The March meeting was hard.")]
    [InlineData("The team helped each other.")]
    [InlineData("I was there for about two years.")]
    public void Ordinary_months_and_group_words_are_not_identifying_detail(string text) =>
        Assert.True(Accepted(WithBody(TileKind.Narrative, text)));

    // ---- Grounding: the family rules are the generic rules ----------------------------------------------------------------------

    [Fact]
    public void A_family_tile_must_cite_at_least_one_covered_topic()
    {
        Assert.Equal(TileDropReason.Ungrounded, Reason(Family(TileKind.Narrative, [], ("title", "T"), ("body", Fill("Neutral.", 600)))));
    }

    [Fact]
    public void A_family_tile_may_not_cite_a_no_data_topic()
    {
        Assert.Equal(TileDropReason.Ungrounded, Reason(Family(TileKind.Narrative, ["management"], ("title", "T"), ("body", Fill("Neutral.", 600)))));
    }

    [Fact]
    public void A_family_tile_may_not_name_a_no_data_topic_it_does_not_cite()
    {
        Assert.Equal(TileDropReason.Ungrounded, Reason(WithBody(TileKind.ShortPost, "My manager was fine.")));
    }

    // ---- The family table and the ids --------------------------------------------------------------------------------------

    [Fact]
    public void Each_family_has_its_own_id()
    {
        var result = Guard.Check(new TileInput(Record), [
            WithBody(TileKind.StructuredReview, "Good team."),
            WithBody(TileKind.Narrative, "Good team."),
            WithBody(TileKind.ProfessionalPost, "Good team."),
            WithBody(TileKind.ShortPost, "Good team."),
        ]);

        Assert.Equal(["structured-review", "narrative", "professional-post", "short-post"], result.Accepted.Select(t => t.Id));
    }

    [Fact]
    public void A_second_family_candidate_of_the_same_kind_is_schema_invalid_and_the_first_wins()
    {
        var first = WithBody(TileKind.ShortPost, "First.");

        var result = Guard.Check(new TileInput(Record), [first, WithBody(TileKind.ShortPost, "Second.")]);

        Assert.Equal(first.Text, Assert.Single(result.Accepted).Text);
        Assert.Equal(TileDropReason.SchemaInvalid, Assert.Single(result.Dropped).ReasonCode);
    }

    [Fact]
    public void The_family_table_is_the_table_of_the_spec()
    {
        var structured = TileFamilies.StructuredReview;
        Assert.Equal(["title", "pros", "cons", "advice"], structured.Sections.Select(s => s.Key));
        Assert.Equal([true, true, true, false], structured.Sections.Select(s => s.Required));
        Assert.Equal(1100, structured.TotalMax);
        Assert.Equal(7, structured.CopyRunWords);
        Assert.False(structured.ExperienceTerms);

        var narrative = TileFamilies.Narrative;
        Assert.Equal(120, narrative.Sections[0].Max);
        Assert.Equal(600, narrative.Sections[1].Min);
        Assert.Equal(4000, narrative.Sections[1].Max);
        Assert.Equal(13, narrative.CopyRunWords);
        Assert.True(narrative.ExperienceTerms);

        var professional = TileFamilies.ProfessionalPost;
        Assert.Equal(300, professional.Sections[0].Min);
        Assert.Equal(1300, professional.Sections[0].Max);

        var shortPost = TileFamilies.ShortPost;
        Assert.Equal(80, shortPost.Sections[0].Min);
        Assert.Equal(270, shortPost.Sections[0].Max);
    }

    [Fact]
    public void The_whole_draft_limits_are_the_family_totals_and_the_generic_limit_is_unchanged()
    {
        Assert.Equal(1100, TileLimits.MaxTextCharsFor(TileKind.StructuredReview));
        Assert.Equal(4120, TileLimits.MaxTextCharsFor(TileKind.Narrative));
        Assert.Equal(1300, TileLimits.MaxTextCharsFor(TileKind.ProfessionalPost));
        Assert.Equal(270, TileLimits.MaxTextCharsFor(TileKind.ShortPost));
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.Overview));
        Assert.Equal(600, TileLimits.MaxTextCharsFor(TileKind.Facts));
    }

    [Fact]
    public void Every_family_is_listed_and_no_family_is_a_generic_kind()
    {
        Assert.Equal(Families, TileFamilies.All.Select(f => f.Kind));
        Assert.All(Families, k => Assert.NotNull(TileFamilies.Find(k)));
        Assert.Null(TileFamilies.Find(TileKind.Overview));
        Assert.Null(TileFamilies.Find(TileKind.Facts));
    }
}
