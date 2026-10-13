using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Agent.Tiles.Platforms;

namespace ExitInterviewAgent.Agent.Tests.Tiles.Platforms;

/// <summary>
/// The draft builder (platform-drafts-spec §3.3, WP4): one draft per enabled profile whose family tile was accepted, in market order,
/// with the profile's sections, labels and copy rule, and never an edited word. Fixture profiles test the rules; the shipped profiles
/// carry the never-edit property.
/// </summary>
public class PlatformDraftBuilderTests
{
    private static readonly DateOnly Accessed = new(2026, 10, 12);

    private static PlatformSection Section(string key, string label, bool optional = false) =>
        new(key, new LocalizedText($"{label} EN", $"{label} PL"), optional);

    private static PlatformProfile Profile(string id, DraftFamily family, bool joinSections, params PlatformSection[] sections) => new(
        id,
        true,
        family,
        new LocalizedText($"{id} EN", $"{id} PL"),
        new LocalizedText($"https://{id}.example.com/", $"https://{id}.example.com/"),
        ["en", "pl"],
        "anonymous",
        "low",
        PlatformRisk.Medium,
        sections,
        joinSections,
        new PlatformLimits(60, 40, 500, 1100),
        ["individuals"],
        [new LocalizedText("Field EN", "Pole PL")],
        new LocalizedText("Warning EN.", "Ostrzeżenie PL."),
        "neutral",
        [new PlatformSource("https://example.com/", Accessed, "unverified")]);

    /// <summary>The profiles a test gives the builder: the enabled ones are the ones the builder sees.</summary>
    private sealed class Fixture(params PlatformProfile[] profiles) : IPlatformProfiles
    {
        public IReadOnlyList<PlatformProfile> Enabled { get; } = profiles.Where(p => p.Enabled).ToList();

        public PlatformProfile Get(string id) => profiles.Single(p => p.Id == id);
    }

    /// <summary>An accepted family tile: its text is the sections joined by a blank line, as the guard leaves it.</summary>
    private static Tile FamilyTile(TileKind kind, params (string Key, string Text)[] sections) => new(
        "t1",
        kind,
        "title",
        string.Join("\n\n", sections.Select(s => s.Text)),
        [],
        sections.Select(s => new TileSection(s.Key, s.Text)).ToList());

    private static TileSet SetOf(string language, params Tile[] tiles) => new(TileSet.CurrentVersion, language, tiles, []);

    [Fact]
    public void One_draft_per_enabled_profile_in_profile_order_when_every_profile_has_the_language()
    {
        var profiles = new Fixture(
            Profile("a", DraftFamily.StructuredReview, false, Section("pros", "Pros")),
            Profile("b", DraftFamily.StructuredReview, false, Section("pros", "Pros")) with { Enabled = false },
            Profile("c", DraftFamily.Narrative, false, Section("body", "Body")));
        var set = SetOf("en",
            FamilyTile(TileKind.StructuredReview, ("pros", "Good team.")),
            FamilyTile(TileKind.Narrative, ("body", "A body of text.")));

        var drafts = PlatformDraftBuilder.Build(set, profiles, "en");

        Assert.Equal(new[] { "a", "c" }, drafts.Select(d => d.Id));
    }

    [Fact]
    public void Profiles_whose_markets_include_the_language_come_first_in_profile_order_and_the_others_after_them()
    {
        var profiles = new Fixture(
            Profile("a", DraftFamily.StructuredReview, false, Section("pros", "Pros")) with { Markets = new[] { "en" } },
            Profile("b", DraftFamily.StructuredReview, false, Section("pros", "Pros")) with { Markets = new[] { "pl" } },
            Profile("c", DraftFamily.StructuredReview, false, Section("pros", "Pros")) with { Markets = new[] { "en", "pl" } });
        var set = SetOf("pl", FamilyTile(TileKind.StructuredReview, ("pros", "Dobry zespół.")));

        var drafts = PlatformDraftBuilder.Build(set, profiles, "pl");

        Assert.Equal(new[] { "b", "c", "a" }, drafts.Select(d => d.Id));
        Assert.Equal(new[] { true, true, false }, drafts.Select(d => d.Primary));
    }

    [Fact]
    public void In_polish_every_shipped_profile_has_a_polish_market_so_the_profile_order_stands_and_none_is_other()
    {
        var drafts = PlatformDraftBuilder.Build(AllFamilies("pl"), PlatformProfiles.Default, "pl");

        Assert.Equal(new[] { "glassdoor", "indeed", "gowork", "linkedin", "facebook_group", "reddit", "wykop", "blind", "x" }, drafts.Select(d => d.Id));
        Assert.All(drafts, d => Assert.True(d.Primary));
    }

    [Fact]
    public void In_english_the_polish_only_platforms_come_last_and_are_not_primary()
    {
        var drafts = PlatformDraftBuilder.Build(AllFamilies("en"), PlatformProfiles.Default, "en");

        Assert.Equal(new[] { "glassdoor", "indeed", "linkedin", "facebook_group", "reddit", "blind", "x", "gowork", "wykop" }, drafts.Select(d => d.Id));
        Assert.Equal(new[] { true, true, true, true, true, true, true, false, false }, drafts.Select(d => d.Primary));
    }

    /// <summary>One accepted tile per family, carrying every section key the shipped profiles name.</summary>
    private static TileSet AllFamilies(string language) => SetOf(language,
        FamilyTile(TileKind.StructuredReview, ("title", "Tytuł."), ("pros", "Plusy."), ("cons", "Minusy."), ("advice", "Rada.")),
        FamilyTile(TileKind.Narrative, ("title", "Tytuł."), ("body", "Treść.")),
        FamilyTile(TileKind.ProfessionalPost, ("body", "Wpis.")),
        FamilyTile(TileKind.ShortPost, ("body", "Krótki wpis.")));

    [Fact]
    public void A_profile_whose_family_tile_was_dropped_gets_no_draft()
    {
        var profiles = new Fixture(Profile("c", DraftFamily.Narrative, false, Section("body", "Body")));
        var set = SetOf("en", FamilyTile(TileKind.StructuredReview, ("pros", "Good team.")));

        Assert.Empty(PlatformDraftBuilder.Build(set, profiles, "en"));
    }

    [Fact]
    public void A_section_the_family_tile_does_not_carry_is_left_out_and_nothing_is_filled_in()
    {
        var profiles = new Fixture(Profile("a", DraftFamily.StructuredReview, false,
            Section("title", "Headline"), Section("pros", "Pros"), Section("advice", "Advice", optional: true)));
        var set = SetOf("en", FamilyTile(TileKind.StructuredReview, ("title", "A headline."), ("pros", "Good team.")));

        var draft = Assert.Single(PlatformDraftBuilder.Build(set, profiles, "en"));

        Assert.Equal(new[] { "title", "pros" }, draft.Sections.Select(s => s.Key));
    }

    [Fact]
    public void Sections_follow_the_profile_order_not_the_order_of_the_tile()
    {
        var profiles = new Fixture(Profile("a", DraftFamily.StructuredReview, false, Section("title", "Headline"), Section("pros", "Pros")));
        var set = SetOf("en", FamilyTile(TileKind.StructuredReview, ("pros", "Good team."), ("title", "A headline.")));

        var draft = Assert.Single(PlatformDraftBuilder.Build(set, profiles, "en"));

        Assert.Equal(new[] { "title", "pros" }, draft.Sections.Select(s => s.Key));
        Assert.Equal(new[] { "A headline.", "Good team." }, draft.Sections.Select(s => s.Text));
    }

    [Fact]
    public void A_blank_section_text_is_left_out_and_is_not_filled_in()
    {
        var profiles = new Fixture(Profile("a", DraftFamily.StructuredReview, false, Section("title", "Headline"), Section("pros", "Pros")));
        var set = SetOf("en", FamilyTile(TileKind.StructuredReview, ("title", "A headline."), ("pros", "   ")));

        var draft = Assert.Single(PlatformDraftBuilder.Build(set, profiles, "en"));

        Assert.Equal(new[] { "title" }, draft.Sections.Select(s => s.Key));
    }

    [Fact]
    public void Names_warnings_labels_and_fields_are_resolved_to_the_language_and_the_url_is_the_profile_url()
    {
        var profiles = new Fixture(Profile("a", DraftFamily.StructuredReview, false, Section("pros", "Pros")));
        var set = SetOf("pl", FamilyTile(TileKind.StructuredReview, ("pros", "Dobry zespół.")));

        var draft = Assert.Single(PlatformDraftBuilder.Build(set, profiles, "pl"));

        Assert.Equal("a PL", draft.Name);
        Assert.Equal("Ostrzeżenie PL.", draft.Warning);
        Assert.Equal("https://a.example.com/", draft.Url);
        Assert.Equal("Pros PL", Assert.Single(draft.Sections).Label);
        Assert.Equal(new[] { "Pole PL" }, draft.FieldsOnSite);
        Assert.Equal(DraftFamily.StructuredReview, draft.Family);
        Assert.Equal(PlatformRisk.Medium, draft.Risk);
    }

    [Fact]
    public void The_copy_text_has_labels_only_when_the_profile_joins_them()
    {
        var joined = new Fixture(Profile("a", DraftFamily.StructuredReview, true, Section("pros", "Pros"), Section("cons", "Cons")));
        var plain = new Fixture(Profile("a", DraftFamily.StructuredReview, false, Section("pros", "Pros"), Section("cons", "Cons")));
        var set = SetOf("en", FamilyTile(TileKind.StructuredReview, ("pros", "Good team."), ("cons", "Hard work.")));

        Assert.Equal("Pros EN\nGood team.\n\nCons EN\nHard work.", Assert.Single(PlatformDraftBuilder.Build(set, joined, "en")).CopyText);
        Assert.Equal("Good team.\n\nHard work.", Assert.Single(PlatformDraftBuilder.Build(set, plain, "en")).CopyText);
    }

    [Fact]
    public void GoWork_style_copy_text_has_no_title_and_no_labels()
    {
        var set = SetOf("pl", FamilyTile(TileKind.StructuredReview,
            ("title", "Notatki z rozmowy."), ("pros", "Dobry zespół, jasne zasady."), ("cons", "Długi proces wdrożenia."), ("advice", "Warto przejrzeć wdrożenie.")));

        var gowork = PlatformDraftBuilder.Build(set, PlatformProfiles.Default, "pl").Single(d => d.Id == "gowork");

        Assert.Equal(new[] { "pros", "cons", "advice" }, gowork.Sections.Select(s => s.Key));
        Assert.Equal("Dobry zespół, jasne zasady.\n\nDługi proces wdrożenia.\n\nWarto przejrzeć wdrożenie.", gowork.CopyText);
    }

    /// <summary>The sections of a family tile by key, from the tile itself.</summary>
    private static Dictionary<string, string> SourceSections(IEnumerable<Tile> tiles, DraftFamily family) =>
        tiles.Single(t => t.Kind == PlatformDraftBuilder.KindOf(family)).Sections!.ToDictionary(s => s.Key, s => s.Text);

    /// <summary>A non-blank text with characters that a rendering could mangle: quotes, markup, diacritics, line breaks, edge spaces.</summary>
    private static string RandomText(Random random)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz ĄĘÓŹŻąęóźż<>&\"'.,;:-\n\t  ";
        var body = new string(Enumerable.Range(0, random.Next(1, 120)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
        return "x" + body + " ";
    }

    [Fact]
    public void Rendering_never_edits_a_word_of_a_section_and_the_copy_text_adds_only_labels_and_blank_lines()
    {
        var random = new Random(20261012);
        var checkedSections = 0;
        for (var round = 0; round < 200; round++)
        {
            var tiles = new[]
            {
                FamilyTile(TileKind.StructuredReview, ("title", RandomText(random)), ("pros", RandomText(random)), ("cons", RandomText(random)), ("advice", RandomText(random))),
                FamilyTile(TileKind.Narrative, ("title", RandomText(random)), ("body", RandomText(random))),
                FamilyTile(TileKind.ProfessionalPost, ("body", RandomText(random))),
                FamilyTile(TileKind.ShortPost, ("body", RandomText(random))),
            };

            foreach (var draft in PlatformDraftBuilder.Build(SetOf("en", tiles), PlatformProfiles.Default, "en"))
            {
                var profile = PlatformProfiles.Default.Get(draft.Id);
                var source = SourceSections(tiles, profile.Family);
                foreach (var section in draft.Sections)
                {
                    Assert.Equal(source[section.Key], section.Text);
                    checkedSections++;
                }

                var expected = profile.JoinSections
                    ? string.Join("\n\n", draft.Sections.Select(s => $"{s.Label}\n{s.Text}"))
                    : string.Join("\n\n", draft.Sections.Select(s => s.Text));
                Assert.Equal(expected, draft.CopyText);
            }
        }

        // The property must have run: a test that checks nothing is worse than none.
        Assert.True(checkedSections > 0);
    }
}
