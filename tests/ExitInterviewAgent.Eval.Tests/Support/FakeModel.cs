using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Eval.Tests.Support;

/// <summary>A model double for the judge and classifier tests: answers by a delegate over the system and user text, and records every prompt.</summary>
internal sealed class FakeModel(Func<string, string, string> answer, string modelId = "fake-judge") : IChatClient
{
    public List<(string System, string User)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var system = list.First(m => m.Role == ChatRole.System).Text;
        var user = list.Last(m => m.Role == ChatRole.User).Text;
        Calls.Add((system, user));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer(system, user))) { ModelId = modelId });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
