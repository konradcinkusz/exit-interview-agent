using System.Net;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Personas;
using ExitInterviewAgent.Providers.Tests.Support;

namespace ExitInterviewAgent.Providers.Tests;

/// <summary>
/// T4's canary test extended to every provider client. A marker is planted in the interviewee's words (so it travels in prompts),
/// the key, a response header, a base-URL path and a provider error body that echoes the request. None may appear in any span,
/// event, tag, metric label, log line or exception, from any ActivitySource in the process (the HTTP stack's and the SDKs' included).
/// Each test first proves it has power: the marker really reached the fake provider and the key really went out.
/// </summary>
[Collection("Environment")]
public class CanaryTests
{
    private const string PromptCanary = "zebracanary7391qx";
    private const string KeyCanary = "sk-KEYCANARY-5528wq-abcdef";
    private const string HeaderCanary = "hdrcanary9917zt";
    private const string PathCanary = "pathcanary4410mm";
    private const string ErrorBodyCanary = "bodycanary6603vv";

    public static IEnumerable<object[]> KindsAndPersonas() =>
        Settings.RealKinds.SelectMany(k => PersonaCatalog.All.Select(p => new object[] { k, p.Id }));

    private static ProviderSettings SettingsFor(ProviderKind kind, string model) => new()
    {
        Kind = kind,
        Model = model,
        BaseUrl = new Uri(kind switch
        {
            ProviderKind.Anthropic => $"https://gateway.example.test/{PathCanary}",
            ProviderKind.OpenAiCompatible => $"https://gateway.example.test/{PathCanary}/v1",
            _ => $"http://localhost:11434/{PathCanary}",
        }),
        ApiKey = kind == ProviderKind.Ollama ? null : new SecretString(KeyCanary),
        // Real clock, no waiting: the retry policy is exercised, the backoff is zero.
        Resilience = new ResilienceOptions { MaxRetries = 1, BaseDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero },
    };

    private static IEnumerable<string> Chain(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            yield return e.GetType().FullName ?? string.Empty;
            yield return e.Message;
            yield return e.ToString();
            yield return e.StackTrace ?? string.Empty;
            foreach (var k in e.Data.Keys) yield return $"{k}={e.Data[k]}";
        }
    }

    private static void AssertNoLeak(IEnumerable<string> haystack, params string[] needles)
    {
        var all = haystack.ToList();
        foreach (var needle in needles)
            Assert.DoesNotContain(all, s => s.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(KindsAndPersonas))]
    public async Task A_whole_interview_through_a_provider_client_leaks_nothing_into_any_span_metric_log_or_header(ProviderKind kind, string personaId)
    {
        var persona = PersonaCatalog.Get(personaId);
        var model = $"canary-{Guid.NewGuid():N}"[..20];
        var backend = new FakeBackend(kind);
        backend.ResponseHeaders["x-request-id"] = HeaderCanary;
        var log = new ListLogger();
        using var capture = new TelemetryCapture();
        using var client = ProviderChatClients.Create(SettingsFor(kind, model), new ProviderRuntime { Transport = backend, Logger = log });

        var result = await PersonaSession.RunAsync(persona, 1, client, decorate: r => r + " " + PromptCanary, logger: log);

        if (personaId == "talkative") Assert.True(result.Submittable, "a full interview through the adapter should end with a valid record");
        // Power: the interview really went through the adapter, the canary really travelled in prompts, the key really went out.
        Assert.True(backend.Calls > 0 || result.Outcome is InterviewOutcome.Withdrawn or InterviewOutcome.ConsentNotGiven or InterviewOutcome.Abandoned, "no model call and no early stop");
        if (backend.Calls > 0)
        {
            Assert.Contains(backend.Seen, s => s.Body.Contains(PromptCanary, StringComparison.Ordinal));
            if (kind != ProviderKind.Ollama) Assert.Contains(backend.Seen, s => s.Headers.Values.Any(v => v.Contains(KeyCanary, StringComparison.Ordinal)));
            Assert.Contains(capture.Activities, a => a.OperationName == ProviderTelemetry.Spans.Call && a.GetTagItem(ProviderTelemetry.Attr.RequestModel) as string == model);
            Assert.Contains(capture.Measurements, m => m.Instrument == ProviderTelemetry.Metrics.TokenUsage && m.Tags.GetValueOrDefault(ProviderTelemetry.Attr.RequestModel) == model);
            Assert.NotEmpty(log.Lines);
        }

        string[] needles = [PromptCanary, KeyCanary, HeaderCanary, ErrorBodyCanary, .. persona.Planted];
        AssertNoLeak(capture.AllStrings(), needles);
        AssertNoLeak(log.Lines, needles);
        // The base URL path is the user's own gateway layout: absent from everything this project emits (the HTTP stack's own spans, which this project does not export, carry it).
        AssertNoLeak(capture.AllStrings(ownSourcesOnly: true), PathCanary);
        AssertNoLeak(log.Lines, PathCanary);
    }

    [Theory]
    [InlineData(ProviderKind.Anthropic, 401)]
    [InlineData(ProviderKind.OpenAiCompatible, 401)]
    [InlineData(ProviderKind.Ollama, 404)]
    [InlineData(ProviderKind.Anthropic, 500)]
    [InlineData(ProviderKind.OpenAiCompatible, 429)]
    [InlineData(ProviderKind.Ollama, 503)]
    public async Task A_failing_provider_that_echoes_the_request_leaks_nothing_into_exceptions_spans_metrics_or_logs(ProviderKind kind, int status)
    {
        var persona = PersonaCatalog.Get("talkative");
        var backend = new FakeBackend(kind);
        backend.ResponseHeaders["x-request-id"] = HeaderCanary;
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.Json((HttpStatusCode)status,
            $$"""{ "error": { "type": "x", "message": "{{ErrorBodyCanary}} echo: {{System.Text.Json.JsonEncodedText.Encode(seen.Body)}} key {{KeyCanary}}" } }"""));
        var log = new ListLogger();
        using var capture = new TelemetryCapture();
        using var client = ProviderChatClients.Create(SettingsFor(kind, $"canary-{Guid.NewGuid():N}"[..20]), new ProviderRuntime { Transport = backend, Logger = log });

        var thrown = await Record.ExceptionAsync(() => PersonaSession.RunAsync(persona, 1, client, decorate: r => r + " " + PromptCanary, logger: log));

        // Every one of these failures ends the interview: fatal on the first call (401, 404), or after repeated failures (5xx, 429).
        var failure = Assert.IsType<ModelCallFailedException>(thrown);
        Assert.True(failure.IsFatal);
        Assert.StartsWith("provider.", failure.Code);
        Assert.True(backend.Calls > 0);
        Assert.Contains(backend.Seen, s => s.Body.Contains(PromptCanary, StringComparison.Ordinal) || s.Body.Length > 0);

        var needles = new[] { PromptCanary, KeyCanary, HeaderCanary, ErrorBodyCanary, "echo:" };
        AssertNoLeak(Chain(thrown), needles);
        AssertNoLeak(capture.AllStrings(), needles);
        AssertNoLeak(log.Lines, needles);
        AssertNoLeak(capture.AllStrings(ownSourcesOnly: true), PathCanary);
        AssertNoLeak(Chain(thrown), PathCanary);
        Assert.Contains(capture.Activities, a => a.OperationName == ProviderTelemetry.Spans.Call && a.GetTagItem(ProviderTelemetry.Attr.ErrorType) is string);
    }

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task The_standard_content_capture_switches_do_nothing_because_there_is_no_content_capture(ProviderKind kind)
    {
        var switches = new[] { "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "OPENAI_EXPERIMENTAL_ENABLE_OPEN_TELEMETRY", "OTEL_DOTNET_EXPERIMENTAL_GENAI_CAPTURE_CONTENT" };
        var previous = switches.ToDictionary(s => s, Environment.GetEnvironmentVariable);
        foreach (var s in switches) Environment.SetEnvironmentVariable(s, "true");
        try
        {
            var backend = new FakeBackend(kind);
            using var capture = new TelemetryCapture();
            using var client = ProviderChatClients.Create(SettingsFor(kind, $"canary-{Guid.NewGuid():N}"[..20]), new ProviderRuntime { Transport = backend });

            await PersonaSession.RunAsync(PersonaCatalog.Get("talkative"), 1, client, decorate: r => r + " " + PromptCanary);

            Assert.Contains(backend.Seen, s => s.Body.Contains(PromptCanary, StringComparison.Ordinal));
            AssertNoLeak(capture.AllStrings(), PromptCanary, KeyCanary);
            var keys = capture.Activities.SelectMany(a => a.TagObjects.Select(t => t.Key)).ToHashSet();
            foreach (var forbidden in new[] { "gen_ai.input.messages", "gen_ai.output.messages", "gen_ai.system_instructions", "gen_ai.prompt", "gen_ai.completion", "gen_ai.request.messages" })
                Assert.DoesNotContain(forbidden, keys);
        }
        finally { foreach (var (k, v) in previous) Environment.SetEnvironmentVariable(k, v); }
    }

    [Fact]
    public async Task The_scan_can_fail_a_span_that_leaks_a_marker_is_caught()
    {
        using var capture = new TelemetryCapture();
        using (var span = ProviderTelemetry.Source.StartActivity("leaky"))
        {
            span!.SetTag("gen_ai.input.messages", "said " + PromptCanary);
        }
        ProviderTelemetry.Retries.Add(1, new KeyValuePair<string, object?>("leak", PromptCanary));

        await Task.CompletedTask;
        Assert.Contains(capture.AllStrings(), s => s.Contains(PromptCanary, StringComparison.Ordinal));
        Assert.Contains(capture.Measurements, m => m.Tags.Values.Contains(PromptCanary));
    }
}
