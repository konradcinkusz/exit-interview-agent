using System.Text.Json.Nodes;
using ExitInterviewAgent.Agent.Tiles.Platforms;

namespace ExitInterviewAgent.Agent.Tests.Tiles.Platforms;

/// <summary>
/// The shipped platform profiles (platform-drafts-spec §3.1 and §3.2, invariants 1–6, 8 and 9; invariant 7, the goldens, is WP4).
/// Each invariant is a theory over every profile, and each profile has a named fact for its family, risk and host. The loader's
/// own refusals are checked at the end. A file that breaks an invariant must turn one of these red.
/// </summary>
public class PlatformProfileTests
{
    private const string WarningEndEn = "Draft, not fact. Not legal advice.";
    private const string WarningEndPl = "To szkic, nie fakt. To nie jest porada prawna.";

    /// <summary>The profile order of the file (research §4 order). A new profile is a reviewed change to this list.</summary>
    private static readonly string[] ExpectedIds =
        ["glassdoor", "indeed", "kununu", "gowork", "linkedin", "facebook_group", "reddit", "wykop", "blind", "x"];

    /// <summary>Invariant 2: the base leave-out set every profile must carry (spec §3.2, invariant 6).</summary>
    private static readonly string[] BaseLeaveOut = ["individuals", "employer_name", "confidential", "accusations", "protected", "contact"];

    /// <summary>Invariant 3: the host allowlist. A change of host needs a reviewed change to this table.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedHosts = new Dictionary<string, string[]>
    {
        ["glassdoor"] = ["www.glassdoor.com"],
        ["indeed"] = ["www.indeed.com"],
        ["kununu"] = ["www.kununu.com"],
        ["gowork"] = ["www.gowork.pl"],
        ["linkedin"] = ["www.linkedin.com"],
        ["facebook_group"] = ["www.facebook.com"],
        ["reddit"] = ["www.reddit.com"],
        ["wykop"] = ["www.wykop.pl"],
        ["blind"] = ["www.teamblind.com"],
        ["x"] = ["x.com"],
    };

    /// <summary>
    /// The family table of spec §3.3: the section keys, the title limit (null where the family has no title), the section range and
    /// the total. A profile's limits must sit inside these, so that the builder never has to cut a word.
    /// </summary>
    private sealed record FamilyRule(IReadOnlyList<string> Keys, int? TitleMax, int SectionMin, int SectionMax, int TotalMax);

    private static readonly IReadOnlyDictionary<DraftFamily, FamilyRule> Families = new Dictionary<DraftFamily, FamilyRule>
    {
        [DraftFamily.StructuredReview] = new(["title", "pros", "cons", "advice"], 60, 40, 500, 1100),
        [DraftFamily.Narrative] = new(["title", "body"], 120, 600, 4000, 4120),
        [DraftFamily.ProfessionalPost] = new(["body"], null, 300, 1300, 1300),
        [DraftFamily.ShortPost] = new(["body"], null, 80, 270, 270),
    };

    public static IEnumerable<object[]> ProfileIds() => PlatformProfiles.Default.All.Select(p => new object[] { p.Id });

    private static PlatformProfile Profile(string id) => PlatformProfiles.Default.Get(id);

    private static void AssertShape(string id, DraftFamily family, PlatformRisk risk, string host, bool enabled = true)
    {
        var p = Profile(id);
        Assert.Equal(enabled, p.Enabled);
        Assert.Equal(family, p.Family);
        Assert.Equal(risk, p.Risk);
        Assert.Equal(host, new Uri(p.Url.En).Host);
        Assert.Equal(host, new Uri(p.Url.Pl).Host);
    }

    // --- invariant 1: ids -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Profile_ids_are_unique()
    {
        var ids = PlatformProfiles.Default.All.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Profile_id_is_lower_case_words(string id)
    {
        Assert.Matches("^[a-z_]+$", id);
    }

    // --- invariant 2: names, labels and warnings --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Names_warning_and_section_labels_are_not_blank_in_either_language(string id)
    {
        var p = Profile(id);
        var texts = new List<string> { p.Names.En, p.Names.Pl, p.Warning.En, p.Warning.Pl };
        texts.AddRange(p.Sections.SelectMany(s => new[] { s.Label.En, s.Label.Pl }));

        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Warning_ends_with_the_fixed_sentence_in_both_languages(string id)
    {
        var p = Profile(id);

        Assert.EndsWith(WarningEndEn, p.Warning.En);
        Assert.EndsWith(WarningEndPl, p.Warning.Pl);
    }

    // --- invariant 3: URLs ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Urls_are_https_with_no_query_fragment_or_user_info_on_the_allowed_host(string id)
    {
        var p = Profile(id);
        Assert.True(AllowedHosts.ContainsKey(id), $"Profile '{id}' has no reviewed host allowlist entry.");

        foreach (var raw in new[] { p.Url.En, p.Url.Pl })
        {
            var uri = new Uri(raw, UriKind.Absolute);
            Assert.Equal("https", uri.Scheme);
            Assert.Equal(string.Empty, uri.Query);
            Assert.Equal(string.Empty, uri.Fragment);
            Assert.Equal(string.Empty, uri.UserInfo);
            Assert.Contains(uri.Host, AllowedHosts[id]);
        }
    }

    // --- invariant 4: the risk rule ------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Risk_is_high_for_a_real_name_or_high_detail_or_gowork_and_medium_otherwise(string id)
    {
        var p = Profile(id);
        var expected = p.Identity == "real_name" || p.Detail == "high" || p.Id == "gowork" ? PlatformRisk.High : PlatformRisk.Medium;

        Assert.Equal(expected, p.Risk);
    }

    // --- invariant 5: limits fit the family -------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Limits_and_section_keys_fit_inside_the_family_limits(string id)
    {
        var p = Profile(id);
        var family = Families[p.Family];
        var limits = p.Limits;

        Assert.True(limits.SectionMin <= limits.SectionMax, "sectionMin must not exceed sectionMax.");
        Assert.True(limits.SectionMin >= family.SectionMin, "sectionMin is below the family's minimum.");
        Assert.True(limits.SectionMax <= family.SectionMax, "sectionMax is above the family's maximum.");
        Assert.True(limits.TotalMax <= family.TotalMax, "totalMax is above the family's total.");
        Assert.True(limits.TotalMax >= limits.SectionMax, "totalMax must hold one full section.");
        if (family.TitleMax is int titleMax && p.Sections.Any(s => s.Key == "title"))
        {
            Assert.True(limits.TitleMax <= titleMax, "titleMax is above the family's title limit.");
        }

        Assert.All(p.Sections, s => Assert.Contains(s.Key, family.Keys));
        Assert.Equal(p.Sections.Count, p.Sections.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Platform_caps_are_respected_linkedin_at_3000_and_x_at_280()
    {
        var linkedin = Profile("linkedin").Limits;
        var x = Profile("x").Limits;

        Assert.True(linkedin.SectionMax <= 3000 && linkedin.TotalMax <= 3000);
        Assert.True(x.SectionMax <= 280 && x.TotalMax <= 280);
    }

    // --- invariant 6: the leave-out base set ----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Leave_out_holds_at_least_the_base_set(string id)
    {
        var p = Profile(id);

        Assert.All(BaseLeaveOut, code => Assert.Contains(code, p.LeaveOut));
    }

    // --- invariant 8: sources ------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ProfileIds))]
    public void Every_source_is_https_with_a_known_level_and_an_enabled_profile_has_one_that_is_not_unverified(string id)
    {
        var p = Profile(id);
        Assert.NotEmpty(p.Sources);

        foreach (var source in p.Sources)
        {
            var uri = new Uri(source.Url, UriKind.Absolute);
            Assert.Equal("https", uri.Scheme);
            Assert.Equal(string.Empty, uri.Query);
            Assert.Equal(string.Empty, uri.Fragment);
            Assert.Contains(source.Level, new[] { "fetched", "search_summary", "unverified" });
        }

        Assert.True(p.Sources.Any(s => s.Level != "unverified") || !p.Enabled, "An enabled profile needs a source that is not unverified.");
    }

    [Fact]
    public void Kununu_is_disabled_because_every_one_of_its_sources_is_unverified()
    {
        var kununu = Profile("kununu");

        Assert.False(kununu.Enabled);
        Assert.All(kununu.Sources, s => Assert.Equal("unverified", s.Level));
    }

    // --- invariant 9: the Google retirement (ADR-0083) -----------------------------------------------------------------------

    [Fact]
    public void No_profile_is_a_google_review_and_no_text_mentions_google_or_trustpilot()
    {
        Assert.DoesNotContain("google_review", PlatformProfiles.Default.All.Select(p => p.Id));
        Assert.DoesNotContain("google", PlatformProfiles.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trustpilot", PlatformProfiles.Text, StringComparison.OrdinalIgnoreCase);
    }

    // --- one fact per profile: family, risk and host --------------------------------------------------------------------------

    [Fact]
    public void Glassdoor_is_a_structured_review_of_medium_risk_on_glassdoor()
    {
        AssertShape("glassdoor", DraftFamily.StructuredReview, PlatformRisk.Medium, "www.glassdoor.com");
    }

    [Fact]
    public void Indeed_is_a_structured_review_of_medium_risk_on_indeed()
    {
        AssertShape("indeed", DraftFamily.StructuredReview, PlatformRisk.Medium, "www.indeed.com");
    }

    [Fact]
    public void Kununu_is_a_structured_review_of_medium_risk_on_kununu_and_is_disabled()
    {
        AssertShape("kununu", DraftFamily.StructuredReview, PlatformRisk.Medium, "www.kununu.com", enabled: false);
    }

    [Fact]
    public void GoWork_is_a_structured_review_of_high_risk_on_gowork()
    {
        AssertShape("gowork", DraftFamily.StructuredReview, PlatformRisk.High, "www.gowork.pl");
    }

    [Fact]
    public void LinkedIn_is_a_professional_post_of_high_risk_on_linkedin()
    {
        AssertShape("linkedin", DraftFamily.ProfessionalPost, PlatformRisk.High, "www.linkedin.com");
    }

    [Fact]
    public void Facebook_group_is_a_professional_post_of_high_risk_on_facebook()
    {
        AssertShape("facebook_group", DraftFamily.ProfessionalPost, PlatformRisk.High, "www.facebook.com");
    }

    [Fact]
    public void Reddit_is_a_narrative_of_high_risk_on_reddit()
    {
        AssertShape("reddit", DraftFamily.Narrative, PlatformRisk.High, "www.reddit.com");
    }

    [Fact]
    public void Wykop_is_a_narrative_of_high_risk_on_wykop()
    {
        AssertShape("wykop", DraftFamily.Narrative, PlatformRisk.High, "www.wykop.pl");
    }

    [Fact]
    public void Blind_is_a_structured_review_of_medium_risk_on_blind()
    {
        AssertShape("blind", DraftFamily.StructuredReview, PlatformRisk.Medium, "www.teamblind.com");
    }

    [Fact]
    public void X_is_a_short_post_of_medium_risk_on_x()
    {
        AssertShape("x", DraftFamily.ShortPost, PlatformRisk.Medium, "x.com");
    }

    // --- the loader -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_shipped_file_loads_the_ten_profiles_in_file_order()
    {
        Assert.Equal(ExpectedIds, PlatformProfiles.Default.All.Select(p => p.Id));
    }

    [Fact]
    public void Enabled_is_every_profile_but_kununu_in_file_order()
    {
        Assert.Equal(ExpectedIds.Where(id => id != "kununu"), PlatformProfiles.Default.Enabled.Select(p => p.Id));
    }

    [Fact]
    public void Get_finds_a_disabled_profile_and_throws_for_an_unknown_id()
    {
        Assert.False(PlatformProfiles.Default.Get("kununu").Enabled);
        Assert.Throws<KeyNotFoundException>(() => PlatformProfiles.Default.Get("google_review"));
    }

    [Fact]
    public void Parse_accepts_the_shipped_text()
    {
        Assert.Equal(ExpectedIds.Length, PlatformProfiles.Parse(PlatformProfiles.Text).All.Count);
    }

    [Fact]
    public void Parse_refuses_malformed_json()
    {
        Assert.Throws<InvalidDataException>(() => PlatformProfiles.Parse("{"));
    }

    [Fact]
    public void Parse_refuses_an_empty_warning()
    {
        Assert.Throws<InvalidDataException>(() => ParseMutated(root => Glassdoor(root)["warning"]!["en"] = ""));
    }

    [Fact]
    public void Parse_refuses_an_http_url()
    {
        Assert.Throws<InvalidDataException>(() => ParseMutated(root => Glassdoor(root)["url"]!["en"] = "http://www.glassdoor.com/"));
    }

    [Fact]
    public void Parse_refuses_an_unknown_family()
    {
        Assert.Throws<InvalidDataException>(() => ParseMutated(root => Glassdoor(root)["family"] = "google_review"));
    }

    [Fact]
    public void Parse_refuses_two_profiles_with_the_same_id()
    {
        Assert.Throws<InvalidDataException>(() => ParseMutated(root =>
        {
            var profiles = root["profiles"]!.AsArray();
            profiles.Add(profiles[0]!.DeepClone());
        }));
    }

    /// <summary>Loads the shipped text, applies one change and parses the result, so a refusal is the loader's own.</summary>
    private static void ParseMutated(Action<JsonNode> change)
    {
        var root = JsonNode.Parse(PlatformProfiles.Text)!;
        change(root);
        PlatformProfiles.Parse(root.ToJsonString());
    }

    private static JsonObject Glassdoor(JsonNode root) => root["profiles"]![0]!.AsObject();
}
