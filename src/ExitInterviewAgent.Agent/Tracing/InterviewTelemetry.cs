using System.Diagnostics;

namespace ExitInterviewAgent.Agent.Tracing;

/// <summary>
/// The agent's trace vocabulary (docs/eval/TRACE-SCHEMA.md). The eval harness reads these names, never prose, so a
/// rename is a breaking change. Spans carry metadata only: counts, flags, enum names and controlled codes. There is
/// deliberately no way to attach free text: <see cref="SpanTags"/> has no <c>string</c> overload.
/// </summary>
public static class InterviewTelemetry
{
    public const string ActivitySourceName = "ExitInterviewAgent.Agent";
    public const string Version = "1.0";

    public static readonly ActivitySource Source = new(ActivitySourceName, Version);

    public static class Spans
    {
        public const string Session = "interview.session";
        public const string Turn = "interview.turn";
        public const string Probe = "interview.probe";
        public const string PiiGuard = "interview.pii_guard";
        public const string Model = "chat";
        public const string Extraction = "interview.extraction";
        public const string QuoteVerification = "interview.quote_verification";
        public const string Validation = "interview.validation";
    }

    public static class Events
    {
        public const string DisclosureDelivered = "interview.disclosure.delivered";
        public const string ConsentGranted = "interview.consent.granted";
        public const string ConsentWithdrawn = "interview.consent.withdrawn";
        public const string TranscriptDiscarded = "interview.transcript.discarded";
        public const string InjectionSuspected = "interview.injection.suspected";
        public const string QuestionRejected = "interview.question.rejected";
        public const string BudgetExhausted = "interview.budget.exhausted";
        public const string NamesMasked = "interview.names.masked";
        public const string QuotesDropped = "interview.quotes.dropped";
    }

    public static class Attr
    {
        // OpenTelemetry GenAI semantic conventions (names as of the 1.37 series; the conventions are still in development).
        public const string OperationName = "gen_ai.operation.name";
        public const string AgentName = "gen_ai.agent.name";
        public const string RequestModel = "gen_ai.request.model";
        public const string UsageInputTokens = "gen_ai.usage.input_tokens";
        public const string UsageOutputTokens = "gen_ai.usage.output_tokens";
        public const string ProviderName = "gen_ai.provider.name";
        public const string ErrorType = "error.type";

        // This repository's own names, for concepts the conventions do not cover.
        public const string ProtocolVersion = "interview.protocol.version";
        public const string Role = "interview.role";
        public const string TurnIndex = "interview.turn.index";
        public const string TurnKind = "interview.turn.kind";
        public const string Topic = "interview.topic";
        public const string Phase = "interview.phase";
        public const string Outcome = "interview.outcome";
        public const string EndReason = "interview.end_reason";
        public const string Turns = "interview.turns";
        public const string ModelCalls = "interview.model_calls";
        public const string TokensEstimated = "interview.tokens.estimated";
        public const string ReplyChars = "interview.reply.chars";
        public const string ReplyWords = "interview.reply.words";
        public const string SignalVague = "interview.signal.vague";
        public const string SignalTerse = "interview.signal.terse";
        public const string SignalHostile = "interview.signal.hostile";
        public const string SignalContradiction = "interview.signal.contradiction";
        public const string SignalWithdrawal = "interview.signal.withdrawal";
        public const string SignalNames = "interview.signal.names_person";
        public const string SignalInjection = "interview.signal.injection_suspected";
        public const string PiiFindings = "interview.pii.findings";
        public const string PiiKinds = "interview.pii.kinds";
        public const string PiiFailClosed = "interview.pii.fail_closed";
        public const string Decision = "interview.decision";
        public const string Probes = "interview.probes";
        public const string Clarifications = "interview.clarifications";
        public const string Redirects = "interview.redirects";
        public const string QuestionsRejected = "interview.questions.rejected";
        public const string Reason = "interview.reason";
        public const string TopicsCovered = "interview.topics.covered";
        public const string SchemaValid = "interview.extraction.schema_valid";
        public const string Attempts = "interview.extraction.attempts";
        public const string QuotesChecked = "interview.quotes.checked";
        public const string QuotesDropped = "interview.quotes.dropped";
        public const string RecordValid = "interview.record.valid";
        public const string ErrorCodes = "interview.validation.error_codes";
        public const string AiDisclosed = "interview.ai_disclosed";
        public const string Submittable = "interview.submittable";
    }
}

/// <summary>
/// Typed writers for span and event tags. Only integers, booleans, enum names and lists of enum names or controlled
/// codes can be written, so interview text cannot reach a trace by construction. A code is checked against a strict
/// pattern and replaced with <c>invalid_code</c> otherwise.
/// </summary>
public static class SpanTags
{
    public static Activity? Set(this Activity? a, string key, int value) => a?.SetTag(key, value);

    public static Activity? Set(this Activity? a, string key, bool value) => a?.SetTag(key, value);

    public static Activity? Set<T>(this Activity? a, string key, T value) where T : struct, Enum => a?.SetTag(key, Snake(value.ToString()));

    public static Activity? Set<T>(this Activity? a, string key, IEnumerable<T> values) where T : struct, Enum =>
        a?.SetTag(key, values.Select(v => Snake(v.ToString())).Distinct().Order(StringComparer.Ordinal).ToArray());

    public static Activity? SetCodes(this Activity? a, string key, IEnumerable<string> codes) =>
        a?.SetTag(key, codes.Select(SafeCode).Distinct().Order(StringComparer.Ordinal).ToArray());

    public static void Event(this Activity? a, string name, params (string Key, object Value)[] tags)
    {
        if (a is null) return;
        var tagList = new ActivityTagsCollection();
        foreach (var (k, v) in tags)
            if (v is int or bool) tagList[k] = v;
            else if (v is Enum e) tagList[k] = Snake(e.ToString());
        a.AddEvent(new ActivityEvent(name, tags: tagList));
    }

    public static string SafeCode(string code) =>
        code.Length is > 0 and <= 48 && code.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-' or ':') ? code : "invalid_code";

    public static string Snake(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]) && !char.IsUpper(pascal[i - 1])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }
}
