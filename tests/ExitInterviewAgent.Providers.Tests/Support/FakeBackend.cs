using System.Net;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Mock;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers.Tests.Support;

/// <summary>One request as the "provider" saw it. Headers and body are exactly what went over the (fake) wire.</summary>
public sealed record SeenRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public string Path => Uri.AbsolutePath;
}

/// <summary>
/// A fake provider that speaks one provider's wire format and answers with the offline scripted model, so a whole interview runs
/// through the real SDK adapters without a network. A <see cref="Script"/> can override any call (status codes, delays, exceptions).
/// </summary>
public sealed class FakeBackend(ProviderKind kind) : HttpMessageHandler
{
    private readonly List<SeenRequest> _seen = [];
    private int _calls;

    /// <summary>Decides call n (1-based). Null falls through to a normal answer.</summary>
    public Func<int, SeenRequest, Task<HttpResponseMessage?>>? Script { get; set; }

    /// <summary>Call n never answers: it waits for the caller's cancellation, as a hung server would.</summary>
    public Func<int, bool>? HangOn { get; set; }

    /// <summary>Extra response headers on every answer (the canary test plants one).</summary>
    public Dictionary<string, string> ResponseHeaders { get; } = [];

    public IReadOnlyList<SeenRequest> Seen { get { lock (_seen) return _seen.ToList(); } }

    public int Calls => Volatile.Read(ref _calls);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new SeenRequest(request.Method, request.RequestUri!, headers, body);
        lock (_seen) _seen.Add(seen);
        var n = Interlocked.Increment(ref _calls);

        if (HangOn?.Invoke(n) == true) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (Script is not null && await Script(n, seen) is { } scripted) return scripted;
        return await Answer(seen, cancellationToken);
    }

    private async Task<HttpResponseMessage> Answer(SeenRequest seen, CancellationToken ct)
    {
        var messages = Decode(seen.Body);
        var reply = await new ScriptedChatClient().GetResponseAsync(messages, null, ct);
        var text = reply.Text;
        var input = reply.Usage!.InputTokenCount!.Value;
        var output = reply.Usage.OutputTokenCount!.Value;

        var json = kind switch
        {
            ProviderKind.Anthropic => JsonSerializer.Serialize(new
            {
                id = "msg_fake",
                type = "message",
                role = "assistant",
                model = "fake-model",
                content = new[] { new { type = "text", text } },
                stop_reason = "end_turn",
                stop_sequence = (string?)null,
                usage = new { input_tokens = input, output_tokens = output },
            }),
            ProviderKind.OpenAiCompatible => JsonSerializer.Serialize(new
            {
                id = "chatcmpl-fake",
                @object = "chat.completion",
                created = 1,
                model = "fake-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = text }, finish_reason = "stop" } },
                usage = new { prompt_tokens = input, completion_tokens = output, total_tokens = input + output },
            }),
            _ => JsonSerializer.Serialize(new
            {
                model = "fake-model",
                created_at = "2026-10-05T12:00:00Z",
                message = new { role = "assistant", content = text },
                done = true,
                done_reason = "stop",
                prompt_eval_count = input,
                eval_count = output,
            }),
        };
        return Json(HttpStatusCode.OK, json);
    }

    public HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (k, v) in ResponseHeaders) response.Headers.TryAddWithoutValidation(k, v);
        return response;
    }

    /// <summary>A provider-shaped error whose body echoes the request, as real providers sometimes do: if any of it reaches a message, a span or a log the canary tests fail.</summary>
    public HttpResponseMessage ErrorEchoing(HttpStatusCode status, SeenRequest seen) =>
        Json(status, JsonSerializer.Serialize(new { type = "error", error = new { type = "x", message = "echo: " + seen.Body } }));

    private IList<ChatMessage> Decode(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var list = new List<ChatMessage>();
        if (kind == ProviderKind.Anthropic && root.TryGetProperty("system", out var system))
            list.Add(new ChatMessage(ChatRole.System, system.ValueKind == JsonValueKind.String ? system.GetString() : string.Concat(system.EnumerateArray().Select(b => b.GetProperty("text").GetString()))));
        foreach (var m in root.GetProperty("messages").EnumerateArray())
        {
            var role = m.GetProperty("role").GetString();
            var content = m.GetProperty("content");
            var text = content.ValueKind == JsonValueKind.String ? content.GetString()! : string.Concat(content.EnumerateArray().Select(b => b.GetProperty("text").GetString()));
            list.Add(new ChatMessage(role == "system" ? ChatRole.System : role == "assistant" ? ChatRole.Assistant : ChatRole.User, text));
        }
        return list;
    }
}
