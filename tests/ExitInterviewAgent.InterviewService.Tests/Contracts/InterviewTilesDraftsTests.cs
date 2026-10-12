using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.InterviewService.Tests.Contracts;

/// <summary>
/// The <c>drafts</c> field of <c>tiles</c> (ADR-0083, platform-drafts-spec §3.4). The service sends an empty list until WP4 builds
/// drafts; the field is present either way, so the BFF never has to guess whether it is missing.
/// </summary>
public class InterviewTilesDraftsTests
{
    // The same options the minimal API writes responses with (camelCase, nulls written).
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private static JsonNode Serialize(InterviewTiles tiles) => JsonNode.Parse(JsonSerializer.Serialize(tiles, Wire))!;

    [Fact]
    public void Tiles_without_drafts_serialize_an_empty_drafts_list()
    {
        var json = Serialize(new InterviewTiles([], [], "Notice."));

        Assert.Equal(JsonValueKind.Array, json["drafts"]!.GetValueKind());
        Assert.Empty(json["drafts"]!.AsArray());
    }

    [Fact]
    public void A_draft_serializes_with_the_camel_case_names_the_bff_reads()
    {
        var draft = new InterviewPlatformDraft(
            Platform: "gowork",
            Name: "GoWork.pl",
            Family: "structured_review",
            Risk: "high",
            Url: "https://www.gowork.pl/",
            Warning: "Szkic, nie fakt.",
            Sections: [new InterviewDraftSection("pros", "Plusy", "Dobry zespół.")],
            CopyText: "Dobry zespół.",
            FieldsOnSite: ["Status zatrudnienia"]);

        var json = Serialize(new InterviewTiles([], [], "Notice.", [draft]));
        var first = json["drafts"]![0]!;

        Assert.Equal("gowork", first["platform"]!.GetValue<string>());
        Assert.Equal("structured_review", first["family"]!.GetValue<string>());
        Assert.Equal("high", first["risk"]!.GetValue<string>());
        Assert.Equal("https://www.gowork.pl/", first["url"]!.GetValue<string>());
        Assert.Equal("Dobry zespół.", first["copyText"]!.GetValue<string>());
        Assert.Equal("Dobry zespół.", first["sections"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("Plusy", first["sections"]![0]!["label"]!.GetValue<string>());
        Assert.Equal("Status zatrudnienia", first["fieldsOnSite"]![0]!.GetValue<string>());
    }

    [Fact]
    public void The_three_argument_constructor_and_the_four_argument_one_with_an_empty_list_serialize_alike()
    {
        var three = Serialize(new InterviewTiles([new InterviewTile("facts", "Text.")], [], "Notice."));
        var four = Serialize(new InterviewTiles([new InterviewTile("facts", "Text.")], [], "Notice.", []));

        Assert.True(JsonNode.DeepEquals(three, four));
    }
}
