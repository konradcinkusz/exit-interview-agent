using System.Net;
using System.Text.Json;
using ExitInterviewAgent.Providers.Tests.Support;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers.Tests;

[Collection("Environment")]
public class AdapterTests
{

    private static ChatMessage[] Prompt() => [new(ChatRole.System, "You are a test."), new(ChatRole.User, "hello there")];

    private static (ProviderChatClient Client, FakeBackend Backend, ManualTime Time) Build(ProviderKind kind, ResilienceOptions? resilience = null, Func<double>? jitter = null)
    {
        var backend = new FakeBackend(kind);
        var time = new ManualTime();
        var client = ProviderChatClients.Create(Settings.For(kind, resilience), new ProviderRuntime { Transport = backend, Time = time, Jitter = jitter ?? (() => 0.5) });
        return (client, backend, time);
    }

    private static ChatOptions Options => new() { Temperature = 0.2f, MaxOutputTokens = 123 };

    [Fact]
    public async Task A_workspace_id_is_sent_to_anthropic_as_a_header_and_nowhere_else()
    {
        var backend = new FakeBackend(ProviderKind.Anthropic);
        var time = new ManualTime();
        var client = ProviderChatClients.Create(Settings.For(ProviderKind.Anthropic) with { WorkspaceId = "wrkspc_01ABC-test" }, new ProviderRuntime { Transport = backend, Time = time, Jitter = () => 0.5 });

        await time.Drive(client.GetResponseAsync(Prompt(), Options));

        var seen = Assert.Single(backend.Seen);
        Assert.Equal("wrkspc_01ABC-test", seen.Headers["anthropic-workspace-id"]);
        Assert.DoesNotContain("wrkspc_01ABC-test", seen.Body);
    }

    [Fact]
    public async Task Without_a_workspace_id_no_workspace_header_is_sent()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic);

        await time.Drive(client.GetResponseAsync(Prompt(), Options));

        Assert.False(Assert.Single(backend.Seen).Headers.ContainsKey("anthropic-workspace-id"));
    }

    [Fact]
    public async Task The_anthropic_request_carries_no_sampling_parameters_because_current_models_reject_them()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic);
        var options = new ChatOptions { Temperature = 0.2f, TopP = 0.9f, TopK = 5, MaxOutputTokens = 123 };

        await time.Drive(client.GetResponseAsync(Prompt(), options));

        var body = Assert.Single(backend.Seen).Body;
        Assert.DoesNotContain("\"temperature\"", body);
        Assert.DoesNotContain("\"top_p\"", body);
        Assert.DoesNotContain("\"top_k\"", body);
        Assert.Contains("123", body);
        Assert.Equal(0.2f, options.Temperature);
    }

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task A_call_returns_text_usage_and_finish_reason_and_is_not_streamed(ProviderKind kind)
    {
        var (client, backend, time) = Build(kind);

        var response = await time.Drive(client.GetResponseAsync(Prompt(), Options));

        Assert.False(string.IsNullOrEmpty(response.ModelId));
        Assert.NotNull(response.Usage?.InputTokenCount);
        Assert.NotNull(response.Usage?.OutputTokenCount);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        var seen = Assert.Single(backend.Seen);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.DoesNotContain("\"stream\":true", seen.Body.Replace(" ", string.Empty));
        Assert.Contains("hello there", seen.Body);
        Assert.Throws<NotSupportedException>(() => client.GetStreamingResponseAsync(Prompt()));
    }

    [Theory]
    [InlineData(ProviderKind.Anthropic, "/v1/messages")]
    [InlineData(ProviderKind.OpenAiCompatible, "/v1/chat/completions")]
    [InlineData(ProviderKind.Ollama, "/api/chat")]
    public async Task The_request_goes_to_the_documented_path_with_the_model_and_the_limits(ProviderKind kind, string path)
    {
        var (client, backend, time) = Build(kind);

        await time.Drive(client.GetResponseAsync(Prompt(), Options));

        var seen = backend.Seen.Single();
        Assert.EndsWith(path, seen.Path);
        using var doc = JsonDocument.Parse(seen.Body);
        Assert.Equal(Settings.For(kind).Model, doc.RootElement.GetProperty("model").GetString());
        var limitProperty = kind switch { ProviderKind.Anthropic => "max_tokens", ProviderKind.OpenAiCompatible => "max_completion_tokens", _ => null };
        if (limitProperty is not null) Assert.Equal(123, doc.RootElement.GetProperty(limitProperty).GetInt32());
        else Assert.Equal(123, doc.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        if (kind == ProviderKind.Ollama) Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task Anthropic_sends_the_key_only_in_x_api_key_and_never_forwards_an_environment_bearer_token()
    {
        // The SDK itself reads ANTHROPIC_AUTH_TOKEN and would add "Authorization: Bearer <it>"; a subscription token must never go out that way.
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "bearer-from-the-environment");
        try
        {
            var (client, backend, time) = Build(ProviderKind.Anthropic);

            await time.Drive(client.GetResponseAsync(Prompt(), Options));

            var seen = backend.Seen.Single();
            Assert.Equal(Settings.Key, seen.Headers["x-api-key"]);
            Assert.False(seen.Headers.ContainsKey("Authorization"));
            Assert.DoesNotContain("bearer-from-the-environment", string.Join("|", seen.Headers.Values) + seen.Body);
        }
        finally { Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previous); }
    }

    [Fact]
    public async Task OpenAI_compatible_sends_the_key_only_as_a_bearer_token()
    {
        var (client, backend, time) = Build(ProviderKind.OpenAiCompatible);

        await time.Drive(client.GetResponseAsync(Prompt(), Options));

        var seen = backend.Seen.Single();
        Assert.Equal("Bearer " + Settings.Key, seen.Headers["Authorization"]);
        Assert.False(seen.Headers.ContainsKey("x-api-key"));
    }

    [Fact]
    public async Task A_keyless_gateway_and_a_local_ollama_send_no_credential_header_at_all()
    {
        foreach (var kind in new[] { ProviderKind.OpenAiCompatible, ProviderKind.Ollama })
        {
            var backend = new FakeBackend(kind);
            var settings = Settings.For(kind) with { ApiKey = null, BaseUrl = new Uri("http://localhost:1234/v1") };
            using var client = ProviderChatClients.Create(settings, new ProviderRuntime { Transport = backend });

            await client.GetResponseAsync(Prompt(), Options);

            var seen = backend.Seen.Single();
            Assert.False(seen.Headers.ContainsKey("Authorization"), kind.ToString());
            Assert.False(seen.Headers.ContainsKey("x-api-key"), kind.ToString());
        }
    }

    [Fact]
    public async Task Ollama_with_a_key_sends_it_as_a_bearer_token_and_the_context_window_when_set()
    {
        var backend = new FakeBackend(ProviderKind.Ollama);
        var settings = Settings.For(ProviderKind.Ollama) with { ApiKey = new SecretString(Settings.Key), NumCtx = 8192 };
        using var client = ProviderChatClients.Create(settings, new ProviderRuntime { Transport = backend });

        await client.GetResponseAsync(Prompt(), Options);

        var seen = backend.Seen.Single();
        Assert.Equal("Bearer " + Settings.Key, seen.Headers["Authorization"]);
        using var doc = JsonDocument.Parse(seen.Body);
        Assert.Equal(8192, doc.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task Ollama_asks_for_json_only_when_the_caller_asks_for_json()
    {
        var backend = new FakeBackend(ProviderKind.Ollama);
        using var client = ProviderChatClients.Create(Settings.For(ProviderKind.Ollama), new ProviderRuntime { Transport = backend });

        await client.GetResponseAsync(Prompt(), new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
        await client.GetResponseAsync(Prompt(), new ChatOptions());

        Assert.Contains("\"format\":\"json\"", backend.Seen[0].Body.Replace(" ", string.Empty));
        Assert.DoesNotContain("\"format\"", backend.Seen[1].Body);
    }

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task The_client_reports_the_provider_and_model_through_the_metadata_the_agent_traces(ProviderKind kind)
    {
        var (client, _, _) = Build(kind);

        var metadata = client.GetService<ChatClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal(ProviderTelemetry.ProviderLabel(kind), metadata!.ProviderName);
        Assert.Equal(ProviderTelemetry.Label(Settings.For(kind).Model), metadata.DefaultModelId);
        Assert.Null(metadata.ProviderUri);
    }

    // ---- error mapping -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ProviderKind.Anthropic, 401, ProviderFailureKind.Authentication, true)]
    [InlineData(ProviderKind.OpenAiCompatible, 403, ProviderFailureKind.Authentication, true)]
    [InlineData(ProviderKind.Ollama, 404, ProviderFailureKind.BadRequest, true)]
    [InlineData(ProviderKind.Anthropic, 400, ProviderFailureKind.BadRequest, true)]
    [InlineData(ProviderKind.OpenAiCompatible, 422, ProviderFailureKind.BadRequest, true)]
    public async Task Non_transient_errors_map_to_a_fatal_kind_without_a_retry(ProviderKind kind, int status, ProviderFailureKind expected, bool fatal)
    {
        var (client, backend, time) = Build(kind);
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing((HttpStatusCode)status, seen));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(expected, ex.Kind);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(fatal, ex.IsFatal);
        Assert.Equal(1, backend.Calls);
        Assert.Equal(1, ex.Attempts);
    }

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task An_error_never_carries_the_prompt_the_key_a_header_or_the_response_body(ProviderKind kind)
    {
        var (client, backend, time) = Build(kind, new ResilienceOptions { MaxRetries = 1 });
        backend.ResponseHeaders["x-request-id"] = "HEADER-CANARY-77";
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(HttpStatusCode.InternalServerError, seen));
        var secretPrompt = new[] { new ChatMessage(ChatRole.User, "PROMPT-CANARY-42 hello") };

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(secretPrompt, Options)));

        var everything = ex.ToString() + ex.Message + ex.Data.Count + ex.InnerException;
        foreach (var secret in new[] { "PROMPT-CANARY-42", Settings.Key, "HEADER-CANARY-77", "echo:" })
            Assert.DoesNotContain(secret, everything);
        Assert.Equal(ProviderFailureKind.ServerError, ex.Kind);
        Assert.Equal(2, ex.Attempts);
        Assert.Contains("PROMPT-CANARY-42", backend.Seen[0].Body);
    }

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task A_200_with_an_unusable_body_is_an_invalid_response_that_names_only_the_exception_type(ProviderKind kind)
    {
        var (client, backend, time) = Build(kind);
        backend.Script = (_, _) => Task.FromResult<HttpResponseMessage?>(backend.Json(HttpStatusCode.OK, "{\"surprise\": \"BODY-CANARY\"}"));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.InvalidResponse, ex.Kind);
        Assert.False(ex.IsFatal);
        Assert.DoesNotContain("BODY-CANARY", ex.ToString());
    }

    [Fact]
    public async Task A_redirect_is_refused_and_not_followed()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic);
        backend.Script = (_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            r.Headers.Location = new Uri("https://other.example.test/steal");
            return Task.FromResult<HttpResponseMessage?>(r);
        };

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.BadRequest, ex.Kind);
        Assert.Contains("redirect", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, backend.Calls);
        Assert.DoesNotContain("steal", ex.ToString());
    }

    [Fact]
    public void The_production_transport_does_not_follow_redirects_so_a_key_header_cannot_be_forwarded()
    {
        using var transport = ProviderHttp.CreateTransport();

        var sockets = Assert.IsType<SocketsHttpHandler>(transport);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
    }

    // ---- retries -------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Settings.Kinds), MemberType = typeof(Settings))]
    public async Task Transient_errors_are_retried_with_jittered_exponential_backoff_and_the_same_request(ProviderKind kind)
    {
        var (client, backend, time) = Build(kind);
        backend.Script = (n, seen) => Task.FromResult<HttpResponseMessage?>(n <= 2 ? backend.ErrorEchoing(n == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable, seen) : null);

        var response = await time.Drive(client.GetResponseAsync(Prompt(), Options));

        Assert.NotNull(response.Usage);
        Assert.Equal(3, backend.Calls);
        // Equal jitter with a fixed 0.5: step/2 + 0.5*step/2 = 0.75 * step, steps 1 s and 2 s. (Timers for per-attempt timeouts are not delays.)
        var delays = time.RequestedDelays.Where(d => d < TimeSpan.FromSeconds(10)).ToList();
        Assert.Equal([TimeSpan.FromSeconds(0.75), TimeSpan.FromSeconds(1.5)], delays);
        Assert.All(backend.Seen, s => Assert.Equal(backend.Seen[0].Body, s.Body));
        Assert.All(backend.Seen, s => Assert.True(s.Headers.ContainsKey(kind == ProviderKind.Anthropic ? "x-api-key" : "Authorization") || kind == ProviderKind.Ollama));
    }

    [Fact]
    public async Task Backoff_never_exceeds_the_configured_cap()
    {
        var (client, backend, time) = Build(ProviderKind.OpenAiCompatible, new ResilienceOptions { MaxRetries = 6, BaseDelay = TimeSpan.FromSeconds(10), MaxDelay = TimeSpan.FromSeconds(20) }, () => 0.999999);
        backend.Script = (n, seen) => Task.FromResult<HttpResponseMessage?>(n <= 6 ? backend.ErrorEchoing(HttpStatusCode.BadGateway, seen) : null);

        await time.Drive(client.GetResponseAsync(Prompt(), Options));

        var delays = time.RequestedDelays.Where(d => d < TimeSpan.FromMinutes(1)).ToList();
        Assert.Equal(6, delays.Count);
        Assert.All(delays, d => Assert.True(d <= TimeSpan.FromSeconds(20), d.ToString()));
        Assert.True(delays[^1] > TimeSpan.FromSeconds(19));
    }

    [Fact]
    public async Task Retry_After_is_honoured_when_it_fits_under_the_cap()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic);
        backend.Script = (n, _) =>
        {
            if (n > 1) return Task.FromResult<HttpResponseMessage?>(null);
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.TryAddWithoutValidation("Retry-After", "3");
            return Task.FromResult<HttpResponseMessage?>(r);
        };

        await time.Drive(client.GetResponseAsync(Prompt(), Options));

        Assert.Contains(TimeSpan.FromSeconds(3), time.RequestedDelays);
        Assert.Equal(2, backend.Calls);
    }

    [Fact]
    public async Task A_Retry_After_beyond_the_cap_ends_the_retries_instead_of_stalling_the_interview()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic);
        backend.Script = (_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.TryAddWithoutValidation("Retry-After", "3600");
            return Task.FromResult<HttpResponseMessage?>(r);
        };

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.RateLimited, ex.Kind);
        Assert.False(ex.IsFatal);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task Retries_are_bounded_and_the_last_failure_is_reported_with_the_attempt_count()
    {
        var (client, backend, time) = Build(ProviderKind.OpenAiCompatible, new ResilienceOptions { MaxRetries = 2 });
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(HttpStatusCode.TooManyRequests, seen));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.RateLimited, ex.Kind);
        Assert.Equal(3, backend.Calls);
        Assert.Equal(3, ex.Attempts);
        Assert.Contains("after 3 attempts", ex.Message);
    }

    [Fact]
    public async Task Zero_retries_means_exactly_one_attempt()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic, new ResilienceOptions { MaxRetries = 0 });
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(HttpStatusCode.BadGateway, seen));

        await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task A_connection_failure_is_retried_then_reported_as_a_network_error()
    {
        var (client, backend, time) = Build(ProviderKind.Ollama, new ResilienceOptions { MaxRetries = 1 });
        backend.Script = (_, _) => throw new HttpRequestException("connection refused: SECRET-HOST-DETAIL");

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.Network, ex.Kind);
        Assert.Equal(2, backend.Calls);
        Assert.DoesNotContain("SECRET-HOST-DETAIL", ex.ToString());
    }

    [Fact]
    public async Task An_attempt_that_hangs_is_cancelled_at_the_request_timeout_and_retried_on_the_fake_clock()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic, new ResilienceOptions { MaxRetries = 1, RequestTimeout = TimeSpan.FromSeconds(5) });
        backend.HangOn = n => n == 1;

        var response = await time.Drive(client.GetResponseAsync(Prompt(), Options));

        Assert.NotNull(response.Usage);
        Assert.Equal(2, backend.Calls);
        Assert.Contains(TimeSpan.FromSeconds(5), time.RequestedDelays);
    }

    [Fact]
    public async Task A_provider_that_never_answers_ends_as_a_timeout_error_after_the_retries()
    {
        var (client, backend, time) = Build(ProviderKind.Ollama, new ResilienceOptions { MaxRetries = 1, RequestTimeout = TimeSpan.FromSeconds(5) });
        backend.HangOn = _ => true;

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.Timeout, ex.Kind);
        Assert.Equal(2, ex.Attempts);
    }

    [Fact]
    public async Task The_whole_call_has_a_deadline_shorter_than_the_attempts()
    {
        var resilience = new ResilienceOptions { MaxRetries = 0, RequestTimeout = TimeSpan.FromSeconds(30), OverallTimeoutOverride = TimeSpan.FromSeconds(10) };
        var (client, backend, time) = Build(ProviderKind.Ollama, resilience);
        backend.HangOn = _ => true;

        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Cancelling_during_a_backoff_wait_stops_at_once_and_surfaces_cancellation_not_a_provider_error()
    {
        var (client, backend, time) = Build(ProviderKind.Anthropic);
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(HttpStatusCode.TooManyRequests, seen));
        using var cts = new CancellationTokenSource();

        var call = client.GetResponseAsync(Prompt(), Options, cts.Token);
        for (var i = 0; i < 200 && time.RequestedDelays.Count == 0; i++) await Task.Delay(5);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task A_token_cancelled_before_the_call_makes_no_request()
    {
        var (client, backend, time) = Build(ProviderKind.OpenAiCompatible);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options, cts.Token)));

        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task Consecutive_failed_calls_stop_the_interview_with_a_fatal_error()
    {
        var (client, backend, time) = Build(ProviderKind.Ollama, new ResilienceOptions { MaxRetries = 0, MaxConsecutiveFailures = 3 });
        backend.Script = (_, seen) => Task.FromResult<HttpResponseMessage?>(backend.ErrorEchoing(HttpStatusCode.BadGateway, seen));

        var first = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));
        var second = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));
        var third = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.False(first.IsFatal);
        Assert.False(second.IsFatal);
        Assert.Equal(ProviderFailureKind.Unavailable, third.Kind);
        Assert.True(third.IsFatal);
    }

    [Fact]
    public async Task A_success_resets_the_consecutive_failure_count()
    {
        var (client, backend, time) = Build(ProviderKind.Ollama, new ResilienceOptions { MaxRetries = 0, MaxConsecutiveFailures = 2 });
        backend.Script = (n, seen) => Task.FromResult<HttpResponseMessage?>(n is 1 or 3 ? backend.ErrorEchoing(HttpStatusCode.BadGateway, seen) : null);

        await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));
        await time.Drive(client.GetResponseAsync(Prompt(), Options));
        var ex = await Assert.ThrowsAsync<ProviderException>(() => time.Drive(client.GetResponseAsync(Prompt(), Options)));

        Assert.Equal(ProviderFailureKind.ServerError, ex.Kind);
    }
}
