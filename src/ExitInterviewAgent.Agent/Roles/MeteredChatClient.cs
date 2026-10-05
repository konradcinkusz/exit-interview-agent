using System.Diagnostics;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Tracing;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Agent.Roles;

/// <summary>Counts model calls and tokens against the protocol's budget. Token counts come from the provider when it reports them, otherwise a four-characters-per-token estimate.</summary>
public sealed class ModelMeter(ProtocolLimits limits)
{
    private int _calls;
    private long _tokens;

    public int Calls => _calls;
    public long Tokens => Interlocked.Read(ref _tokens);
    public bool Exhausted => Calls >= limits.MaxModelCalls || Tokens >= limits.MaxEstimatedTokens;

    internal void Add(long tokens)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Add(ref _tokens, tokens);
    }

    public static long Estimate(string? text) => ((text?.Length ?? 0) + 3) / 4;
}

/// <summary>
/// Wraps any <see cref="IChatClient"/>: one <c>chat</c> span per call with GenAI usage attributes and the role, and
/// the meter update. It never reads message text into a span.
/// </summary>
public sealed class MeteredChatClient(IChatClient inner, ModelMeter meter) : DelegatingChatClient(inner)
{
    public const string RoleKey = "interview.role";

    public static ChatOptions Options(Role role, int maxOutputTokens, float temperature = 0.2f) => new()
    {
        MaxOutputTokens = maxOutputTokens,
        Temperature = temperature,
        AdditionalProperties = new AdditionalPropertiesDictionary { [RoleKey] = role.ToString() },
    };

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var role = options?.AdditionalProperties?.TryGetValue(RoleKey, out var r) == true && Enum.TryParse<Role>(r?.ToString(), out var parsed) ? parsed : Role.Interviewer;
        var metadata = InnerClient.GetService<ChatClientMetadata>();

        using var span = InterviewTelemetry.Source.StartActivity($"{InterviewTelemetry.Spans.Model} {role.ToString().ToLowerInvariant()}", ActivityKind.Client);
        span?.SetTag(InterviewTelemetry.Attr.OperationName, "chat");
        span.Set(InterviewTelemetry.Attr.Role, role);
        if (metadata?.ProviderName is { } provider) span?.SetTag(InterviewTelemetry.Attr.ProviderName, SpanTags.SafeCode(provider));
        if (metadata?.DefaultModelId is { } model) span?.SetTag(InterviewTelemetry.Attr.RequestModel, SpanTags.SafeCode(model));

        var inputEstimate = list.Sum(m => ModelMeter.Estimate(m.Text));
        try
        {
            var response = await base.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);
            var input = response.Usage?.InputTokenCount ?? inputEstimate;
            var output = response.Usage?.OutputTokenCount ?? ModelMeter.Estimate(response.Text);
            meter.Add(input + output);
            span.Set(InterviewTelemetry.Attr.UsageInputTokens, (int)input).Set(InterviewTelemetry.Attr.UsageOutputTokens, (int)output);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            meter.Add(inputEstimate);
            span?.SetStatus(ActivityStatusCode.Error, "model_call_failed");
            var failure = ex as IModelFailure;
            var code = failure?.FailureCode ?? ex.GetType().Name;
            span?.SetTag(InterviewTelemetry.Attr.ErrorType, SpanTags.SafeCode(code));
            throw new ModelCallFailedException(code, failure?.IsFatal ?? false);
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The interview agent uses non-streaming calls so that every call is metered.");
}

/// <summary>
/// A model call failed. Carries a controlled code (the exception type name, or the code of an <see cref="IModelFailure"/>) only:
/// provider messages can echo the prompt. <see cref="IsFatal"/> failures (bad credentials, a spent budget) end the interview
/// instead of degrading to the protocol's own wording.
/// </summary>
public sealed class ModelCallFailedException(string innerType, bool isFatal = false) : Exception($"The model call failed ({SpanTags.SafeCode(innerType)}).")
{
    public bool IsFatal { get; } = isFatal;

    public string Code { get; } = SpanTags.SafeCode(innerType);
}
