using System.Text.Json;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli.Tests;

[Collection("Console")]
public class InterviewLanguageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-lang-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Out => Path.Combine(_dir, "out");

    private static readonly string[] MockArgs = ["--provider", "mock", "--model", "scripted", "--tenure", "1y_3y", "--employer", "acme-example"];

    private Func<string, string?> Env() => k => k == EnvVars.ConfigDir ? Path.Combine(_dir, "cfg") : null;

    private async Task<(int Code, string Out, string Err)> Run(string[] args, string stdin)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var host = new CliHost(new StringReader(stdin), o, e, Env(), null, default, ExportTelemetry: false);
        var code = await CliApp.RunAsync(args, host);
        return (code, o.ToString(), e.ToString());
    }

    private static string Polish() => InterviewProtocol.For("pl").Opening;

    // ---- the flag reaches the interview -------------------------------------------------------------------------

    [Fact]
    public async Task Language_pl_puts_the_polish_opening_on_standard_output_and_records_pl()
    {
        var (code, output, err) = await Run(["interview", .. MockArgs, "--language", "pl", "--out", Out], InterviewCliTests.Answers());

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, err);
        Assert.Contains(Polish(), output);
        Assert.Contains("sztucznej inteligencji", output);
        Assert.DoesNotContain(InterviewProtocol.Current.Opening, output);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "record.json")));
        Assert.Equal("pl", doc.RootElement.GetProperty("interview").GetProperty("language").GetString());
    }

    [Fact]
    public async Task Language_pl_asks_the_topic_questions_in_polish()
    {
        var (_, output, _) = await Run(["interview", .. MockArgs, "--language", "pl"], InterviewCliTests.Answers());

        foreach (var t in InterviewProtocol.For("pl").Topics) Assert.Contains(t.Question, output);
        foreach (var t in InterviewProtocol.Current.Topics) Assert.DoesNotContain(t.Question, output);
    }

    [Fact]
    public async Task Language_en_keeps_the_english_opening_and_records_en()
    {
        var (code, output, _) = await Run(["interview", .. MockArgs, "--language", "en", "--out", Out], InterviewCliTests.Answers());

        Assert.Equal(0, code);
        Assert.Contains(InterviewProtocol.Current.Opening, output);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Out, "record.json")));
        Assert.Equal("en", doc.RootElement.GetProperty("interview").GetProperty("language").GetString());
    }

    [Fact]
    public async Task An_unknown_language_is_a_usage_error_with_exit_code_2_and_nothing_starts()
    {
        var (code, output, err) = await Run(["interview", .. MockArgs, "--language", "xx", "--out", Out], InterviewCliTests.Answers());

        Assert.Equal(2, code);
        Assert.Contains("--language", err);
        Assert.DoesNotContain("I am an AI", output);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public async Task A_language_flag_without_a_value_is_a_usage_error()
    {
        var (code, _, err) = await Run(["interview", .. MockArgs, "--language"], string.Empty);

        Assert.Equal(2, code);
        Assert.Contains("--language", err);
    }

    [Fact]
    public void The_language_flag_is_declared_for_the_interview_command_and_the_usage_text_mentions_it()
    {
        Assert.Contains("--language", CliFlags.ByCommand["interview"]);
        Assert.Contains("--language pl|en|auto", CliApp.Usage);
    }

    // ---- auto follows the system UI language, injected rather than read from the process --------------------------

    [Theory]
    [InlineData("pl", "pl")]
    [InlineData("en", "en")]
    [InlineData("de", "en")]
    [InlineData("fr", "en")]
    [InlineData("", "en")]
    public void Auto_is_polish_for_a_polish_ui_language_and_english_otherwise(string uiLanguage, string expected) =>
        Assert.Equal(expected, InterviewCommand.ResolveLanguage("auto", uiLanguage));

    [Fact]
    public void Auto_is_the_default_when_the_flag_is_missing()
    {
        Assert.Equal("pl", InterviewCommand.ResolveLanguage(null, "pl"));
        Assert.Equal("en", InterviewCommand.ResolveLanguage(null, "de"));
    }

    [Theory]
    [InlineData("pl", "pl")]
    [InlineData("en", "en")]
    public void An_explicit_language_wins_over_the_system_language(string value, string expected) =>
        Assert.Equal(expected, InterviewCommand.ResolveLanguage(value, "de"));

    [Theory]
    [InlineData("xx")]
    [InlineData("PL")]
    [InlineData("pl-PL")]
    [InlineData("")]
    public void Any_other_value_is_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => InterviewCommand.ResolveLanguage(value, "en"));
}
