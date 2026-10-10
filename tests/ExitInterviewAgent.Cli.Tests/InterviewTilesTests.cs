using System.Text.Json;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Cli.Tiles;
using ExitInterviewAgent.Providers;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Cli.Tests;

/// <summary>Y4: <c>interview</c> ends with tiles (unless <c>--no-tiles</c>), keeps its files new, switches language mid-interview and never shows an unmasked address.</summary>
[Collection("Console")]
public class InterviewTilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-itiles-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Out => Path.Combine(_dir, "out");

    private Func<string, string?> Env() => k => k == EnvVars.ConfigDir ? Path.Combine(_dir, "cfg") : null;

    private static readonly string[] MockArgs = ["--provider", "mock", "--model", "scripted", "--tenure", "1y_3y", "--employer", "acme-example"];

    private static readonly InterviewProtocol Pl = InterviewProtocol.For("pl");

    private const string PlAnswer = "Na tym temacie było jedno konkretne zdarzenie: proces trwał 3 tygodnie i nikt nie wyjaśnił dlaczego.";
    private const string EnAnswer = "On this topic there was one concrete thing: the process took 3 weeks and nobody explained why.";

    private async Task<(int Code, string Out, string Err)> Run(string[] args, string stdin)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var host = new CliHost(new StringReader(stdin), o, e, Env(), null, default, ExportTelemetry: false);
        var code = await CliApp.RunAsync(args, host);
        return (code, o.ToString(), e.ToString());
    }

    /// <summary>The standard answers with some lines replaced. Index 0 is consent, 1 to 6 are the six topic answers.</summary>
    private static string Stdin(params (int Index, string Line)[] replace)
    {
        var lines = InterviewCliTests.Answers().Split('\n').Where(l => l.Length > 0).ToArray();
        foreach (var (index, line) in replace) lines[index] = line;
        return string.Join('\n', lines) + "\n";
    }

    private string Read(params string[] parts) => File.ReadAllText(Path.Combine([Out, .. parts]));

    private static string AllText(string dir) => string.Join('\n', Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(File.ReadAllText));

    // ---- tiles at the end of interview ---------------------------------------------------------------------------

    [Fact]
    public async Task A_completed_polish_interview_ends_with_polish_tiles_and_writes_them_into_a_tiles_folder()
    {
        var (code, output, err) = await Run(["interview", .. MockArgs, "--language", "pl", "--out", Out], InterviewCliTests.PolishAnswers());

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, err);
        Assert.Contains(TileRenderer.NoticePl, output);
        Assert.Contains("Fakty z twoich ocen", output);
        Assert.True(File.Exists(Path.Combine(Out, "record.json")));
        Assert.Contains("Fakty z twoich ocen", Read("tiles", "tiles.html"));
        using var doc = JsonDocument.Parse(Read("tiles", "tiles.json"));
        Assert.Equal("pl", doc.RootElement.GetProperty("language").GetString());
        // The Facts tile is built by code; the others come from the model (here the scripted mock) and must be there too.
        Assert.True(doc.RootElement.GetProperty("tiles").GetArrayLength() > 1);
    }

    [Fact]
    public async Task An_english_interview_ends_with_the_english_notice_and_facts_tile()
    {
        var (code, output, _) = await Run(["interview", .. MockArgs, "--language", "en"], InterviewCliTests.Answers());

        Assert.Equal(0, code);
        Assert.Contains(TileRenderer.NoticeEn, output);
        Assert.Contains("Facts from your ratings", output);
    }

    [Fact]
    public async Task No_tiles_skips_them_and_the_record_is_written_as_before()
    {
        var (code, output, _) = await Run(["interview", .. MockArgs, "--no-tiles", "--out", Out], InterviewCliTests.Answers());

        Assert.Equal(0, code);
        Assert.DoesNotContain(TileRenderer.NoticeEn, output);
        Assert.DoesNotContain(TileRenderer.NoticePl, output);
        Assert.False(Directory.Exists(Path.Combine(Out, "tiles")));
        Assert.True(File.Exists(Path.Combine(Out, "record.json")));
    }

    [Fact]
    public async Task A_second_run_into_the_same_folder_is_refused_before_the_interview_and_overwrites_nothing()
    {
        await Run(["interview", .. MockArgs, "--out", Out], InterviewCliTests.Answers());
        var recordBefore = Read("record.json");
        var tilesBefore = Read("tiles", "tiles.json");

        var (code, output, err) = await Run(["interview", .. MockArgs, "--out", Out], InterviewCliTests.Answers());

        Assert.Equal(2, code);
        Assert.Contains("already exists and will not be overwritten", err);
        Assert.DoesNotContain("I am an AI", output);
        Assert.Equal(recordBefore, Read("record.json"));
        Assert.Equal(tilesBefore, Read("tiles", "tiles.json"));
    }

    [Fact]
    public async Task A_withdrawn_interview_has_no_record_and_no_tiles_and_no_error()
    {
        var (code, output, err) = await Run(["interview", .. MockArgs, "--out", Out], "No, I do not agree.\n");

        Assert.Equal(3, code);
        Assert.Equal(string.Empty, err);
        Assert.Contains("No tiles", output);
        Assert.DoesNotContain(TileRenderer.NoticeEn, output);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public async Task An_address_typed_in_an_answer_appears_in_neither_the_record_nor_the_tiles()
    {
        const string canary = "canary.person@example.com";
        var stdin = Stdin((1, $"Contact me at {canary}: the process took 3 weeks and nobody explained why."));

        var (code, output, err) = await Run(["interview", .. MockArgs, "--out", Out], stdin);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, err);
        Assert.True(File.Exists(Path.Combine(Out, "tiles", "tiles.html")), "the tiles must have been produced for this check to mean anything");
        Assert.DoesNotContain(canary, output);
        Assert.DoesNotContain(canary, AllText(Out));
    }

    [Fact]
    public async Task An_exhausted_budget_skips_the_tiles_and_says_so_while_the_interview_result_stands()
    {
        var options = new InterviewOptions("acme-example", new RecordContext(TenureBand.OneToThreeYears, null, null));
        var lines = InterviewCliTests.Answers().Split('\n').Where(l => l.Length > 0).ToArray();
        var result = await InterviewRunner.Create(new ScriptedChatClient(), options).RunAsync(new LinesInterviewee(lines));
        Assert.Equal(InterviewOutcome.Completed, result.Outcome);

        var o = new StringWriter();
        var e = new StringWriter();
        var host = new CliHost(new StringReader(string.Empty), o, e, Env(), null, default, ExportTelemetry: false);
        var spent = new ModelMeter(InterviewProtocol.Current.Limits with { MaxModelCalls = 0 });

        await InterviewCommand.AutoTilesAsync(result, new ScriptedChatClient(), spent, host, null);

        Assert.Contains("No tiles", o.ToString());
        Assert.Contains("budget", o.ToString());
        Assert.DoesNotContain(TileRenderer.NoticeEn, o.ToString());
        Assert.Equal(string.Empty, e.ToString());
    }

    [Fact]
    public void The_usage_text_names_no_tiles_and_the_language_flag()
    {
        Assert.Contains("--no-tiles", CliApp.Usage);
        Assert.Contains("--language pl|en|auto", CliApp.Usage);
    }

    // ---- language switch mid-interview ---------------------------------------------------------------------------

    [Fact]
    public async Task A_polish_answer_in_an_english_interview_switches_the_next_question_with_one_confirmation_sentence()
    {
        var topics = InterviewProtocol.Current.Topics.Select(t => t.Topic).ToArray();

        var (code, output, _) = await Run(["interview", .. MockArgs, "--language", "en"], Stdin((2, PlAnswer)));

        Assert.Equal(0, code);
        Assert.Contains($"Dobrze, kontynuujmy po polsku. {Pl.Spec(topics[2]).Question}", output);
    }

    [Fact]
    public async Task An_english_request_in_a_polish_interview_switches_to_english_from_the_next_question()
    {
        var topics = InterviewProtocol.Current.Topics.Select(t => t.Topic).ToArray();
        var request = "Mów po angielsku, proszę. Na tym temacie proces trwał 3 tygodnie i nikt nie wyjaśnił dlaczego.";

        var (code, output, _) = await Run(["interview", .. MockArgs, "--language", "pl"], Stdin((1, request)));

        Assert.Equal(0, code);
        Assert.Contains($"Sure, let's continue in English. {InterviewProtocol.Current.Spec(topics[1]).Question}", output);
    }

    [Fact]
    public async Task A_tie_does_not_switch_the_language()
    {
        var (code, output, _) = await Run(["interview", .. MockArgs, "--language", "en"], Stdin((1, "Process 2 weeks, nie the 3 people, one manager.")));

        Assert.Equal(0, code);
        Assert.DoesNotContain("kontynuujmy po polsku", output);
        Assert.DoesNotContain("Sure, let's continue", output);
    }

    [Fact]
    public async Task The_record_language_is_the_language_most_interviewee_turns_were_written_in()
    {
        var stdin = Stdin((1, PlAnswer), (2, PlAnswer), (3, PlAnswer), (4, PlAnswer));

        var (code, _, _) = await Run(["interview", .. MockArgs, "--language", "en", "--out", Out], stdin);

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(Read("record.json"));
        Assert.Equal("pl", doc.RootElement.GetProperty("interview").GetProperty("language").GetString());
    }

    /// <summary>Replays fixed lines, then leaves. A plain test double for the runner outside the console.</summary>
    private sealed class LinesInterviewee(IReadOnlyList<string> lines) : IInterviewee
    {
        private int _next;

        public Task<string?> ReplyAsync(IntervieweeTurn turn, CancellationToken ct) =>
            Task.FromResult(_next < lines.Count ? lines[_next++] : null);

        public Task DeliverAsync(IntervieweeTurn turn, CancellationToken ct) => Task.CompletedTask;
    }
}
