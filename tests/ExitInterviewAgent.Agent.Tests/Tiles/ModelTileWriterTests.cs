using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Tiles.TileTestSupport;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

public class ModelTileWriterTests
{
    [Fact]
    public async Task The_writer_is_tagged_as_the_tile_writer_role_and_its_prompt_carries_only_the_record_data()
    {
        var model = new FakeChatClient((_, _) => "{\"tiles\":[]}");

        await new ModelTileWriter(model).WriteAsync(Full(), ["schema_invalid"], CancellationToken.None);

        var call = Assert.Single(model.Calls);
        Assert.Equal(Role.TileWriter, call.Role);
        Assert.StartsWith("ROLE: tilewriter\n", call.System, StringComparison.Ordinal);
        Assert.Contains("<<<RECORD_DATA ", call.User, StringComparison.Ordinal);
        Assert.Contains("<<<END_RECORD_DATA ", call.User, StringComparison.Ordinal);
        Assert.Contains("\"topic\":\"onboarding\"", call.User, StringComparison.Ordinal);
        Assert.Contains("schema_invalid", call.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_employer_reference_and_the_interview_id_never_reach_the_model()
    {
        var model = new FakeChatClient((_, _) => "{\"tiles\":[]}");

        await new ModelTileWriter(model).WriteAsync(Full(), [], CancellationToken.None);

        var call = Assert.Single(model.Calls);
        Assert.DoesNotContain("acme-sp-zoo", call.User, StringComparison.Ordinal);
        Assert.DoesNotContain("0f3c9a1e7b2d4c58a6e1903fd2b47c11", call.User, StringComparison.Ordinal);
        Assert.DoesNotContain("acme-sp-zoo", call.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_no_data_topic_is_not_offered_to_the_model()
    {
        var model = new FakeChatClient((_, _) => "{\"tiles\":[]}");

        await new ModelTileWriter(model).WriteAsync(WithTopic(Full(), Topic.Growth, TopicEntry.NoData), [], CancellationToken.None);

        var call = Assert.Single(model.Calls);
        Assert.DoesNotContain("\"topic\":\"growth\"", call.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_text_is_returned_unchanged_for_the_parser_to_judge()
    {
        const string reply = "not even json, returned as is";

        var text = await new ModelTileWriter(new FakeChatClient((_, _) => reply)).WriteAsync(Full(), [], CancellationToken.None);

        Assert.Equal(reply, text);
    }
}
