using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// Tile-writer role on any <see cref="IChatClient"/> (use the metered one the runner builds). Sees the record inside its data
/// block, and the PII-masked transcript in its own data block when the input carries one (ADR-0075). Both are data.
/// </summary>
public sealed class ModelTileWriter(IChatClient client) : ITileWriter
{
    /// <summary>Room for up to eight tiles, one of them up to 4500 characters.</summary>
    private const int MaxOutputTokens = 4000;

    public Task<string> WriteAsync(InterviewRecord record, IReadOnlyList<string> previousErrorCodes, CancellationToken ct) =>
        WriteAsync(new TileInput(record), previousErrorCodes, ct);

    public async Task<string> WriteAsync(TileInput input, IReadOnlyList<string> previousErrorCodes, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, TilePrompts.WriterSystem),
            new ChatMessage(ChatRole.User, TilePrompts.WriterUser(input, previousErrorCodes, TileWriterOutput.SchemaText, DataBlock.NewNonce())),
        };
        var response = await client.GetResponseAsync(messages, MeteredChatClient.Options(Role.TileWriter, MaxOutputTokens, 0f), ct).ConfigureAwait(false);
        return response.Text;
    }
}
