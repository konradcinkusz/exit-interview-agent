using System.Net;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Personas;
using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli.Tests;

[Collection("Console")]
public class InterviewCliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Out => Path.Combine(_dir, "out");

    private Func<string, string?> Env(params (string, string)[] extra)
    {
        var d = new Dictionary<string, string> { [EnvVars.ConfigDir] = Path.Combine(_dir, "cfg") };
        foreach (var (k, v) in extra) d[k] = v;
        return k => d.GetValueOrDefault(k);
    }

    private static async Task<(int Code, string Out, string Err)> Run(string[] args, string stdin, Func<string, string?> env, ProviderRuntime? runtime = null, TextReader? reader = null, CancellationToken ct = default)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await CliApp.RunAsync(args, new CliHost(reader ?? new StringReader(stdin), o, e, env, runtime, ct, ExportTelemetry: false));
        return (code, o.ToString(), e.ToString());
    }

    /// <summary>What a cooperative person would type: consent, then each topic answered concretely, then more concrete answers for any follow-up.</summary>
    internal static string Answers()
    {
        var p = PersonaCatalog.Get("talkative");
        var lines = new List<string> { p.Responses.Consent[0] };
        lines.AddRange(InterviewProtocol.Current.Topics.Select(t => p.Responses.Topics[t.Id][0]));
        for (var i = 0; i < 30; i++) lines.Add(p.Responses.Fallback[i % p.Responses.Fallback.Count]);
        return string.Join('\n', lines) + "\n";
    }

    private const string Consent = "Yes, I consent.\n";

    private static readonly string[] MockArgs = ["--provider", "mock", "--model", "scripted", "--tenure", "1y_3y", "--employer", "acme-example"];

    // ---- the interactive interview -------------------------------------------------------------------------------

    [Fact]
    public async Task A_full_interview_in_the_terminal_shows_the_record_and_validation_and_writes_only_the_record()
    {
        var (code, output, err) = await Run(["interview", .. MockArgs, "--out", Out], Answers(), Env());

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, err);
        Assert.Contains("I am an AI interviewer", output);
        Assert.Contains("== Record ==", output);
        Assert.Contains("valid: yes, errors: 0", output);
        Assert.Contains("submittable: yes", output);
        Assert.Contains("Submitting is optional and separate", output);
        Assert.Equal(["record.json"], Directory.GetFiles(Out).Select(Path.GetFileName));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "record.json")));
        Assert.Equal("1", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.DoesNotContain("transcript", string.Join(' ', Directory.GetFiles(Out)));
    }

    [Fact]
    public async Task The_transcript_is_written_only_with_save_transcript_and_out()
    {
        var (code, _, _) = await Run(["interview", .. MockArgs, "--out", Out, "--save-transcript"], Answers(), Env());

        Assert.Equal(0, code);
        var transcript = File.ReadAllText(Path.Combine(Out, "transcript.txt"));
        Assert.Contains("Interviewer:", transcript);
        Assert.Contains("Interviewee:", transcript);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Out, "transcript.txt")));

        var (bad, _, err) = await Run(["interview", .. MockArgs, "--save-transcript"], Answers(), Env());
        Assert.Equal(2, bad);
        Assert.Contains("--out", err);
    }

    [Fact]
    public async Task End_of_input_in_the_middle_is_consent_withdrawal_nothing_is_kept_and_nothing_is_written()
    {
        var firstThree = string.Join('\n', Answers().Split('\n').Take(3)) + "\n";

        var (code, output, _) = await Run(["interview", .. MockArgs, "--out", Out, "--save-transcript"], firstThree, Env());

        Assert.Equal(3, code);
        Assert.Contains("nothing was kept or written", output);
        Assert.DoesNotContain("== Record ==", output);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public async Task Ctrl_C_while_waiting_for_an_answer_discards_everything()
    {
        using var cts = new CancellationTokenSource();
        var reader = new BlockingReader(Answers().Split('\n').Take(2).ToArray(), cts);

        var (code, output, _) = await Run(["interview", .. MockArgs, "--out", Out, "--save-transcript"], string.Empty, Env(), reader: reader, ct: cts.Token);

        Assert.Equal(3, code);
        Assert.True(reader.Blocked);
        Assert.Contains("nothing was kept or written", output);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public async Task Refusing_consent_ends_the_interview_with_no_record()
    {
        var (code, output, _) = await Run(["interview", .. MockArgs, "--out", Out], "No, I do not agree.\n", Env());

        Assert.Equal(3, code);
        Assert.Contains("consent_not_given", output);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public async Task A_missing_tenure_is_asked_for_and_end_of_input_there_keeps_nothing()
    {
        var args = new[] { "interview", "--provider", "mock", "--model", "m" };

        var (code, output, _) = await Run(args, string.Empty, Env());
        var (bad, _, err) = await Run(["interview", "--provider", "mock", "--model", "m", "--tenure", "forever"], string.Empty, Env());

        Assert.Equal(InterviewCommand.Exit.Cancelled, code);
        Assert.Contains("How long did you work there?", output);
        Assert.Equal(2, bad);
        Assert.Contains("--tenure must be one of", err);
    }

    // ---- disclosure ----------------------------------------------------------------------------------------------

    private sealed class Counting : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = """{ "model": "m", "message": { "role": "assistant", "content": "OK" }, "done": true, "done_reason": "stop", "prompt_eval_count": 5, "eval_count": 1 }""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    private static readonly string[] RemoteOllama = ["interview", "--provider", "ollama", "--model", "m", "--base-url", "http://gpu-box.lan:11434", "--tenure", "1y_3y"];

    [Fact]
    public async Task An_external_provider_needs_an_explicit_yes_and_declining_sends_nothing()
    {
        var transport = new Counting();

        var (code, output, _) = await Run(RemoteOllama, "no\n", Env(), new ProviderRuntime { Transport = transport });

        Assert.Equal(InterviewCommand.Exit.NotConfirmed, code);
        Assert.Equal(0, transport.Calls);
        Assert.Contains("gpu-box.lan:11434", output);
        Assert.Contains("every question and every answer you type", output);
        Assert.Contains("README.md#run-it-with-your-own-model", output);
        Assert.Contains("Not confirmed. Nothing was sent anywhere.", output);
        Assert.False(File.Exists(Path.Combine(_dir, "cfg", ConfigPaths.ConfirmationsFileName)));
    }

    [Fact]
    public async Task End_of_input_at_the_confirmation_prompt_counts_as_no()
    {
        var transport = new Counting();

        var (code, _, _) = await Run(RemoteOllama, string.Empty, Env(), new ProviderRuntime { Transport = transport });

        Assert.Equal(InterviewCommand.Exit.NotConfirmed, code);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Yes_continues_and_the_choice_is_remembered_only_when_asked_in_a_file_with_no_secret_and_no_time()
    {
        var env = Env(("ANTHROPIC_API_KEY", "sk-secret-never-stored-123"));
        var transport = new Counting { Status = HttpStatusCode.Unauthorized };

        var first = await Run(["interview", "--provider", "anthropic", "--model", "m", "--tenure", "1y_3y"], "yes\ny\nYes\n", env, new ProviderRuntime { Transport = transport });
        var path = Path.Combine(_dir, "cfg", ConfigPaths.ConfirmationsFileName);
        var stored = File.ReadAllText(path);
        var second = await Run(["interview", "--provider", "anthropic", "--model", "m", "--tenure", "1y_3y"], "Yes\n", env, new ProviderRuntime { Transport = transport });

        Assert.Equal(InterviewCommand.Exit.ProviderFailed, first.Code);
        Assert.Contains("api.anthropic.com", first.Out);
        Assert.Contains("Remembered in", first.Out);
        Assert.DoesNotContain("sk-secret", stored);
        Assert.DoesNotContain("20", stored.Replace("2026", string.Empty).Replace("\"noticeVersion\": 1", string.Empty).Replace("11434", string.Empty));
        using (var doc = JsonDocument.Parse(stored))
        {
            var entry = doc.RootElement.GetProperty("confirmed").EnumerateArray().Single();
            Assert.Equal(["endpoint", "noticeVersion", "provider"], entry.EnumerateObject().Select(p => p.Name).Order());
        }
        Assert.Contains("You confirmed this provider before", second.Out);
        Assert.DoesNotContain("Type \"yes\"", second.Out);

        var forget = await Run(["providers", "forget-confirmations"], string.Empty, env);
        Assert.Contains("Deleted", forget.Out);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Saying_no_to_remembering_stores_nothing_and_the_next_run_asks_again()
    {
        var env = Env(("ANTHROPIC_API_KEY", "sk-secret-never-stored-123"));
        var transport = new Counting { Status = HttpStatusCode.Unauthorized };

        await Run(["interview", "--provider", "anthropic", "--model", "m", "--tenure", "1y_3y"], "yes\nn\n", env, new ProviderRuntime { Transport = transport });
        var again = await Run(["interview", "--provider", "anthropic", "--model", "m", "--tenure", "1y_3y"], string.Empty, env, new ProviderRuntime { Transport = transport });

        Assert.False(File.Exists(Path.Combine(_dir, "cfg", ConfigPaths.ConfirmationsFileName)));
        Assert.Equal(InterviewCommand.Exit.NotConfirmed, again.Code);
    }

    [Fact]
    public async Task The_non_interactive_flag_confirms_without_a_prompt_and_without_remembering()
    {
        var transport = new Counting { Status = HttpStatusCode.Unauthorized };

        var (code, output, _) = await Run([.. RemoteOllama, "--yes-i-understand"], Consent, Env(), new ProviderRuntime { Transport = transport });

        Assert.Equal(InterviewCommand.Exit.ProviderFailed, code);
        Assert.Contains("--yes-i-understand given", output);
        Assert.True(transport.Calls > 0);
        Assert.False(File.Exists(Path.Combine(_dir, "cfg", ConfigPaths.ConfirmationsFileName)));
    }

    [Fact]
    public async Task A_local_model_gets_the_short_notice_and_needs_no_confirmation()
    {
        var transport = new Counting { Status = HttpStatusCode.NotFound };

        var (code, output, _) = await Run(["interview", "--provider", "ollama", "--model", "m", "--tenure", "1y_3y"], Consent, Env(), new ProviderRuntime { Transport = transport });

        Assert.Contains("Local model:", output);
        Assert.Contains("on this computer", output);
        Assert.DoesNotContain("Type \"yes\"", output);
        Assert.DoesNotContain("where your answers go", output);
        Assert.True(transport.Calls > 0);
        Assert.Equal(InterviewCommand.Exit.ProviderFailed, code);
    }

    [Fact]
    public async Task A_provider_that_rejects_the_key_stops_the_interview_with_a_hint_and_without_the_key_anywhere()
    {
        var key = "sk-secret-never-printed-456";
        var transport = new Counting { Status = HttpStatusCode.Unauthorized, Body = $$"""{ "error": { "message": "bad key {{key}}" } }""" };

        var (code, output, err) = await Run(["interview", "--provider", "anthropic", "--model", "m", "--tenure", "1y_3y", "--yes-i-understand"], Consent, Env(("ANTHROPIC_API_KEY", key)), new ProviderRuntime { Transport = transport });

        Assert.Equal(InterviewCommand.Exit.ProviderFailed, code);
        Assert.Contains("provider.auth_failed", err);
        Assert.Contains("Nothing was kept or written", err);
        Assert.Contains("Check the API key", err);
        Assert.DoesNotContain(key, output + err);
        Assert.False(Directory.Exists(Out));
    }

    // ---- configuration errors ------------------------------------------------------------------------------------

    [Fact]
    public async Task There_is_no_api_key_flag_and_a_value_given_to_an_unknown_option_is_not_echoed()
    {
        var (code, _, err) = await Run(["interview", "--provider", "anthropic", "--model", "m", "--api-key", "sk-typed-on-the-command-line"], string.Empty, Env());

        Assert.Equal(2, code);
        Assert.DoesNotContain("sk-typed", err);
        Assert.Contains("Unknown option '--api-key'", err);
    }

    [Fact]
    public async Task Unsupported_backends_and_credentials_are_refused_with_exit_2_and_the_readme_pointer()
    {
        var copilot = await Run(["interview", "--provider", "copilot", "--model", "m"], string.Empty, Env());
        var subscription = await Run(["interview", "--provider", "anthropic", "--model", "m", "--api-key-env", "CLAUDE_CODE_OAUTH_TOKEN"], string.Empty, Env(("CLAUDE_CODE_OAUTH_TOKEN", "sk-sub-token-value-789")));
        var missingKey = await Run(["interview", "--provider", "anthropic", "--model", "m"], string.Empty, Env(("CLAUDE_CODE_OAUTH_TOKEN", "sk-sub-token-value-789")));

        Assert.All(new[] { copilot, subscription, missingKey }, r => Assert.Equal(2, r.Code));
        Assert.Contains("could not be verified", copilot.Err);
        Assert.Contains("subscription", subscription.Err);
        Assert.Contains("README.md#run-it-with-your-own-model", subscription.Err);
        Assert.Contains("ANTHROPIC_API_KEY", missingKey.Err);
        Assert.DoesNotContain("sk-sub-token", copilot.Err + subscription.Err + missingKey.Err + copilot.Out + subscription.Out + missingKey.Out);
    }

    [Fact]
    public async Task Settings_come_from_the_config_file_and_a_key_in_it_is_refused()
    {
        var file = Path.Combine(_dir, "my-config.json");
        File.WriteAllText(file, """{ "provider": "ollama", "model": "from-file", "baseUrl": "http://localhost:9" }""");
        var transport = new Counting { Status = HttpStatusCode.NotFound };

        var ok = await Run(["interview", "--config", file, "--tenure", "1y_3y"], string.Empty, Env(), new ProviderRuntime { Transport = transport });
        File.WriteAllText(file, """{ "provider": "anthropic", "apiKey": "sk-in-a-file" }""");
        var bad = await Run(["interview", "--config", file], string.Empty, Env());
        var missing = await Run(["interview", "--config", Path.Combine(_dir, "nope.json")], string.Empty, Env());

        Assert.Contains("localhost:9", ok.Out);
        Assert.Equal(2, bad.Code);
        Assert.DoesNotContain("sk-in-a-file", bad.Err);
        Assert.Equal(2, missing.Code);
    }

    // ---- providers command ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Providers_lists_every_provider_and_the_refusals_and_makes_no_network_call()
    {
        var transport = new Counting();

        var (code, output, _) = await Run(["providers"], string.Empty, Env(("ANTHROPIC_API_KEY", "sk-secret-never-printed-789")), new ProviderRuntime { Transport = transport });

        Assert.Equal(0, code);
        Assert.Equal(0, transport.Calls);
        foreach (var id in new[] { "anthropic", "openai-compatible", "ollama", "mock" }) Assert.Contains(id, output);
        Assert.Contains("ANTHROPIC_API_KEY is set", output);
        Assert.DoesNotContain("sk-secret", output);
        Assert.Contains("Claude subscription", output);
        Assert.Contains("GitHub Copilot", output);
        Assert.Contains("could not be verified", output);
        Assert.Contains("Nothing selected yet", output);
    }

    [Fact]
    public async Task Providers_shows_the_effective_selection_and_where_each_value_came_from()
    {
        var (_, output, _) = await Run(["providers"], string.Empty, Env((EnvVars.Provider, "ollama"), (EnvVars.Model, "llama-x")));

        Assert.Contains("provider ollama (from Environment), model llama-x (from Environment)", output);
    }

    [Fact]
    public async Task Ping_makes_exactly_one_request_only_when_asked_and_reports_tokens_and_latency()
    {
        var transport = new Counting();

        var (code, output, _) = await Run(["providers", "ping", "--provider", "ollama", "--model", "m"], string.Empty, Env(), new ProviderRuntime { Transport = transport });

        Assert.Equal(0, code);
        Assert.Equal(1, transport.Calls);
        Assert.Contains("one minimal request", output);
        Assert.Contains("no interview content", output);
        Assert.Contains("tokens_in=5 tokens_out=1", output);
    }

    [Fact]
    public async Task A_failed_ping_reports_the_status_and_never_the_body_or_key()
    {
        var key = "sk-secret-never-printed-321";
        var transport = new Counting { Status = HttpStatusCode.Unauthorized, Body = "body-with-" + key };

        var (code, output, err) = await Run(["providers", "ping", "--provider", "anthropic", "--model", "m"], string.Empty, Env(("ANTHROPIC_API_KEY", key)), new ProviderRuntime { Transport = transport });

        Assert.Equal(5, code);
        Assert.Contains("HTTP 401", err);
        Assert.DoesNotContain(key, output + err);
        Assert.DoesNotContain("body-with", output + err);
    }

    // ---- telemetry -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, null, new string[0])]
    [InlineData("console", null, new[] { "console" })]
    [InlineData("otlp,console", null, new[] { "otlp", "console" })]
    [InlineData("none", "http://collector:4317", new string[0])]
    [InlineData(null, "http://collector:4317", new[] { "otlp" })]
    [InlineData("bogus", null, new string[0])]
    public void Trace_export_is_off_unless_selected_or_an_otlp_endpoint_is_configured(string? selector, string? endpoint, string[] expected)
    {
        var env = new Dictionary<string, string?> { ["OTEL_TRACES_EXPORTER"] = selector, ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint };

        Assert.Equal(expected, TelemetrySetup.TraceExporters(k => env.GetValueOrDefault(k)));
    }

    [Fact]
    public void The_sdk_disabled_switch_turns_every_exporter_off_and_only_the_two_project_sources_are_registered()
    {
        var env = new Dictionary<string, string?> { ["OTEL_SDK_DISABLED"] = "true", ["OTEL_TRACES_EXPORTER"] = "console", ["OTEL_METRICS_EXPORTER"] = "otlp" };

        Assert.Empty(TelemetrySetup.TraceExporters(k => env.GetValueOrDefault(k)));
        Assert.Empty(TelemetrySetup.MetricExporters(k => env.GetValueOrDefault(k)));
        Assert.Equal(["ExitInterviewAgent.Agent", "ExitInterviewAgent.Providers"], TelemetrySetup.Sources.Order());
        Assert.Equal(["ExitInterviewAgent.Providers"], TelemetrySetup.Meters);
        Assert.Equal(["bogus"], TelemetrySetup.Unknown(k => k == "OTEL_TRACES_EXPORTER" ? "otlp,bogus" : null, "OTEL_TRACES_EXPORTER"));
    }

    [Fact]
    public async Task With_the_console_exporter_selected_the_spans_are_printed_once_at_the_end_and_carry_no_interview_text()
    {
        const string canary = "zebracanary7391qx";
        var captured = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(captured);
        try
        {
            var o = new StringWriter();
            var env = Env(("OTEL_TRACES_EXPORTER", "console"), ("OTEL_METRICS_EXPORTER", "console"));
            var lines = Answers().Split('\n').Select(l => l.Length > 0 ? l + " " + canary : l);
            var code = await CliApp.RunAsync(["interview", .. MockArgs, "--out", Out, "--save-transcript"], new CliHost(new StringReader(string.Join('\n', lines)), o, new StringWriter(), env, null, default, ExportTelemetry: true));

            Assert.Equal(0, code);
            // Power: the canary really went through the interview.
            Assert.Contains(canary, File.ReadAllText(Path.Combine(Out, "transcript.txt")));
        }
        finally { Console.SetOut(previous); }

        var exported = captured.ToString();
        Assert.Contains("interview.session", exported);
        Assert.Contains("interview.turn", exported);
        Assert.DoesNotContain(canary, exported);
        Assert.DoesNotContain("acme-example", exported);
    }

    private sealed class BlockingReader(string[] lines, CancellationTokenSource cts) : TextReader
    {
        private int _next;

        public bool Blocked { get; private set; }

        public override Task<string?> ReadLineAsync()
        {
            if (_next < lines.Length) return Task.FromResult<string?>(lines[_next++]);
            Blocked = true;
            cts.CancelAfter(50);
            return new TaskCompletionSource<string?>().Task;
        }
    }
}

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;
