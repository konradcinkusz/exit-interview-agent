using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers;

/// <summary>
/// A thin client for Ollama's native <c>POST /api/chat</c> (non-streaming): system, user and assistant text in, text and token
/// counts out. Hand-written because the Microsoft package for Ollama is deprecated (its NuGet page recommends OllamaSharp) and OllamaSharp would add a
/// second dependency tree for one endpoint ([ADR-0032](../../docs/adr/0032-provider-packages-and-adapters.md)). It speaks only to the
/// <see cref="HttpClient"/> it is given (which carries the retry policy) and never reads an error body.
/// </summary>
internal sealed class OllamaChatClient : IChatClient
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _chatUri;
    private readonly string _model;
    private readonly int? _numCtx;

    public OllamaChatClient(HttpClient http, Uri baseUrl, string model, int? numCtx)
    {
        _http = http;
        _model = model;
        _numCtx = numCtx;
        var root = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        _chatUri = new Uri(root, "api/chat");
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var body = BuildRequest(messages, options);
        using var request = new HttpRequestMessage(HttpMethod.Post, _chatUri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var json = await ReadLimitedAsync(response.Content, cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var content = root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : throw new JsonException("no message");
        var reply = new ChatResponse(new ChatMessage(ChatRole.Assistant, content))
        {
            ModelId = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : _model,
            FinishReason = root.TryGetProperty("done_reason", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() switch { "stop" => ChatFinishReason.Stop, "length" => ChatFinishReason.Length, _ => null } : null,
        };
        if (root.TryGetProperty("prompt_eval_count", out var pe) && pe.TryGetInt64(out var inTokens) && root.TryGetProperty("eval_count", out var ev) && ev.TryGetInt64(out var outTokens))
            reply.Usage = new UsageDetails { InputTokenCount = inTokens, OutputTokenCount = outTokens, TotalTokenCount = inTokens + outTokens };
        return reply;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is off.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private string BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var list = new JsonArray();
        foreach (var message in messages)
        {
            var role = message.Role == ChatRole.System ? "system" : message.Role == ChatRole.Assistant ? "assistant" : message.Role == ChatRole.Tool ? "tool" : "user";
            list.Add(new JsonObject { ["role"] = role, ["content"] = message.Text });
        }

        var settings = new JsonObject();
        if (options?.Temperature is { } t) settings["temperature"] = t;
        if (options?.MaxOutputTokens is { } n) settings["num_predict"] = n;
        if (_numCtx is { } ctx) settings["num_ctx"] = ctx;

        var request = new JsonObject { ["model"] = _model, ["messages"] = list, ["stream"] = false };
        if (settings.Count > 0) request["options"] = settings;
        if (options?.ResponseFormat is ChatResponseFormatJson) request["format"] = "json";
        return request.ToJsonString();
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new JsonException("too large");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
