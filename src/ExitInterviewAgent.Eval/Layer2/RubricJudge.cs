using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Eval.Layer1;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Eval.Layer2;

/// <summary>What the judge is shown for one item: the interviewer's question, its kind, and (for a probe) the reply it follows. All of it is data.</summary>
public sealed record JudgeInput(string Rubric, string Kind, string Question, string PrecededBy);

public sealed record JudgeScore(string Rubric, int? Score, string Status, string Justification, bool InputInstructionLike);

/// <summary>
/// The Layer 2 judge. Rubric-anchored per criterion, a pinned model id (recorded with the model that actually answered), a prompt whose SHA-256 is
/// recorded, and every piece of interview text treated as untrusted data: it only ever appears inside a nonce-marked block of one JSON object per
/// line, never in the system prompt, and the reply is parsed strictly (anything but a valid score is <c>invalid</c>, never a pass).
/// Scores threshold and trend; they gate nothing until calibrated against human labels (docs/eval/SPEC.md §9).
/// </summary>
public sealed class RubricJudge(IChatClient client, string pinnedModelId, Rubric rubric)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string PinnedModelId => pinnedModelId;

    /// <summary>The model the service says actually answered (last response), which may differ from the pinned id; recorded, never assumed.</summary>
    public string? AnsweringModelId { get; private set; }

    public static string SystemPrompt(Criterion c)
    {
        var template = File.ReadAllText(Rubric.PromptPath);
        var anchors = string.Join('\n', c.Anchors.OrderBy(a => a.Key).Select(a => $"  {a.Key}: {a.Value}"));
        return template.Replace("{{CRITERION_ID}}", c.Id, StringComparison.Ordinal).Replace("{{CRITERION_NAME}}", c.Name, StringComparison.Ordinal)
            .Replace("{{SUMMARY}}", c.Summary.Trim(), StringComparison.Ordinal).Replace("{{ANCHORS}}", anchors, StringComparison.Ordinal)
            .Replace("{{MAX_LEVEL}}", c.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>The user message: a nonce-marked data block. A reply that contains the end marker cannot close it (it is JSON-escaped and the nonce is random).</summary>
    public static string UserMessage(JudgeInput input, string nonce) =>
        $"<<<ITEM_DATA {nonce}>>>\n{JsonSerializer.Serialize(new { rubric = input.Rubric, kind = input.Kind, question = input.Question, preceded_by = input.PrecededBy }, Json)}\n<<<END_ITEM_DATA {nonce}>>>";

    public async Task<JudgeScore> JudgeAsync(JudgeInput input, CancellationToken ct = default)
    {
        var c = rubric.Get(input.Rubric);
        var suspicious = IndependentRules.LooksLikeInstruction(input.Question) || IndependentRules.LooksLikeInstruction(input.PrecededBy);
        var nonce = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var messages = new[] { new ChatMessage(ChatRole.System, SystemPrompt(c)), new ChatMessage(ChatRole.User, UserMessage(input, nonce)) };
        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messages, new ChatOptions { Temperature = 0f, MaxOutputTokens = 300, ModelId = pinnedModelId }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JudgeScore(input.Rubric, null, "error:" + ex.GetType().Name, "", suspicious);
        }
        AnsweringModelId = response.ModelId ?? AnsweringModelId;
        return Parse(input.Rubric, response.Text, c.Scale, suspicious);
    }

    /// <summary>Strict parse: exactly {"score": int, "justification": string}, score within the scale. Anything else is invalid.</summary>
    public static JudgeScore Parse(string rubricId, string text, int scale, bool suspicious = false)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.Trim());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2) return new JudgeScore(rubricId, null, "invalid:shape", "", suspicious);
            if (!root.TryGetProperty("score", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out var score)) return new JudgeScore(rubricId, null, "invalid:score", "", suspicious);
            if (score < 0 || score > scale) return new JudgeScore(rubricId, null, "invalid:range", "", suspicious);
            if (!root.TryGetProperty("justification", out var j) || j.ValueKind != JsonValueKind.String) return new JudgeScore(rubricId, null, "invalid:justification", "", suspicious);
            var why = j.GetString()!;
            return new JudgeScore(rubricId, score, "ok", why.Length > 240 ? why[..240] : why, suspicious);
        }
        catch (JsonException) { return new JudgeScore(rubricId, null, "invalid:json", "", suspicious); }
    }

    /// <summary>
    /// The judge-injection differential (threat T-05): score the same item with and without the instruction-like sentences, and report whether the
    /// score moved. A susceptible judge moves; a robust one does not. Runs whenever a judge credential exists.
    /// </summary>
    public async Task<(JudgeScore With, JudgeScore Without, bool Moved)> InjectionDifferentialAsync(JudgeInput input, Func<string, string> strip, CancellationToken ct = default)
    {
        var with = await JudgeAsync(input, ct).ConfigureAwait(false);
        var without = await JudgeAsync(input with { Question = strip(input.Question), PrecededBy = strip(input.PrecededBy) }, ct).ConfigureAwait(false);
        return (with, without, with.Score != without.Score);
    }

    public static string Describe(JudgeScore s) => new StringBuilder().Append(s.Rubric).Append('=').Append(s.Score?.ToString() ?? s.Status).ToString();
}
