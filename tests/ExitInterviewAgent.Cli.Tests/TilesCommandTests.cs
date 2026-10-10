using System.Net;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Cli.Tests.Support;
using ExitInterviewAgent.Cli.Tiles;
using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli.Tests;

/// <summary>
/// <c>exit-interview tiles</c>, offline: the scripted mock model, a fake transport for the external-provider cases, and
/// records written from the shared fixture. Nothing reaches the network.
/// </summary>
[Collection("Console")]
public class TilesCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-tiles-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Out => Path.Combine(_dir, "out");

    private Func<string, string?> Env(params (string, string)[] extra)
    {
        var d = new Dictionary<string, string> { [EnvVars.ConfigDir] = Path.Combine(_dir, "cfg") };
        foreach (var (k, v) in extra) d[k] = v;
        return k => d.GetValueOrDefault(k);
    }

    private static async Task<(int Code, string Out, string Err)> Run(string[] args, string stdin, Func<string, string?> env, ProviderRuntime? runtime = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await CliApp.RunAsync(args, new CliHost(new StringReader(stdin), o, e, env, runtime, default, ExportTelemetry: false));
        return (code, o.ToString(), e.ToString());
    }

    /// <summary>The shared valid fixture, found by walking up from the test binary to the repository root.</summary>
    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tests", "ExitInterviewAgent.Records.Tests", "Fixtures", "valid", "full.json"))) dir = dir.Parent;
        return dir is null ? throw new InvalidOperationException("The records fixture was not found.") : Path.Combine(dir.FullName, "tests", "ExitInterviewAgent.Records.Tests", "Fixtures", "valid", "full.json");
    }

    private string WriteRecord(string name, string? content = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content ?? File.ReadAllText(FixturePath()));
        return path;
    }

    private static readonly string[] MockArgs = ["tiles", "--provider", "mock", "--model", "scripted"];

    private sealed class Counting : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.Unauthorized;
        public string Body = """{ "error": { "message": "rejected" } }""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    // ---- the full run ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_full_offline_run_prints_the_tiles_with_the_notice_and_the_facts_tile()
    {
        var record = WriteRecord("record.json");

        var (code, output, err) = await Run([.. MockArgs, "--record", record], string.Empty, Env());

        Assert.Equal(0, code);
        Assert.Contains("Scripted mock model: nothing is sent anywhere.", err);
        Assert.Contains("[1] Facts from your ratings", output);
        Assert.Contains(TileRenderer.NoticeEn, output);
    }

    [Fact]
    public async Task Out_writes_tiles_json_and_tiles_html_and_never_overwrites_them()
    {
        var record = WriteRecord("record.json");

        var (code, _, err) = await Run([.. MockArgs, "--record", record, "--out", Out], string.Empty, Env());

        Assert.Equal(0, code);
        Assert.Contains("Wrote: ", err);
        Assert.Equal(["tiles.html", "tiles.json"], Directory.GetFiles(Out).Select(Path.GetFileName).Order());
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "tiles.json"))))
        {
            Assert.Equal("1", doc.RootElement.GetProperty("tilesVersion").GetString());
            Assert.Contains(doc.RootElement.GetProperty("tiles").EnumerateArray(), t => t.GetProperty("kind").GetString() == "facts");
        }
        Assert.Contains("<!doctype html>", File.ReadAllText(Path.Combine(Out, "tiles.html")));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Out, "tiles.json")));

        var before = File.ReadAllText(Path.Combine(Out, "tiles.json"));
        var (again, _, againErr) = await Run([.. MockArgs, "--record", record, "--out", Out], string.Empty, Env());

        Assert.Equal(2, again);
        Assert.Contains("already exists", againErr);
        Assert.Equal(before, File.ReadAllText(Path.Combine(Out, "tiles.json")));
    }

    [Fact]
    public async Task An_existing_single_file_also_stops_the_run_before_anything_is_sent_or_written()
    {
        var record = WriteRecord("record.json");
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "tiles.html"), "keep me");

        var (code, _, err) = await Run([.. MockArgs, "--record", record, "--out", Out], string.Empty, Env());

        Assert.Equal(2, code);
        Assert.Contains("already exists", err);
        Assert.Equal(["tiles.html"], Directory.GetFiles(Out).Select(Path.GetFileName));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(Out, "tiles.html")));
    }

    [Fact]
    public async Task Format_json_prints_the_tile_set_as_json_and_nothing_else_on_standard_output()
    {
        var record = WriteRecord("record.json");

        var (code, output, _) = await Run([.. MockArgs, "--record", record, "--format", "json", "--out", Out], string.Empty, Env());

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("1", doc.RootElement.GetProperty("tilesVersion").GetString());
        Assert.Equal(File.ReadAllText(Path.Combine(Out, "tiles.json")).Trim(), output.Trim());
        Assert.Contains(doc.RootElement.GetProperty("tiles").EnumerateArray(), t => t.GetProperty("kind").GetString() == "facts");
    }

    [Fact]
    public async Task Format_html_prints_the_page_on_standard_output()
    {
        var record = WriteRecord("record.json");

        var (code, output, _) = await Run([.. MockArgs, "--record", record, "--format", "html"], string.Empty, Env());

        Assert.Equal(0, code);
        Assert.StartsWith("<!doctype html>", output);
        Assert.Contains("Content-Security-Policy", output);
    }

    [Fact]
    public async Task The_same_mock_run_twice_writes_byte_identical_tiles_json()
    {
        var record = WriteRecord("record.json");
        var first = Path.Combine(_dir, "first");
        var second = Path.Combine(_dir, "second");

        Assert.Equal(0, (await Run([.. MockArgs, "--record", record, "--out", first], string.Empty, Env())).Code);
        Assert.Equal(0, (await Run([.. MockArgs, "--record", record, "--out", second], string.Empty, Env())).Code);

        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "tiles.json")), File.ReadAllBytes(Path.Combine(second, "tiles.json")));
    }

    // ---- usage, configuration and the record ----------------------------------------------------------------------

    [Fact]
    public async Task An_invalid_record_exits_4_and_its_content_is_not_echoed()
    {
        var record = WriteRecord("bad.json", """{ "schemaVersion": "1", "secretCanary": "CANARYRECORD-0123456789" }""");

        var (code, output, err) = await Run([.. MockArgs, "--record", record], string.Empty, Env());

        Assert.Equal(4, code);
        Assert.Equal(string.Empty, output);
        Assert.DoesNotContain("CANARYRECORD", err);
    }

    [Fact]
    public async Task A_record_with_a_person_in_a_quote_is_refused_with_exit_4_and_the_text_is_not_echoed()
    {
        var text = File.ReadAllText(FixturePath()).Replace("The first two weeks nobody told me who to ask about access.", "Anna Kowalska told me nothing about access.");
        var record = WriteRecord("pii.json", text);

        var (code, _, err) = await Run([.. MockArgs, "--record", record], string.Empty, Env());

        Assert.Equal(4, code);
        Assert.DoesNotContain("Anna", err);
    }

    [Fact]
    public async Task An_unknown_provider_is_a_usage_error()
    {
        var record = WriteRecord("record.json");

        var (code, _, err) = await Run(["tiles", "--provider", "nonsense", "--model", "m", "--record", record], string.Empty, Env());

        Assert.Equal(2, code);
        Assert.Contains("Run 'exit-interview --help'", err);
    }

    [Fact]
    public async Task A_missing_record_flag_is_a_usage_error()
    {
        var (code, _, err) = await Run([.. MockArgs], string.Empty, Env());

        Assert.Equal(2, code);
        Assert.Contains("--record", err);
    }

    [Fact]
    public async Task An_unknown_format_is_a_usage_error()
    {
        var record = WriteRecord("record.json");

        var (code, _, err) = await Run([.. MockArgs, "--record", record, "--format", "pdf"], string.Empty, Env());

        Assert.Equal(2, code);
        Assert.Contains("--format", err);
    }

    [Fact]
    public async Task A_missing_api_key_for_anthropic_is_a_usage_error_that_names_only_the_variable()
    {
        var record = WriteRecord("record.json");
        var transport = new Counting();

        var (code, output, err) = await Run(["tiles", "--provider", "anthropic", "--model", "m", "--record", record], "yes\n", Env(), new ProviderRuntime { Transport = transport });

        Assert.Equal(2, code);
        Assert.Contains("ANTHROPIC_API_KEY", err);
        Assert.Equal(0, transport.Calls);
        Assert.DoesNotContain("api.anthropic.com", output);
    }

    // ---- disclosure for an external provider ----------------------------------------------------------------------

    private static readonly string[] External = ["tiles", "--provider", "anthropic", "--model", "m"];

    [Fact]
    public async Task An_external_provider_without_confirmation_exits_3_and_sends_nothing()
    {
        var record = WriteRecord("record.json");
        var transport = new Counting();

        var (code, output, err) = await Run([.. External, "--record", record], "no\n", Env(("ANTHROPIC_API_KEY", "sk-canary-never-printed-123")), new ProviderRuntime { Transport = transport });

        Assert.Equal(3, code);
        Assert.Equal(0, transport.Calls);
        Assert.Contains("rekord (nie transkrypt) zostanie wysłany do", err);
        Assert.Contains("api.anthropic.com", err);
        Assert.Contains("Not confirmed. Nothing was sent anywhere.", err);
        Assert.Equal(string.Empty, output);
        Assert.False(Directory.Exists(Out));
        Assert.DoesNotContain("sk-canary", err);
    }

    [Fact]
    public async Task End_of_input_at_the_confirmation_prompt_counts_as_no()
    {
        var record = WriteRecord("record.json");
        var transport = new Counting();

        var (code, _, _) = await Run([.. External, "--record", record], string.Empty, Env(("ANTHROPIC_API_KEY", "sk-canary-never-printed-123")), new ProviderRuntime { Transport = transport });

        Assert.Equal(3, code);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Yes_i_understand_continues_to_the_provider_and_a_provider_failure_exits_5_without_writing()
    {
        var record = WriteRecord("record.json");
        var transport = new Counting();

        var (code, output, err) = await Run([.. External, "--record", record, "--out", Out, "--yes-i-understand"], string.Empty, Env(("ANTHROPIC_API_KEY", "sk-canary-never-printed-123")), new ProviderRuntime { Transport = transport });

        Assert.Equal(5, code);
        Assert.True(transport.Calls > 0);
        Assert.Contains("model provider failed", err);
        Assert.DoesNotContain("sk-canary", err + output);
        Assert.False(Directory.Exists(Out) && Directory.GetFiles(Out).Length > 0);
    }

    [Fact]
    public async Task Standard_input_as_the_record_with_an_external_provider_needs_yes_i_understand()
    {
        var record = File.ReadAllText(FixturePath());
        var transport = new Counting();

        var (code, _, err) = await Run([.. External, "--record", "-"], record, Env(("ANTHROPIC_API_KEY", "sk-canary-never-printed-123")), new ProviderRuntime { Transport = transport });

        Assert.Equal(2, code);
        Assert.Contains("--yes-i-understand", err);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Standard_input_carries_a_record_for_the_mock()
    {
        var record = File.ReadAllText(FixturePath());

        var (code, output, _) = await Run([.. MockArgs, "--record", "-"], record, Env());

        Assert.Equal(0, code);
        Assert.Contains("[1] Facts from your ratings", output);
    }

    [Fact]
    public async Task Errors_never_carry_the_record_text_or_the_key()
    {
        var record = WriteRecord("record.json", File.ReadAllText(FixturePath()).Replace("The first two weeks nobody told me who to ask about access.", "CANARYQUOTE-7f3a9c"));
        var transport = new Counting();

        var (code, output, err) = await Run([.. External, "--record", record, "--yes-i-understand"], string.Empty, Env(("ANTHROPIC_API_KEY", "sk-canary-never-printed-123")), new ProviderRuntime { Transport = transport });

        Assert.Equal(5, code);
        Assert.DoesNotContain("CANARYQUOTE", err + output);
        Assert.DoesNotContain("sk-canary", err + output);
    }

    [Fact]
    public async Task Help_lists_the_tiles_command()
    {
        var (code, output, _) = await Run(["--help"], string.Empty, Env());

        Assert.Equal(0, code);
        Assert.Contains("exit-interview tiles --record", output);
    }
}
