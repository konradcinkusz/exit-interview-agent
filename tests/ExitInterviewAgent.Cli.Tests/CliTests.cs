using ExitInterviewAgent.Cli;
using ExitInterviewAgent.Personas;

namespace ExitInterviewAgent.Cli.Tests;

public class CliTests
{
    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await CliApp.RunAsync(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task Personas_lists_every_persona_with_its_id()
    {
        var (code, output, _) = await Run("personas");

        Assert.Equal(0, code);
        foreach (var p in PersonaCatalog.All) Assert.Contains(p.Id, output);
        Assert.Equal(PersonaCatalog.All.Count, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Demo_prints_the_masked_transcript_the_record_the_validation_and_the_invariant_report()
    {
        var (code, output, err) = await Run("demo", "--persona", "talkative", "--seed", "1");

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, err);
        Assert.Contains("== Masked transcript ==", output);
        Assert.Contains("I am an AI interviewer", output);
        Assert.Contains("== Record ==", output);
        Assert.Contains("\"schemaVersion\": \"1\"", output);
        Assert.Contains("valid: yes, errors: 0", output);
        Assert.Contains("== Invariants ==", output);
        Assert.Contains("[PASS] quotes-verbatim", output);
        Assert.DoesNotContain("[FAIL]", output);
        Assert.Contains("scripted mock model", output);
    }

    [Fact]
    public async Task Demo_output_is_byte_identical_for_the_same_seed_and_changes_with_it()
    {
        var a = await Run("demo", "--persona", "talkative", "--seed", "4");
        var b = await Run("demo", "--persona", "talkative", "--seed", "4");
        var outputs = new HashSet<string>();
        foreach (var seed in Enumerable.Range(1, 8)) outputs.Add((await Run("demo", "--persona", "talkative", "--seed", seed.ToString())).Out);

        Assert.Equal(a.Out, b.Out);
        Assert.True(outputs.Count > 1);
    }

    [Fact]
    public async Task Demo_runs_for_every_persona_and_exits_zero()
    {
        foreach (var p in PersonaCatalog.All)
        {
            var (code, output, err) = await Run("demo", "--persona", p.Id);

            Assert.True(code == 0, $"{p.Id}: {err}");
            Assert.DoesNotContain("[FAIL]", output);
        }
    }

    [Fact]
    public async Task A_withdrawal_prints_no_transcript_and_no_record()
    {
        var (code, output, _) = await Run("demo", "--persona", "withdraws-consent");

        Assert.Equal(0, code);
        Assert.Contains("(discarded:", output);
        Assert.Contains("(no record:", output);
        Assert.DoesNotContain("Interviewee:", output);
        Assert.DoesNotContain("\"schemaVersion\"", output);
        Assert.Contains("outcome=withdrawn", output);
    }

    [Fact]
    public async Task The_manager_demo_shows_the_masked_name_and_none_of_the_planted_literals()
    {
        var (_, output, _) = await Run("demo", "--persona", "names-manager");

        Assert.Contains("[PERSON]", output);
        Assert.DoesNotContain("Brunhilda", output);
        Assert.DoesNotContain("Fogwhistle", output);
        Assert.DoesNotContain("mailinator", output);
    }

    [Fact]
    public async Task Out_writes_the_files_that_exist_and_none_for_a_discarded_interview()
    {
        var dir = Path.Combine(Path.GetTempPath(), "exit-interview-tests-" + Guid.NewGuid().ToString("N"));
        var dir2 = dir + "-w";
        try
        {
            await Run("demo", "--persona", "vague", "--seed", "2", "--out", dir);
            await Run("demo", "--persona", "withdraws-consent", "--out", dir2);

            Assert.Equal(["record.json", "report.txt", "transcript.txt"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());
            Assert.Equal(["report.txt"], Directory.GetFiles(dir2).Select(Path.GetFileName));
            Assert.Contains("\"schemaVersion\"", File.ReadAllText(Path.Combine(dir, "record.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(dir2)) Directory.Delete(dir2, true);
        }
    }

    [Theory]
    [InlineData]
    [InlineData("unknown")]
    [InlineData("demo")]
    [InlineData("demo", "--persona", "nobody")]
    [InlineData("demo", "--persona", "talkative", "--seed", "x")]
    [InlineData("demo", "--persona", "talkative", "--bogus")]
    [InlineData("demo", "--persona")]
    public async Task Bad_usage_exits_two_with_a_message_on_stderr_and_no_stack_trace(params string[] args)
    {
        var (code, output, err) = await Run(args);

        Assert.Equal(2, code);
        Assert.True(err.Length > 0 || output.Contains("Usage:"));
        Assert.DoesNotContain("   at ", err + output);
    }

    [Fact]
    public async Task Help_and_version_exit_zero()
    {
        Assert.Equal(0, (await Run("--help")).Code);
        var (code, output, _) = await Run("--version");
        Assert.Equal(0, code);
        Assert.Contains("protocol 1.2", output);
    }
}
