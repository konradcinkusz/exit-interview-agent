using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Agent.Tests.Tracing;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Agent.Tests.Tiles.Platforms;

/// <summary>
/// The two ADR-0083 schemas load as JSON Schema 2020-12 and accept and refuse what platform-drafts-spec §3.1 and §3.3 say. Every
/// rule has a case that must pass and one that must fail, so a schema that accepts everything cannot pass.
/// </summary>
public class PlatformSchemaTests
{
    private const string ProfilesFile = "platform-profiles.v1.schema.json";
    private const string WriterV2File = "tile-writer-output.v2.schema.json";

    private static readonly Lazy<JsonValidator> Profiles = new(() => new JsonValidator(Read(ProfilesFile)));
    private static readonly Lazy<JsonValidator> WriterV2 = new(() => new JsonValidator(Read(WriterV2File)));

    private static string Read(string file) => File.ReadAllText(Path.Combine(TraceTests.RepoRoot(), "schemas", file));

    private static bool IsValid(Lazy<JsonValidator> validator, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = validator.Value.Validate(doc.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = false, ValidateFormat = false });
        return result.IsValid;
    }

    [Fact]
    public void Both_schemas_declare_draft_2020_12_and_their_stable_ids()
    {
        foreach (var (file, id) in new[] { (ProfilesFile, "urn:exit-interview-agent:schema:platform-profiles:v1"), (WriterV2File, "urn:exit-interview-agent:schema:tile-writer-output:v2") })
        {
            using var doc = JsonDocument.Parse(Read(file));

            Assert.Equal("https://json-schema.org/draft/2020-12/schema", doc.RootElement.GetProperty("$schema").GetString());
            Assert.Equal(id, doc.RootElement.GetProperty("$id").GetString());
        }
    }

    // --- platform profiles (spec §3.1) -----------------------------------------------------------------------------------------

    private const string GlassdoorProfile = """
        {
          "id": "glassdoor",
          "enabled": true,
          "family": "structured_review",
          "names": { "en": "Glassdoor", "pl": "Glassdoor" },
          "url": { "en": "https://www.glassdoor.com/", "pl": "https://www.glassdoor.com/" },
          "markets": ["en", "pl"],
          "identity": "anonymous_verified_email",
          "detail": "low",
          "risk": "medium",
          "sections": [
            { "key": "title", "label": { "en": "Headline", "pl": "Nagłówek" } },
            { "key": "pros", "label": { "en": "Pros", "pl": "Plusy" } },
            { "key": "cons", "label": { "en": "Cons", "pl": "Minusy" } },
            { "key": "advice", "label": { "en": "Advice to management", "pl": "Rada dla zarządu" }, "optional": true }
          ],
          "joinSections": false,
          "limits": { "titleMax": 60, "sectionMin": 40, "sectionMax": 500, "totalMax": 1100 },
          "leaveOut": ["individuals", "executives", "employer_name", "confidential", "accusations", "protected", "contact", "identifying_detail"],
          "fieldsOnSite": [ { "en": "Employment status and job title", "pl": "Status zatrudnienia i stanowisko" } ],
          "warning": { "en": "Draft, not fact. Not legal advice.", "pl": "To szkic, nie fakt. To nie jest porada prawna." },
          "tone": "neutral, balanced",
          "sources": [ { "url": "https://help.glassdoor.com/s/article/Community-Guidelines", "accessed": "2026-10-12", "level": "search_summary" } ]
        }
        """;

    /// <summary>The fixture profile, with <paramref name="change"/> applied, inside the file's <c>profiles</c> array.</summary>
    private static bool ProfileIsValid(Action<JsonObject> change)
    {
        var profile = JsonNode.Parse(GlassdoorProfile)!.AsObject();
        change(profile);
        var file = new JsonObject { ["profiles"] = new JsonArray(profile) };
        return IsValid(Profiles, file.ToJsonString());
    }

    [Fact]
    public void A_profile_in_the_spec_shape_is_accepted()
    {
        Assert.True(ProfileIsValid(_ => { }));
    }

    [Fact]
    public void An_empty_profile_list_is_refused()
    {
        Assert.False(IsValid(Profiles, """{ "profiles": [] }"""));
    }

    [Fact]
    public void The_family_is_a_closed_vocabulary()
    {
        Assert.False(ProfileIsValid(p => p["family"] = "blog_post"));
        Assert.False(ProfileIsValid(p => p["family"] = "google_review"));
    }

    [Fact]
    public void Identity_detail_and_risk_are_closed_vocabularies()
    {
        Assert.False(ProfileIsValid(p => p["identity"] = "named"));
        Assert.False(ProfileIsValid(p => p["detail"] = "extreme"));
        Assert.False(ProfileIsValid(p => p["risk"] = "low"));
    }

    [Fact]
    public void Each_leave_out_code_is_from_the_closed_list_and_listed_once()
    {
        Assert.False(ProfileIsValid(p => p["leaveOut"]!.AsArray().Add("ethnicity")));
        Assert.False(ProfileIsValid(p => p["leaveOut"]!.AsArray().Add("contact")));
    }

    [Fact]
    public void Only_english_and_polish_are_markets()
    {
        Assert.False(ProfileIsValid(p => p["markets"]!.AsArray().Add("de")));
    }

    [Theory]
    [InlineData("http://www.glassdoor.com/")]
    [InlineData("https://www.glassdoor.com/?ref=draft")]
    [InlineData("https://www.glassdoor.com/#reviews")]
    [InlineData("ftp://www.glassdoor.com/")]
    public void Every_url_is_https_with_no_query_and_no_fragment(string url)
    {
        Assert.False(ProfileIsValid(p => p["url"]!["en"] = url));
    }

    [Fact]
    public void Every_label_warning_and_name_is_non_empty_in_both_languages()
    {
        Assert.False(ProfileIsValid(p => p["names"]!["pl"] = ""));
        Assert.False(ProfileIsValid(p => p["warning"]!["en"] = ""));
        Assert.False(ProfileIsValid(p => p["sections"]![1]!["label"]!["pl"] = ""));
    }

    [Fact]
    public void A_profile_needs_its_limits_and_leave_out_and_no_unknown_property()
    {
        Assert.False(ProfileIsValid(p => p.Remove("leaveOut")));
        Assert.False(ProfileIsValid(p => p["limits"]!.AsObject().Remove("totalMax")));
        Assert.False(ProfileIsValid(p => p["googleReview"] = true));
    }

    [Fact]
    public void Ids_are_lower_case_words_and_a_source_needs_a_date_and_a_level()
    {
        Assert.False(ProfileIsValid(p => p["id"] = "Glassdoor"));
        Assert.False(ProfileIsValid(p => p["sources"]![0]!["accessed"] = "12/10/2026"));
        Assert.False(ProfileIsValid(p => p["sources"]![0]!["level"] = "guessed"));
    }

    [Fact]
    public void A_profile_needs_at_least_one_source()
    {
        Assert.False(ProfileIsValid(p => p["sources"] = new JsonArray()));
    }

    // --- the family writer output, version 2 (spec §3.3) -----------------------------------------------------------------------

    private static bool ReplyIsValid(params JsonObject[] tiles) =>
        IsValid(WriterV2, new JsonObject { ["tiles"] = new JsonArray(tiles) }.ToJsonString());

    private static JsonObject General(string kind, string title = "Title", string text = "A short, neutral sentence.") => new()
    {
        ["kind"] = kind,
        ["title"] = title,
        ["text"] = text,
        ["basedOn"] = new JsonArray("culture"),
    };

    private static JsonObject Family(string kind, params JsonObject[] sections) => new()
    {
        ["kind"] = kind,
        ["sections"] = new JsonArray(sections),
        ["basedOn"] = new JsonArray("culture"),
    };

    private static JsonObject Section(string key, string text) => new() { ["key"] = key, ["text"] = text };

    /// <summary>The keys every member of the family must carry, in the family table of spec §3.3.</summary>
    private static readonly Dictionary<string, string[]> RequiredKeys = new()
    {
        ["structured_review"] = ["title", "pros", "cons"],
        ["narrative"] = ["title", "body"],
        ["professional_post"] = ["body"],
        ["short_post"] = ["body"],
    };

    private static JsonObject FamilyWithOneLongSection(string kind, string key, int length)
    {
        var keys = RequiredKeys[kind].Append(key).Distinct();
        return Family(kind, keys.Select(k => Section(k, k == key ? new string('a', length) : "A short section.")).ToArray());
    }

    [Fact]
    public void One_reply_with_a_general_tile_and_one_of_each_family_is_accepted()
    {
        Assert.True(ReplyIsValid(
            General("overview"),
            Family("structured_review", Section("title", "Mixed"), Section("pros", "Good team."), Section("cons", "Slow onboarding."), Section("advice", "Write the plan down.")),
            Family("narrative", Section("title", "My notes"), Section("body", "The first month was quiet.")),
            Family("professional_post", Section("body", "Short professional note.")),
            Family("short_post", Section("body", "One line for the feed."))));
    }

    [Theory]
    [InlineData("structured_review", "title", 60, true)]
    [InlineData("structured_review", "title", 61, false)]
    [InlineData("structured_review", "pros", 500, true)]
    [InlineData("structured_review", "pros", 501, false)]
    [InlineData("structured_review", "cons", 501, false)]
    [InlineData("structured_review", "advice", 500, true)]
    [InlineData("structured_review", "advice", 501, false)]
    [InlineData("narrative", "title", 120, true)]
    [InlineData("narrative", "title", 121, false)]
    [InlineData("narrative", "body", 4000, true)]
    [InlineData("narrative", "body", 4001, false)]
    [InlineData("professional_post", "body", 1300, true)]
    [InlineData("professional_post", "body", 1301, false)]
    [InlineData("short_post", "body", 270, true)]
    [InlineData("short_post", "body", 271, false)]
    public void Each_section_has_its_family_maximum_and_it_is_inclusive(string kind, string key, int length, bool accepted)
    {
        Assert.Equal(accepted, ReplyIsValid(FamilyWithOneLongSection(kind, key, length)));
    }

    [Fact]
    public void A_required_section_may_be_empty_so_the_guard_can_drop_it_as_empty()
    {
        Assert.True(ReplyIsValid(Family("structured_review", Section("title", "Headline"), Section("pros", ""), Section("cons", "A con."))));
    }

    [Theory]
    [InlineData("structured_review", "pros")]
    [InlineData("structured_review", "cons")]
    [InlineData("structured_review", "title")]
    [InlineData("narrative", "title")]
    [InlineData("narrative", "body")]
    public void A_family_draft_without_one_of_its_required_sections_is_refused(string kind, string missing)
    {
        var sections = RequiredKeys[kind].Where(k => k != missing).Select(k => Section(k, "Text.")).ToArray();
        Assert.False(ReplyIsValid(Family(kind, sections)));
    }

    [Fact]
    public void A_family_draft_with_an_unknown_section_key_is_refused()
    {
        Assert.False(ReplyIsValid(Family("structured_review", Section("title", "H"), Section("pros", "P."), Section("cons", "C."), Section("summary", "S."))));
    }

    [Fact]
    public void A_key_from_another_family_is_refused()
    {
        Assert.False(ReplyIsValid(Family("short_post", Section("pros", "A short post."))));
        Assert.False(ReplyIsValid(Family("narrative", Section("title", "T"), Section("body", "B."), Section("pros", "P."))));
    }

    [Fact]
    public void A_family_draft_has_sections_and_no_tile_level_text_or_title()
    {
        var withText = Family("short_post", Section("body", "One line."));
        withText["text"] = "One line.";
        var withTitle = Family("short_post", Section("body", "One line."));
        withTitle["title"] = "A title";

        Assert.False(ReplyIsValid(withText));
        Assert.False(ReplyIsValid(withTitle));
    }

    [Fact]
    public void A_general_tile_has_one_text_and_no_sections()
    {
        var withSections = General("overview");
        withSections["sections"] = new JsonArray(Section("body", "One line."));
        var noText = General("overview");
        noText.Remove("text");

        Assert.False(ReplyIsValid(withSections));
        Assert.False(ReplyIsValid(noText));
        Assert.False(ReplyIsValid(General("overview", text: new string('a', 601))));
        Assert.False(ReplyIsValid(General("overview", title: new string('a', 61))));
    }

    [Theory]
    [InlineData("glassdoor")]
    [InlineData("google_review")]
    [InlineData("reddit")]
    [InlineData("short_note")]
    public void The_retired_platform_kinds_are_refused_in_version_2(string kind)
    {
        Assert.False(ReplyIsValid(General(kind)));
    }

    [Fact]
    public void The_reply_holds_at_most_eight_tiles_and_may_be_empty()
    {
        Assert.False(ReplyIsValid(Enumerable.Range(0, 9).Select(_ => General("overview")).ToArray()));
        Assert.True(ReplyIsValid(Enumerable.Range(0, 8).Select(_ => General("overview")).ToArray()));
        Assert.True(IsValid(WriterV2, """{ "tiles": [] }"""));
    }
}
