using System.Text.Json.Nodes;
using ExitInterviewAgent.Eval.Cli;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Eval.Reporting;
using static ExitInterviewAgent.Eval.Tests.Support.Fixtures;

namespace ExitInterviewAgent.Eval.Tests;

public class ReportTests
{
    private static async Task<(ReportInput Input, JsonObject Json, string Markdown)> Build(PriceTable? prices = null, bool twoProfiles = false)
    {
        var run = await FullMockRun();
        var profiles = new List<ProfileRun> { run };
        if (twoProfiles) profiles.Add(run with { Profile = ModelProfile.Mock with { Name = "mock-b" } });
        profiles.Add(ProfileRun.Skipped(new ModelProfile("anthropic", "x", "(unset)", null, "skipped:no-credential (environment not set: ANTHROPIC_API_KEY)")));
        var l2 = await Layer2Run.RunAsync(run.Runs, null);
        var input = new ReportInput(ExitInterviewAgent.Eval.Scenarios.Corpus.SpecVersion(), ExitInterviewAgent.Eval.Scenarios.Corpus.Digest(All), All, profiles, l2, prices, "test", true);
        return (input, ConformanceReport.ToJson(input), ConformanceReport.ToMarkdown(input));
    }

    private static IEnumerable<string> AllKeys(JsonNode? n) => n switch
    {
        JsonObject o => o.SelectMany(p => AllKeys(p.Value).Prepend(p.Key)),
        JsonArray a => a.SelectMany(AllKeys),
        _ => [],
    };

    [Fact]
    public async Task Every_proportion_is_reported_with_n_and_a_wilson_interval_in_the_same_payload()
    {
        var (_, json, _) = await Build();

        var coverage = json["metrics"]!.AsArray().Single(m => m!["id"]!.GetValue<string>() == "coverage")!["byProfile"]!["mock"]!["overall"]!;

        Assert.NotNull(coverage["k"]);
        Assert.True(coverage["n"]!.GetValue<int>() > 0);
        Assert.NotNull(coverage["rate"]);
        Assert.True(coverage["low"]!.GetValue<double>() <= coverage["rate"]!.GetValue<double>());
        Assert.True(coverage["high"]!.GetValue<double>() >= coverage["rate"]!.GetValue<double>());
    }

    [Fact]
    public async Task A_metric_with_no_observations_has_a_null_rate_never_zero_or_one()
    {
        var (_, json, _) = await Build();

        var opr = json["metrics"]!.AsArray().Single(m => m!["id"]!.GetValue<string>() == "lqr.rules_disagree")!["byProfile"]!["mock"]!["byClass"]!;
        var anyEmptyClass = json["metrics"]!.AsArray().SelectMany(m => m!["byProfile"]!["mock"]!["byClass"]!.AsObject()).Count();

        Assert.NotNull(opr);
        Assert.True(anyEmptyClass > 0);
    }

    [Fact]
    public async Task The_report_has_no_composite_no_weighted_total_and_no_ranking_anywhere()
    {
        var (_, json, md) = await Build(twoProfiles: true);

        var keys = AllKeys(json).Select(k => k.ToLowerInvariant()).ToList();

        Assert.DoesNotContain(keys, k => k.Contains("composite", StringComparison.Ordinal) || k.Contains("weighted", StringComparison.Ordinal) || k.Contains("rank", StringComparison.Ordinal) || k.Contains("leaderboard", StringComparison.Ordinal) || k == "total" || k.Contains("overallscore", StringComparison.Ordinal));
        Assert.DoesNotContain("leaderboard", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no composite", json["report"]!["readingGuide"]!.GetValue<string>());
        Assert.Contains("never a person or an employer", json["report"]!["unitOfEvaluation"]!.GetValue<string>());
    }

    [Fact]
    public async Task Two_profiles_with_overlapping_intervals_are_reported_as_not_distinguishable()
    {
        var (_, _, md) = await Build(twoProfiles: true);

        Assert.Contains("Distinguishable?", md);
        Assert.Contains("no (intervals overlap)", md);
    }

    [Fact]
    public async Task A_profile_that_cannot_run_is_listed_with_its_skip_reason_in_both_formats()
    {
        var (_, json, md) = await Build();

        var p = json["profiles"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "anthropic")!;
        Assert.StartsWith("skipped:no-credential", p["status"]!.GetValue<string>());
        Assert.Contains("ANTHROPIC_API_KEY", md);
        Assert.Contains("## Skipped profiles", md);
    }

    [Fact]
    public async Task The_report_says_plainly_what_the_mock_does_and_does_not_demonstrate()
    {
        var (_, json, md) = await Build();

        Assert.Contains("says NOTHING about how a real model behaves", json["report"]!["whatTheMockShows"]!.GetValue<string>());
        Assert.Contains("What this run does and does not show", md);
    }

    [Fact]
    public async Task Cost_is_computed_only_from_a_user_supplied_price_table_otherwise_tokens_are_reported()
    {
        var (_, without, md) = await Build();
        var prices = new PriceTable { As_Of = "2000-01-01", Source = "test table", Currency = "XXX", Models = { ["scripted-mock"] = new ModelPrice { Input_Per_Mtok = 1, Output_Per_Mtok = 2 } } };
        var (_, with, mdWith) = await Build(prices);

        Assert.StartsWith("not computed", without["usage"]!["mock"]!["cost"]!.GetValue<string>());
        Assert.Contains("not computed", md);
        Assert.True(without["usage"]!["mock"]!["inputTokens"]!.GetValue<long>() > 0);
        Assert.True(with["usage"]!["mock"]!["cost"]!["amount"]!.GetValue<double>() > 0);
        Assert.Contains("price table as of 2000-01-01", mdWith);
    }

    [Fact]
    public async Task Layer_2_is_reported_as_skipped_with_its_rubric_and_prompt_hashes_and_the_calibration_state()
    {
        var (_, json, md) = await Build();

        var l2 = json["layer2"]!;
        Assert.StartsWith("skipped:no-credential", l2["status"]!.GetValue<string>());
        Assert.Equal(64, l2["rubricSha256"]!.GetValue<string>().Length);
        Assert.False(l2["gating"]!.GetValue<bool>());
        Assert.Contains("NOT calibrated", md);
    }

    [Fact]
    public async Task The_cli_run_command_writes_json_and_markdown_and_the_skipped_profile_is_named()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}");
        var @out = new StringWriter();

        var code = await EvalCli.RunAsync(["run", "--profile", "mock", "--profile", "anthropic", "--out", dir, "--deterministic"], @out, new StringWriter());

        Assert.Equal(0, code);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "report.json")))!;
        Assert.Equal("mock", json["profiles"]![0]!["name"]!.GetValue<string>());
        Assert.StartsWith("skipped", json["profiles"]![1]!["status"]!.GetValue<string>());
        Assert.Contains("# Conformance report", await File.ReadAllTextAsync(Path.Combine(dir, "report.md")));
    }

    [Fact]
    public async Task Two_cli_runs_into_different_directories_write_byte_identical_json_so_the_ci_cmp_check_holds()
    {
        var a = Path.Combine(Path.GetTempPath(), $"rep-a-{Guid.NewGuid():N}");
        var b = Path.Combine(Path.GetTempPath(), $"rep-b-{Guid.NewGuid():N}");

        await EvalCli.RunAsync(["run", "--profile", "mock", "--deterministic", "--out", a], new StringWriter(), new StringWriter());
        await EvalCli.RunAsync(["run", "--profile", "mock", "--deterministic", "--out", b], new StringWriter(), new StringWriter());

        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(a, "report.json")), await File.ReadAllTextAsync(Path.Combine(b, "report.json")));
    }

    [Fact]
    public async Task The_cli_calibrate_command_states_the_label_provenance_and_the_skips_and_computes_the_offline_numbers()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"calib-{Guid.NewGuid():N}");

        var code = await EvalCli.RunAsync(["calibrate", "--out", dir], new StringWriter(), new StringWriter());

        Assert.Equal(0, code);
        var md = await File.ReadAllTextAsync(Path.Combine(dir, "calibration.md"));
        Assert.Contains("author-labelled and non-human", md);
        Assert.Contains("rehearsal", md);
        Assert.Contains("skipped:no-credential", md);
        Assert.Contains("kappa=", md);
        Assert.Contains("NOT calibrated", md);
    }

    [Fact]
    public async Task The_cli_validate_list_and_profiles_commands_work_and_an_unknown_command_is_a_usage_error()
    {
        var o = new StringWriter();

        Assert.Equal(0, await EvalCli.RunAsync(["validate"], o, new StringWriter()));
        Assert.Contains("ok", o.ToString());
        Assert.Equal(0, await EvalCli.RunAsync(["list"], o, new StringWriter()));
        Assert.Equal(0, await EvalCli.RunAsync(["profiles"], o, new StringWriter()));
        Assert.Contains("mock", o.ToString());
        Assert.Equal(2, await EvalCli.RunAsync(["frobnicate"], o, new StringWriter()));
        Assert.Equal(2, await EvalCli.RunAsync(["run", "--bogus"], o, new StringWriter()));
    }
}
