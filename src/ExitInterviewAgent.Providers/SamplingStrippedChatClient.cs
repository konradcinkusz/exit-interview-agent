using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers;

/// <summary>
/// Removes the sampling controls (<c>Temperature</c>, <c>TopP</c>, <c>TopK</c>) from the options before a request leaves for
/// the Anthropic API. Current Claude models reject a non-default value for any of them with HTTP 400, so a request that
/// carries one never reaches the model; the models use their own defaults instead. The caller's options are not changed.
/// </summary>
internal sealed class SamplingStrippedChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Strip(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Strip(options), cancellationToken);

    private static ChatOptions? Strip(ChatOptions? options)
    {
        if (options is null || (options.Temperature is null && options.TopP is null && options.TopK is null)) return options;
        var copy = options.Clone();
        copy.Temperature = null;
        copy.TopP = null;
        copy.TopK = null;
        return copy;
    }
}
