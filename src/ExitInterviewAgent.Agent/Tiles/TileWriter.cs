using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>Tile-writer role on any <see cref="IChatClient"/> (use the metered one the runner builds). Sees only the record, inside its data block.</summary>
public sealed class ModelTileWriter(IChatClient client) : ITileWriter
{
    public async Task<string> WriteAsync(InterviewRecord record, IReadOnlyList<string> previousErrorCodes, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, TilePrompts.WriterSystem),
            new ChatMessage(ChatRole.User, TilePrompts.WriterUser(record, previousErrorCodes, TileWriterOutput.SchemaText, DataBlock.NewNonce())),
        };
        var response = await client.GetResponseAsync(messages, MeteredChatClient.Options(Role.TileWriter, 1500, 0f), ct).ConfigureAwait(false);
        return response.Text;
    }
}
