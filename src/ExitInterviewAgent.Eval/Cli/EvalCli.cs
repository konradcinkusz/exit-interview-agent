using System.Text;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Eval.Reporting;
using ExitInterviewAgent.Eval.Scenarios;

namespace ExitInterviewAgent.Eval.Cli;

/// <summary>
/// `exit-interview-eval`: validate, run, gate, baseline, calibrate, profiles, list. Exit code 0 = ok, 1 = a gate or validation failed, 2 = usage.
/// Nothing here calls a real model unless a profile that needs one is named and its environment is configured.
/// </summary>
public static class EvalCli
{
    public const string Usage = """
        exit-interview-eval <command> [options]

          validate                         check scenarios against the schema and the corpus rules, print the per-class counts
          list                             list scenarios
          profiles                         list model profiles and whether they can run here
          run [--profile NAME]... [--out DIR] [--deterministic] [--prices FILE] [--no-judge]
                                           run the corpus against each profile (default: mock) and write report.json and report.md
          gate [--profile mock]            run the offline gate: constraints at 100%, behaviours against evals/baseline.json
          baseline --justification TEXT    regenerate evals/baseline.json (a justification is required and the PR must repeat it)
          calibrate [--out DIR]            rule screens and the reply analyser against the hand labels; judge and model classifier when a judge profile exists
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter @out, TextWriter err, CancellationToken ct = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { await @out.WriteLineAsync(Usage); return args.Length == 0 ? 2 : 0; }
        var opts = Options.Parse(args.Skip(1).ToArray());
        if (opts.Error is { } e) { await err.WriteLineAsync(e); await err.WriteLineAsync(Usage); return 2; }
        try
        {
            return args[0] switch
            {
                "validate" => Validate(@out),
                "list" => List(@out),
                "profiles" => Profiles(@out),
                "run" => await RunCommandAsync(opts, args, @out, err, ct),
                "gate" => await GateAsync(opts, @out, err, ct),
                "baseline" => await BaselineAsync(opts, @out, err, ct),
                "calibrate" => await CalibrateAsync(opts, @out, ct),
                _ => Unknown(args[0], err),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or InvalidOperationException or KeyNotFoundException)
        {
            await err.WriteLineAsync("error: " + ex.Message);
            return 1;
        }
    }

    private static int Unknown(string cmd, TextWriter err) { err.WriteLine($"unknown command '{cmd}'"); err.WriteLine(Usage); return 2; }

    private sealed record Options(List<string> Profiles, string? Out, bool Deterministic, string? Prices, bool NoJudge, string? Justification, string? Error)
    {
        public static Options Parse(string[] a)
        {
            var profiles = new List<string>();
            string? o = null, prices = null, just = null;
            bool det = false, noJudge = false;
            for (var i = 0; i < a.Length; i++)
            {
                string? Next() => i + 1 < a.Length ? a[++i] : null;
                switch (a[i])
                {
                    case "--profile": if (Next() is { } p) profiles.Add(p); else return Bad("--profile needs a name"); break;
                    case "--out": o = Next(); if (o is null) return Bad("--out needs a directory"); break;
                    case "--prices": prices = Next(); if (prices is null) return Bad("--prices needs a file"); break;
                    case "--justification": just = Next(); if (string.IsNullOrWhiteSpace(just)) return Bad("--justification needs a sentence"); break;
                    case "--deterministic": det = true; break;
                    case "--no-judge": noJudge = true; break;
                    default: return Bad($"unknown option '{a[i]}'");
                }
            }
            return new Options(profiles, o, det, prices, noJudge, just, null);
            static Options Bad(string m) => new([], null, false, null, false, null, m);
        }
    }

    private static int Validate(TextWriter w)
    {
        var corpus = ScenarioLoader.LoadAll();
        var report = Corpus.Validate(corpus);
        w.WriteLine($"scenarios: {report.Scenarios} ({report.Runs} runs: one per scenario and seed)");
        foreach (var (cls, n) in report.PerClass) w.WriteLine($"  {cls,-12} {n}");
        w.WriteLine($"constraint-gated: {corpus.Count(l => l.Scenario.Gate == "constraint")}, behaviour-gated: {corpus.Count(l => l.Scenario.Gate == "behaviour")}, skipped: {corpus.Count(l => l.Scenario.Skip is not null)}");
        w.WriteLine($"spec version {Corpus.SpecVersion()}, corpus digest {Corpus.Digest(corpus)}");
        foreach (var e in report.Errors) w.WriteLine("INVALID " + e);
        w.WriteLine(report.Ok ? "ok" : $"{report.Errors.Count} problem(s)");
        return report.Ok ? 0 : 1;
    }

    private static int List(TextWriter w)
    {
        foreach (var l in ScenarioLoader.LoadAll())
            w.WriteLine($"{l.Id}  [{l.Scenario.Class}/{l.Scenario.Gate}] persona={l.Scenario.Persona} seeds={string.Join(",", l.Scenario.EffectiveSeeds)} spec={string.Join(",", l.Scenario.Spec)}");
        return 0;
    }

    private static int Profiles(TextWriter w)
    {
        foreach (var p in ProfileCatalog.LoadAll())
            w.WriteLine($"{p.Name,-22} {(p.Runnable ? "runnable" : p.SkipReason),-60} model={p.ModelId}");
        if (ProfileCatalog.LoadJudge() is { } j) w.WriteLine($"{"judge (" + j.Name + ")",-22} {(j.Runnable ? "runnable" : j.SkipReason),-60} model={j.ModelId}");
        w.WriteLine($"registered provider factories: {(ProviderFactories.Registered.Count == 0 ? "none (T6 registers them; see docs/eval/README.md)" : string.Join(", ", ProviderFactories.Registered))}");
        return 0;
    }

    private static IReadOnlyList<ModelProfile> Resolve(IEnumerable<string> names)
    {
        var all = ProfileCatalog.LoadAll();
        var wanted = names.Any() ? names : ["mock"];
        return wanted.Select(n => all.FirstOrDefault(p => p.Name == n) ?? throw new KeyNotFoundException($"no profile '{n}' (known: {string.Join(", ", all.Select(p => p.Name))})")).ToList();
    }

    private static IReadOnlyList<LoadedScenario> LoadValidCorpus(TextWriter err, out bool ok)
    {
        var corpus = ScenarioLoader.LoadAll();
        var v = Corpus.Validate(corpus);
        foreach (var e in v.Errors) err.WriteLine("INVALID " + e);
        ok = v.Ok;
        return corpus;
    }

    private static async Task<int> RunCommandAsync(Options o, string[] args, TextWriter w, TextWriter err, CancellationToken ct)
    {
        var corpus = LoadValidCorpus(err, out var ok);
        if (!ok) return 1;
        var labels = LabelSets.ReplyIndex(LabelSets.LoadVagueness());
        var profiles = new List<ProfileRun>();
        foreach (var p in Resolve(o.Profiles)) profiles.Add(await Evaluation.RunProfileAsync(p, corpus, labels, null, ct));

        var judgeProfile = o.NoJudge ? null : ProfileCatalog.LoadJudge();
        var l2 = await Layer2Run.RunAsync(profiles.SelectMany(p => p.Runs).ToList(), judgeProfile, ct);
        if (o.NoJudge) l2 = l2 with { Status = "skipped:disabled (--no-judge)" };

        var input = new ReportInput(Corpus.SpecVersion(), Corpus.Digest(corpus), corpus, profiles, l2, o.Prices is null ? null : PriceTable.Load(o.Prices),
            "exit-interview-eval " + string.Join(' ', args.Where(a => a != "--prices" && !(o.Prices is not null && a == o.Prices))), o.Deterministic);
        var md = ConformanceReport.ToMarkdown(input);
        if (o.Out is { } dir)
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "report.json"), ConformanceReport.Serialize(ConformanceReport.ToJson(input)), ct);
            await File.WriteAllTextAsync(Path.Combine(dir, "report.md"), md, ct);
            await w.WriteLineAsync($"wrote {Path.Combine(dir, "report.json")} and {Path.Combine(dir, "report.md")}");
        }
        else await w.WriteAsync(md);

        var failed = profiles.Where(p => p.Ran).Sum(p => p.Grades.Count(g => g.Failures.Any(f => f.Kind == Layer1.AssertionResult.Constraint)) + p.Errors.Count);
        await w.WriteLineAsync(failed == 0 ? "constraints: held in every run of every ran profile" : $"constraints: {failed} run(s) violated a constraint or errored (see the report); `gate` is the command that blocks");
        return 0;
    }

    private static async Task<int> GateAsync(Options o, TextWriter w, TextWriter err, CancellationToken ct)
    {
        var corpus = LoadValidCorpus(err, out var ok);
        if (!ok) return 1;
        var profile = Resolve(o.Profiles.Count == 0 ? ["mock"] : o.Profiles)[0];
        var run = await Evaluation.RunProfileAsync(profile, corpus, LabelSets.ReplyIndex(LabelSets.LoadVagueness()), null, ct);
        var baseline = Baseline.Load();
        var result = Gate.Evaluate(run, corpus, baseline, Corpus.SpecVersion(), Corpus.Digest(corpus));
        var cons = run.Grades.Sum(g => g.Assertions.Count(a => a.Kind == Layer1.AssertionResult.Constraint && a.Verdict != Layer1.Verdict.NotApplicable));
        await w.WriteLineAsync($"gate profile '{profile.Name}': {run.Grades.Count} runs, {cons} constraint assertions evaluated, {run.Errors.Count} harness errors; baseline recorded {baseline.Recorded} ({baseline.Justification})");
        foreach (var n in result.Notes) await w.WriteLineAsync($"note   [{n.Kind}] {n.Subject}: {n.Message}");
        foreach (var f in result.Failures) await w.WriteLineAsync($"FAILED [{f.Kind}] {f.Subject}: {f.Message}");
        await w.WriteLineAsync(result.Passed ? "gate: PASSED (layer 1 only; layer 2 is advisory and blocks nothing)" : $"gate: FAILED ({result.Failures.Count} finding(s))");
        return result.Passed ? 0 : 1;
    }

    private static async Task<int> BaselineAsync(Options o, TextWriter w, TextWriter err, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(o.Justification)) { await err.WriteLineAsync("refusing to regenerate the baseline without --justification \"why the measured behaviour changed\"; the PR description must repeat it as a 'Baseline justification:' line"); return 2; }
        var corpus = LoadValidCorpus(err, out var ok);
        if (!ok) return 1;
        var profile = Resolve(o.Profiles.Count == 0 ? ["mock"] : o.Profiles)[0];
        var run = await Evaluation.RunProfileAsync(profile, corpus, LabelSets.ReplyIndex(LabelSets.LoadVagueness()), null, ct);
        if (!run.Ran || run.Errors.Count > 0) { await err.WriteLineAsync("the profile did not run cleanly; not recording a baseline from a broken run"); return 1; }
        var failedConstraint = run.Grades.SelectMany(g => g.Failures).Where(f => f.Kind == Layer1.AssertionResult.Constraint).ToList();
        if (failedConstraint.Count > 0) { await err.WriteLineAsync($"{failedConstraint.Count} constraint assertion(s) fail: a baseline never records a constraint violation. Fix the agent or the scenario first."); return 1; }

        var existing = File.Exists(RepoLayout.BaselinePath) ? Baseline.Load() : null;
        var metrics = MetricCatalog.All.Where(m => m.Gated).ToDictionary(m => m.Id, m => Evaluation.Pool(run.Grades, m.Id));
        var scenarios = corpus.Where(l => l.Scenario.Skip is null).ToDictionary(l => l.Id, l => Evaluation.ScenarioStatus(run.Grades, run.Errors, l.Id));
        var tokens = Evaluation.Tokens(run.Grades, g => corpus.First(l => l.Id == g.ScenarioId).Scenario.Measures("coverage"))?.Mean ?? 0;
        var json = Baseline.Serialize(Corpus.SpecVersion(), Corpus.Digest(corpus), profile.Name, o.Justification!.Trim(), scenarios, metrics, tokens, existing?.Tolerances, existing?.TokensRelativeTolerance ?? 0.10,
            DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        await File.WriteAllTextAsync(RepoLayout.BaselinePath, json, ct);
        await w.WriteLineAsync($"wrote {RepoLayout.BaselinePath} ({scenarios.Count} scenarios, {metrics.Count} gated metrics). Put this line in the PR description:");
        await w.WriteLineAsync($"Baseline justification: {o.Justification!.Trim()}");
        return 0;
    }

    private static async Task<int> CalibrateAsync(Options o, TextWriter w, CancellationToken ct)
    {
        var rubric = Rubric.Load();
        var labels = LabelSets.LoadJudge();
        var vag = LabelSets.LoadVagueness();
        var sb = new StringBuilder();
        sb.AppendLine("# Calibration and classifier experiment");
        sb.AppendLine();
        sb.AppendLine($"Judge label set: {labels.Items.Count} items, labeller `{labels.Labeller}` (kind `{labels.Labeller_Kind}`). Vagueness label set: {vag.Replies.Count} replies and {vag.Pairs.Count} pairs, labeller `{vag.Labeller}` (kind `{vag.Labeller_Kind}`).");
        sb.AppendLine("**Both sets were written by the AI session that wrote the harness: author-labelled and non-human. This is a rehearsal of the protocol, a weak calibration at best, and it cannot discharge the rule that a judge is calibrated against humans.**");
        sb.AppendLine();
        sb.AppendLine("## Rule screens against the author labels (offline, reproducible)");
        sb.AppendLine();
        foreach (var a in Calibration.RuleScreensVsLabels(labels)) sb.AppendLine("- " + a);

        var judgeProfile = ProfileCatalog.LoadJudge();
        sb.AppendLine();
        sb.AppendLine("## Judge against the author labels");
        sb.AppendLine();
        AgreementReport? agreement = null;
        if (judgeProfile is { Runnable: true })
        {
            var judge = new RubricJudge(judgeProfile.Factory!(), judgeProfile.ModelId, rubric);
            var judged = new Dictionary<string, int?>();
            foreach (var item in labels.Items)
                judged[item.Id] = (await judge.JudgeAsync(new JudgeInput(item.Rubric, item.Rubric == "R-02" ? "probe" : "question", item.Question, item.Preceded_By), ct)).Score;
            foreach (var r in new[] { "R-01", "R-02" }) sb.AppendLine("- " + Calibration.JudgeVsLabels(labels, judged, r));
            agreement = Calibration.JudgeVsLabels(labels, judged, "R-01");
            sb.AppendLine($"- judge model configured: {judge.PinnedModelId}; answering: {judge.AnsweringModelId ?? "n/a"}");
        }
        else sb.AppendLine($"- {judgeProfile?.SkipReason ?? "skipped:no-credential (no judge profile is declared in evals/profiles.yaml)"}. Judge-versus-label agreement was NOT computed.");
        var decision = Calibration.Decide(labels, rubric, agreement?.Kappa);
        sb.AppendLine($"- Gate: {decision.Reason}");

        sb.AppendLine();
        sb.AppendLine("## Vagueness and contradiction: rule-based analyser against the hand labels");
        sb.AppendLine();
        AppendClassifier(sb, ClassifierExperiment.VaguenessAnalyzer(vag));
        AppendClassifier(sb, ClassifierExperiment.ContradictionAnalyzer(vag));
        sb.AppendLine();
        sb.AppendLine("## Vagueness and contradiction: model-assisted classifier (the experiment T4 recommended)");
        sb.AppendLine();
        if (judgeProfile is { Runnable: true })
            foreach (var r in await new ModelClassifier(judgeProfile.Factory!(), judgeProfile.ModelId).RunAsync(vag, ct)) AppendClassifier(sb, r);
        else sb.AppendLine($"- {judgeProfile?.SkipReason ?? "skipped:no-credential (no judge profile is declared in evals/profiles.yaml)"}. The model-assisted classifier was NOT run; nothing here compares it with the analyser.");

        if (o.Out is { } dir)
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "calibration.md"), sb.ToString(), ct);
            await w.WriteLineAsync($"wrote {Path.Combine(dir, "calibration.md")}");
        }
        else await w.WriteAsync(sb.ToString());
        return 0;
    }

    private static void AppendClassifier(StringBuilder sb, ClassifierReport r)
    {
        sb.AppendLine($"### {r.Name}");
        sb.AppendLine();
        sb.AppendLine($"- status {r.Status}; n={r.N}; {r.Note}");
        if (r.Agreement is { } a) sb.AppendLine("- " + a);
        if (r.Recall is { } rc) sb.AppendLine($"- recall (positive class): {rc}");
        if (r.Precision is { } pr) sb.AppendLine($"- precision (positive class): {pr}");
        if (r.Confusion is { } m)
        {
            sb.AppendLine();
            sb.AppendLine("| human \\ predicted | " + string.Join(" | ", m.Classes) + " |");
            sb.AppendLine("|---|" + string.Concat(m.Classes.Select(_ => "---|")));
            for (var i = 0; i < m.Classes.Count; i++) sb.AppendLine($"| {m.Classes[i]} | " + string.Join(" | ", Enumerable.Range(0, m.Classes.Count).Select(j => m.Cells[i, j])) + " |");
        }
        sb.AppendLine();
    }
}
