using System.Reflection;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Personas;
using ExitInterviewAgent.Providers.Tests.Support;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers.Tests;

[Collection("Environment")]
public class TelemetryTests
{
    private static readonly ChatMessage[] Prompt = [new(ChatRole.User, "hello there")];

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExitInterviewAgent.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> Constants(Type t) =>
        t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

    private static (ProviderChatClient, FakeBackend, string Model) Client(ProviderKind kind, ResilienceOptions? r = null)
    {
        var backend = new FakeBackend(kind);
        var model = $"tel-{Guid.NewGuid():N}"[..16];
        var settings = Settings.For(kind, r) with { Model = model };
        return (ProviderChatClients.Create(settings, new ProviderRuntime { Transport = backend }), backend, model);
    }

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task A_call_emits_one_provider_span_with_genai_attributes_usage_and_finish_reason(ProviderKind kind)
    {
        using var capture = new TelemetryCapture();
        var (client, _, model) = Client(kind);

        await client.GetResponseAsync(Prompt);

        var span = Assert.Single(capture.Activities, a => a.OperationName == ProviderTelemetry.Spans.Call);
        Assert.Equal(ProviderTelemetry.SourceName, span.Source.Name);
        var tags = span.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("chat", tags[ProviderTelemetry.Attr.OperationName]);
        Assert.Equal(ProviderTelemetry.ProviderLabel(kind), tags[ProviderTelemetry.Attr.ProviderName]);
        Assert.Equal(model, tags[ProviderTelemetry.Attr.RequestModel]);
        Assert.Equal("fake-model", tags[ProviderTelemetry.Attr.ResponseModel]);
        Assert.Equal(["stop"], Assert.IsType<string[]>(tags[ProviderTelemetry.Attr.FinishReasons]));
        Assert.IsType<long>(tags[ProviderTelemetry.Attr.UsageInputTokens]);
        Assert.IsType<long>(tags[ProviderTelemetry.Attr.UsageOutputTokens]);
        Assert.Equal(1, tags[ProviderTelemetry.Attr.Attempts]);
        Assert.Equal(false, tags[ProviderTelemetry.Attr.UsageEstimated]);
        Assert.Equal(System.Diagnostics.ActivityKind.Client, span.Kind);
    }

    [Fact]
    public async Task The_provider_span_is_a_child_of_the_agents_chat_span_when_run_inside_an_interview()
    {
        using var capture = new TelemetryCapture();
        var (client, _, _) = Client(ProviderKind.Ollama);

        await PersonaSession.RunAsync(PersonaCatalog.Get("talkative"), 1, client);

        var providerSpans = capture.Activities.Where(a => a.OperationName == ProviderTelemetry.Spans.Call).ToList();
        Assert.NotEmpty(providerSpans);
        Assert.All(providerSpans, p => Assert.StartsWith("chat", capture.Activities.Single(a => a.Id == p.ParentId).OperationName, StringComparison.Ordinal));
        Assert.All(providerSpans, p => Assert.Equal(InterviewTelemetry.ActivitySourceName, capture.Activities.Single(a => a.Id == p.ParentId).Source.Name));
    }

    [Fact]
    public async Task A_retry_is_an_event_with_ints_only_and_a_counter_with_a_closed_reason_set()
    {
        using var capture = new TelemetryCapture();
        var time = new ManualTime();
        var backend = new FakeBackend(ProviderKind.Anthropic);
        var model = $"tel-{Guid.NewGuid():N}"[..16];
        using var client = ProviderChatClients.Create(Settings.For(ProviderKind.Anthropic) with { Model = model }, new ProviderRuntime { Transport = backend, Time = time, Jitter = () => 0 });
        backend.Script = (n, seen) => Task.FromResult<HttpResponseMessage?>(n == 1 ? backend.ErrorEchoing(System.Net.HttpStatusCode.TooManyRequests, seen) : null);

        await time.Drive(client.GetResponseAsync(Prompt));

        var span = capture.Activities.Single(a => a.OperationName == ProviderTelemetry.Spans.Call && (string?)a.GetTagItem(ProviderTelemetry.Attr.RequestModel) == model);
        var retry = Assert.Single(span.Events, e => e.Name == ProviderTelemetry.Events.Retry);
        Assert.All(retry.Tags, t => Assert.IsType<int>(t.Value));
        Assert.Equal(2, span.GetTagItem(ProviderTelemetry.Attr.Attempts));
        Assert.Contains(capture.Measurements, m => m.Instrument == ProviderTelemetry.Metrics.Retries && m.Tags[ProviderTelemetry.Attr.RetryReason] == "429");
    }

    [Fact]
    public async Task A_failed_call_sets_error_status_and_a_controlled_error_type_and_never_a_message()
    {
        using var capture = new TelemetryCapture();
        var (client, backend, model) = Client(ProviderKind.OpenAiCompatible);
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(System.Net.HttpStatusCode.Unauthorized, seen));

        await Assert.ThrowsAsync<ProviderException>(() => client.GetResponseAsync(Prompt));

        var span = capture.Activities.Single(a => a.OperationName == ProviderTelemetry.Spans.Call && (string?)a.GetTagItem(ProviderTelemetry.Attr.RequestModel) == model);
        Assert.Equal(System.Diagnostics.ActivityStatusCode.Error, span.Status);
        Assert.Equal("provider_call_failed", span.StatusDescription);
        Assert.Equal("provider.auth_failed", span.GetTagItem(ProviderTelemetry.Attr.ErrorType));
        Assert.Equal(401, span.GetTagItem(ProviderTelemetry.Attr.HttpStatusCode));
    }

    [Fact]
    public async Task Metrics_use_the_genai_names_and_only_low_cardinality_labels()
    {
        using var capture = new TelemetryCapture();
        var (ok, _, model) = Client(ProviderKind.Anthropic);
        var (bad, backend, badModel) = Client(ProviderKind.Ollama, new ResilienceOptions { MaxRetries = 0 });
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(System.Net.HttpStatusCode.BadGateway, seen));

        await ok.GetResponseAsync(Prompt);
        await Assert.ThrowsAsync<ProviderException>(() => bad.GetResponseAsync(Prompt));

        var mine = capture.Measurements.Where(m => m.Tags.GetValueOrDefault(ProviderTelemetry.Attr.RequestModel) is { } rm && (rm == model || rm == badModel)).ToList();
        Assert.Contains(mine, m => m.Instrument == ProviderTelemetry.Metrics.OperationDuration && m.Tags[ProviderTelemetry.Attr.ProviderName] == "anthropic" && !m.Tags.ContainsKey(ProviderTelemetry.Attr.ErrorType));
        Assert.Contains(mine, m => m.Instrument == ProviderTelemetry.Metrics.OperationDuration && m.Tags.GetValueOrDefault(ProviderTelemetry.Attr.ErrorType) == "provider.server_error");
        Assert.Contains(mine, m => m.Instrument == ProviderTelemetry.Metrics.TokenUsage && m.Tags[ProviderTelemetry.Attr.TokenType] == "input");
        Assert.Contains(mine, m => m.Instrument == ProviderTelemetry.Metrics.TokenUsage && m.Tags[ProviderTelemetry.Attr.TokenType] == "output");

        var allowedKeys = new[] { ProviderTelemetry.Attr.OperationName, ProviderTelemetry.Attr.ProviderName, ProviderTelemetry.Attr.RequestModel, ProviderTelemetry.Attr.ErrorType, ProviderTelemetry.Attr.TokenType, ProviderTelemetry.Attr.RetryReason, ProviderTelemetry.Attr.Limit };
        Assert.All(capture.Measurements, m => Assert.All(m.Tags.Keys, k => Assert.Contains(k, allowedKeys)));
        Assert.All(capture.Measurements, m => Assert.All(m.Tags, t => Assert.True(t.Value.Length <= 48, $"{t.Key} label too long")));
        var providers = capture.Measurements.Select(m => m.Tags.GetValueOrDefault(ProviderTelemetry.Attr.ProviderName)).Where(v => v is not null).ToHashSet();
        Assert.Subset(new HashSet<string?> { "anthropic", "openai_compatible", "ollama", "mock" }, providers);
    }

    [Fact]
    public void The_metric_instruments_carry_the_documented_names_and_units()
    {
        Assert.Equal("gen_ai.client.operation.duration", ProviderTelemetry.OperationDuration.Name);
        Assert.Equal("s", ProviderTelemetry.OperationDuration.Unit);
        Assert.Equal("gen_ai.client.token.usage", ProviderTelemetry.TokenUsage.Name);
        Assert.Equal("{token}", ProviderTelemetry.TokenUsage.Unit);
        Assert.Equal("ExitInterviewAgent.Providers", ProviderTelemetry.MeterName);
        Assert.Equal("ExitInterviewAgent.Providers", ProviderTelemetry.SourceName);
    }

    [Theory]
    [InlineData("claude-3/with space\nnewline", "claude-3_with_space_newline")]
    [InlineData("llama3.2:3b", "llama3.2:3b")]
    [InlineData("", "unknown")]
    public void Labels_are_sanitised_not_echoed_raw(string raw, string expected) => Assert.Equal(expected, ProviderTelemetry.Label(raw));

    [Fact]
    public void A_label_is_at_most_48_characters()
    {
        Assert.Equal(48, ProviderTelemetry.Label(new string('a', 500)).Length);
    }

    // ---- no content capture: not present, not switchable ---------------------------------------------------------

    [Fact]
    public void No_member_of_the_providers_assembly_offers_to_capture_prompt_or_completion_text()
    {
        var banned = new[] { "capture", "sensitive", "recordcontent", "includecontent", "logcontent", "logprompt", "includeprompt", "recordprompt", "messagecontent" };
        var names = typeof(ProviderChatClient).Assembly.GetTypes()
            .SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name)))
            .Where(n => !n.StartsWith('<'))
            .ToList();

        Assert.DoesNotContain(names, n => banned.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_providers_source_reads_no_environment_variable_at_all_so_no_switch_can_be_hidden_there()
    {
        var sources = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "ExitInterviewAgent.Providers"), "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(sources);
        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("GetEnvironmentVariable", text);
            Assert.DoesNotContain("CAPTURE_MESSAGE_CONTENT", text);
            Assert.DoesNotContain("EnableSensitiveData", text);
            Assert.DoesNotContain("OpenTelemetryChatClient", text);
            Assert.DoesNotContain("UseOpenTelemetry", text);
        }
    }

    [Fact]
    public void The_only_string_a_provider_tag_can_take_is_a_sanitised_code()
    {
        var tagMethods = typeof(ProviderTelemetry).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "Tag" or "Code").ToList();

        var stringOverloads = tagMethods.Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(string) && p.Position == 2)).ToList();
        Assert.Equal(["Code"], stringOverloads.Select(m => m.Name).Distinct());
        Assert.DoesNotContain(tagMethods, m => m.Name == "Tag" && m.GetParameters().Any(p => p.Position == 2 && p.ParameterType == typeof(string)));
    }

    // ---- drift between code and docs/eval/TRACE-SCHEMA.md ---------------------------------------------------------

    [Fact]
    public void The_trace_schema_document_lists_every_provider_span_event_attribute_and_metric_name()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "eval", "TRACE-SCHEMA.md"));

        var names = Constants(typeof(ProviderTelemetry.Spans)).Concat(Constants(typeof(ProviderTelemetry.Events))).Concat(Constants(typeof(ProviderTelemetry.Attr))).Concat(Constants(typeof(ProviderTelemetry.Metrics)));
        foreach (var name in names)
            Assert.True(doc.Contains($"`{name}`", StringComparison.Ordinal), $"docs/eval/TRACE-SCHEMA.md does not document `{name}`");
        Assert.Contains($"`{ProviderTelemetry.SourceName}`", doc);
        Assert.Contains($"`{ProviderTelemetry.MeterName}`", doc);
        Assert.True(doc.Contains($"`{InterviewTelemetry.Attr.ErrorType}`", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_span_attribute_and_event_a_provider_run_emits_is_a_documented_constant()
    {
        using var capture = new TelemetryCapture();
        var time = new ManualTime();
        var backend = new FakeBackend(ProviderKind.OpenAiCompatible);
        using var client = ProviderChatClients.Create(Settings.For(ProviderKind.OpenAiCompatible), new ProviderRuntime { Transport = backend, Time = time });
        backend.Script = (n, seen) => Task.FromResult<HttpResponseMessage?>(n == 1 ? backend.ErrorEchoing(System.Net.HttpStatusCode.ServiceUnavailable, seen) : null);
        await time.Drive(client.GetResponseAsync(Prompt));
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(System.Net.HttpStatusCode.Unauthorized, seen));
        await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt)));

        var own = capture.Activities.Where(a => a.Source.Name == ProviderTelemetry.SourceName).ToList();
        Assert.NotEmpty(own);
        var attrs = Constants(typeof(ProviderTelemetry.Attr)).ToHashSet();
        Assert.Subset(attrs, own.SelectMany(a => a.TagObjects.Select(t => t.Key)).ToHashSet());
        Assert.Subset(Constants(typeof(ProviderTelemetry.Events)).ToHashSet(), own.SelectMany(a => a.Events.Select(e => e.Name)).ToHashSet());
        Assert.Subset(Constants(typeof(ProviderTelemetry.Spans)).ToHashSet(), own.Select(a => a.OperationName).ToHashSet());
        Assert.Subset(Constants(typeof(ProviderTelemetry.Metrics)).ToHashSet(), capture.Measurements.Select(m => m.Instrument).ToHashSet());
    }

    [Fact]
    public async Task The_agents_chat_span_carries_the_providers_controlled_error_code_on_a_fatal_failure()
    {
        using var capture = new TelemetryCapture();
        var (client, backend, _) = Client(ProviderKind.Anthropic);
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(System.Net.HttpStatusCode.Unauthorized, seen));

        var ex = await Assert.ThrowsAsync<ModelCallFailedException>(() => PersonaSession.RunAsync(PersonaCatalog.Get("talkative"), 1, client));

        Assert.True(ex.IsFatal);
        Assert.Equal("provider.auth_failed", ex.Code);
        Assert.Contains(capture.Activities, a => a.Source.Name == InterviewTelemetry.ActivitySourceName && a.GetTagItem(InterviewTelemetry.Attr.ErrorType) as string == "provider.auth_failed");
    }
}
