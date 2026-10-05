using System.Text.Json;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Agent.Mock;

/// <summary>
/// A deterministic, offline <see cref="IChatClient"/> that plays the interviewer, prober and extractor roles from
/// templates and rules (no network, no credentials, no randomness). It is a SEAM for tests and demos and NOT a quality
/// baseline: it cannot be talked into anything because it understands nothing, so a clean result with it says
/// nothing about how a real model behaves under the same prompts. Real providers arrive with T6.
/// The role is read from the <c>ROLE:</c> marker on the first line of the system prompt.
/// </summary>
public sealed partial class ScriptedChatClient : IChatClient
{
    public const string ModelId = "scripted-mock";

    private static readonly ChatClientMetadata Metadata = new("mock", null, ModelId);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = messages.ToList();
        var system = list.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
        var user = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

        var reply = RoleOf(system) switch
        {
            "interviewer" or "prober" => Field(user, "SEED") ?? string.Empty,
            "extractor" => Extract(user),
            _ => string.Empty,
        };

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
        {
            ModelId = ModelId,
            Usage = new UsageDetails { InputTokenCount = ModelMeter.Estimate(system) + ModelMeter.Estimate(user), OutputTokenCount = ModelMeter.Estimate(reply) },
        };
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsAssignableFrom(typeof(ChatClientMetadata)) ? Metadata : serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string RoleOf(string system)
    {
        var first = system.TrimStart().Split('\n', 2)[0].Trim();
        return first.StartsWith(Prompts.RoleMarker, StringComparison.Ordinal) ? first[Prompts.RoleMarker.Length..].Trim() : string.Empty;
    }

    private static string? Field(string user, string name) =>
        user.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(name + ": ", StringComparison.Ordinal)) is { } line ? line[(name.Length + 2)..] : null;

    // ---- extractor ---------------------------------------------------------------------------------------------

    private static string Extract(string user)
    {
        var byTopic = new Dictionary<string, List<string>>();
        var inside = false;
        foreach (var raw in user.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("<<<TRANSCRIPT_DATA ", StringComparison.Ordinal)) { inside = true; continue; }
            if (line.StartsWith("<<<END_TRANSCRIPT_DATA ", StringComparison.Ordinal)) { inside = false; continue; }
            if (!inside || !line.StartsWith('{')) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.GetProperty("speaker").GetString() != "interviewee" || root.GetProperty("topic").ValueKind != JsonValueKind.String) continue;
            var topic = root.GetProperty("topic").GetString()!;
            if (!byTopic.TryGetValue(topic, out var texts)) byTopic[topic] = texts = [];
            texts.Add(root.GetProperty("text").GetString() ?? string.Empty);
        }

        var topics = new Dictionary<string, object>();
        foreach (var name in Wire.Names<Topic>())
            topics[name] = byTopic.TryGetValue(name, out var texts) ? ExtractTopic(texts) : new { status = "no_data" };
        return JsonSerializer.Serialize(new { topics });
    }

    private static object ExtractTopic(List<string> texts)
    {
        var all = string.Join(' ', texts);
        if (ReplyAnalyzer.CountWords(all) < 5) return new { status = "no_data" };

        var sentences = SentenceSplit().Split(all).Select(s => s.Trim()).Where(s => ReplyAnalyzer.CountWords(s) >= 4 && !ReplyAnalyzer.HasHostileCue(s)).ToList();
        if (sentences.Count == 0) return new { status = "no_data" };
        var quotes = sentences
            .Select((s, i) => (s, i, concrete: ReplyAnalyzer.HasConcreteCue(s)))
            .OrderByDescending(x => x.concrete).ThenBy(x => x.i)
            .Take(2).OrderBy(x => x.i)
            .Select(x => Clip(x.s)).ToArray();

        var (pos, neg) = ReplyAnalyzer.CueCounts(string.Join(' ', quotes));
        int? rating = pos + neg == 0 ? null : (pos - neg) switch { >= 3 => 5, >= 1 => 4, 0 => 3, >= -2 => 2, _ => 1 };
        var words = ReplyAnalyzer.CountWords(all);
        var confidence = words >= 40 && quotes.Length >= 2 ? "high" : words >= 15 ? "medium" : "low";
        return new { status = "covered", rating, confidence, quotes };
    }

    private static string Clip(string s)
    {
        if (s.EnumerateRunes().Count() <= TopicEntry.MaxQuoteLength) return s;
        var cut = s[..TopicEntry.MaxQuoteLength];
        var space = cut.LastIndexOf(' ');
        return space > 0 ? cut[..space] : cut;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex SentenceSplit();
}
