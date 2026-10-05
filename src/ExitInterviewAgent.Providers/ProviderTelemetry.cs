using System.Diagnostics;
using System.Diagnostics.Metrics;
using ExitInterviewAgent.Agent.Tracing;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers;

/// <summary>
/// The providers' trace and metric vocabulary (docs/eval/TRACE-SCHEMA.md, "Provider layer"). Same rule as the agent's
/// ([ADR-0025](../../docs/adr/0025-trace-schema-metadata-only.md)): counts, flags, enum names and controlled codes only.
/// <b>There is no way to record prompt or completion text</b>: no switch, no option, no environment variable is read for it
/// (the standard GenAI content-capture environment switch is deliberately ignored), and the only string a tag can
/// take goes through <see cref="Label"/>. Metric labels are low-cardinality: provider (3 values), operation, token type, error kind.
/// </summary>
public static class ProviderTelemetry
{
    public const string SourceName = "ExitInterviewAgent.Providers";
    public const string MeterName = "ExitInterviewAgent.Providers";
    public const string Version = "1.0";

    public static readonly ActivitySource Source = new(SourceName, Version);
    public static readonly Meter Meter = new(MeterName, Version);

    public static class Spans
    {
        public const string Call = "provider.call";
    }

    public static class Events
    {
        public const string Retry = "provider.retry";
        public const string BudgetExceeded = "provider.budget.exceeded";
    }

    public static class Attr
    {
        // OpenTelemetry GenAI semantic conventions (1.37 series names, pinned) and HTTP conventions.
        public const string OperationName = "gen_ai.operation.name";
        public const string ProviderName = "gen_ai.provider.name";
        public const string RequestModel = "gen_ai.request.model";
        public const string ResponseModel = "gen_ai.response.model";
        public const string FinishReasons = "gen_ai.response.finish_reasons";
        public const string UsageInputTokens = "gen_ai.usage.input_tokens";
        public const string UsageOutputTokens = "gen_ai.usage.output_tokens";
        public const string TokenType = "gen_ai.token.type";
        public const string ErrorType = "error.type";
        public const string HttpStatusCode = "http.response.status_code";

        // This repository's own names.
        public const string Attempts = "exit_interview.provider.attempts";
        public const string UsageEstimated = "exit_interview.provider.usage_estimated";
        public const string RetryAttempt = "exit_interview.provider.retry.attempt";
        public const string RetryReason = "exit_interview.provider.retry.reason";
        public const string Limit = "exit_interview.provider.budget.limit";
    }

    public static class Metrics
    {
        public const string OperationDuration = "gen_ai.client.operation.duration";
        public const string TokenUsage = "gen_ai.client.token.usage";
        public const string Retries = "exit_interview.provider.retries";
        public const string BudgetExceeded = "exit_interview.provider.budget_exceeded";
    }

    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(Metrics.OperationDuration, "s", "Duration of one model call, retries included.");
    public static readonly Histogram<long> TokenUsage = Meter.CreateHistogram<long>(Metrics.TokenUsage, "{token}", "Tokens used per model call, as reported by the provider (or estimated).");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>(Metrics.Retries, "{retry}", "Retried HTTP attempts.");
    public static readonly Counter<long> BudgetExceeded = Meter.CreateCounter<long>(Metrics.BudgetExceeded, "{event}", "Calls refused because the interview budget was reached.");

    /// <summary>Provider ids as used in <c>gen_ai.provider.name</c>. Custom values are allowed by the conventions where no well-known one fits.</summary>
    public static string ProviderLabel(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => "anthropic",
        ProviderKind.OpenAiCompatible => "openai_compatible",
        ProviderKind.Ollama => "ollama",
        _ => "mock",
    };

    /// <summary>A user-chosen identifier (a model id) made safe for a tag: at most 48 characters of <c>[A-Za-z0-9_.:-]</c>, anything else becomes <c>_</c>.</summary>
    public static string Label(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "unknown";
        var chars = value.Take(48).Select(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-' or ':' ? c : '_').ToArray();
        return new string(chars);
    }

    /// <summary>Finish reasons are mapped to a closed set, so a provider cannot put free text in a tag.</summary>
    public static string FinishReasonCode(ChatFinishReason? reason) => reason?.Value switch
    {
        null => "none",
        "stop" => "stop",
        "length" => "length",
        "tool_calls" => "tool_calls",
        "content_filter" => "content_filter",
        _ => "other",
    };

    /// <summary>The only string tag: a controlled code, passed through <see cref="Label"/> and <see cref="SpanTags.SafeCode"/>.</summary>
    public static Activity? Code(this Activity? a, string key, string value) => a?.SetTag(key, SpanTags.SafeCode(Label(value)));

    public static Activity? Tag(this Activity? a, string key, long value) => a?.SetTag(key, value);

    public static Activity? Tag(this Activity? a, string key, int value) => a?.SetTag(key, value);

    public static Activity? Tag(this Activity? a, string key, bool value) => a?.SetTag(key, value);
}
