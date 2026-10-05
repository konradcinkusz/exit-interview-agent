using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Reporting;

public sealed record ReportInput(
    string SpecVersion, string CorpusDigest, IReadOnlyList<LoadedScenario> Corpus, IReadOnlyList<ProfileRun> Profiles, Layer2Result Layer2,
    PriceTable? Prices, string Command, bool Deterministic);

/// <summary>
/// The conformance report: the same scenario corpus run against each profile, as JSON and as Markdown. Per scenario class and per metric, every
/// proportion with n and a 95% Wilson interval, one column per profile. There is deliberately NO composite or weighted total and NO ranking of
/// profiles: where two profiles' intervals overlap the report says they are not distinguishable. A profile that cannot run is listed with its skip
/// reason. The deterministic part is byte-identical across runs; latency is the only volatile block and is omitted with --deterministic.
/// </summary>
public static class ConformanceReport
{
    private static readonly string[] ConstraintIds = Enumerable.Range(1, 12).Select(i => $"C-{i:D2}").ToArray();

    public const string MockLimits =
        "The `mock` profile is a scripted, offline model: templates and rules, no network, no randomness. It cannot be talked into anything because it understands nothing. " +
        "A clean mock run therefore shows that the CODE-SIDE protections hold (state machine, question guard, PII guard, quote step, validator, telemetry rules) and that the harness can " +
        "detect their failure; it says NOTHING about how a real model behaves under the same prompts. Intervals are over scenarios and seeds, not over model sampling. " +
        "Latency with the mock measures the harness, not a model. Real-model profiles report skipped:* unless their environment is configured.";

    public static JsonObject ToJson(ReportInput r)
    {
        var ran = r.Profiles.Where(p => p.Ran).ToList();
        var root = new JsonObject
        {
            ["report"] = new JsonObject
            {
                ["specVersion"] = r.SpecVersion,
                ["corpusDigest"] = r.CorpusDigest,
                ["harnessVersion"] = Baseline.HarnessVersion,
                ["command"] = r.Command,
                ["unitOfEvaluation"] = "interview artifact (scenario, seed): transcript, trace, record; never a person or an employer",
                ["readingGuide"] = "there is no composite score, no weighted total and no ranking; read each row with its counter-metric and its interval; overlapping intervals are not different",
                ["whatTheMockShows"] = MockLimits,
            },
            ["profiles"] = new JsonArray(r.Profiles.Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Profile.Name,
                ["modelId"] = p.Profile.ModelId,
                ["description"] = p.Profile.Description,
                ["status"] = p.Status,
                ["runs"] = p.Grades.Count,
                ["harnessErrors"] = p.Errors.Count,
            }).ToArray()),
        };

        var constraints = new JsonObject();
        foreach (var id in ConstraintIds)
        {
            var row = new JsonObject();
            foreach (var p in ran)
            {
                var results = p.Grades.SelectMany(g => g.Assertions).Where(a => a.Id == "L1." + id).ToList();
                row[p.Profile.Name] = new JsonObject { ["pass"] = results.Count(a => a.Verdict == Verdict.Pass), ["fail"] = results.Count(a => a.Verdict == Verdict.Fail), ["notApplicable"] = results.Count(a => a.Verdict == Verdict.NotApplicable) };
            }
            constraints[id] = row;
        }
        root["constraints"] = constraints;

        var metrics = new JsonArray();
        foreach (var def in MetricCatalog.All)
        {
            var byProfile = new JsonObject();
            foreach (var p in ran)
            {
                var perClass = new JsonObject();
                foreach (var cls in Corpus.Classes)
                {
                    var c = Evaluation.Pool(p.Grades, def.Id, g => ClassOf(r, g) == cls);
                    if (c.N > 0) perClass[cls] = Cnt(c);
                }
                byProfile[p.Profile.Name] = new JsonObject { ["overall"] = Cnt(Evaluation.Pool(p.Grades, def.Id)), ["byClass"] = perClass };
            }
            metrics.Add(new JsonObject
            {
                ["id"] = def.Id,
                ["title"] = def.Title,
                ["direction"] = def.Direction.ToString().ToLowerInvariant(),
                ["counterMetric"] = def.Counter,
                ["gated"] = def.Gated,
                ["definition"] = def.Description,
                ["byProfile"] = byProfile,
            });
        }
        root["metrics"] = metrics;

        var scenarios = new JsonArray();
        foreach (var l in r.Corpus)
        {
            var byProfile = new JsonObject();
            foreach (var p in r.Profiles)
            {
                if (!p.Ran) { byProfile[p.Profile.Name] = p.Status; continue; }
                if (l.Scenario.Skip is not null) { byProfile[p.Profile.Name] = "skipped:unimplemented"; continue; }
                var grades = p.Grades.Where(g => g.ScenarioId == l.Id).ToList();
                byProfile[p.Profile.Name] = new JsonObject
                {
                    ["status"] = Evaluation.ScenarioStatus(p.Grades, p.Errors, l.Id),
                    ["runs"] = grades.Count,
                    ["runsPassed"] = grades.Count(g => g.Passed),
                    ["failedAssertions"] = new JsonArray(grades.SelectMany(g => g.Failures).Select(f => f.Id).Distinct().Order(StringComparer.Ordinal).Select(x => (JsonNode)x!).ToArray()),
                };
            }
            scenarios.Add(new JsonObject { ["id"] = l.Id, ["class"] = l.Scenario.Class, ["gate"] = l.Scenario.Gate, ["seeds"] = l.Scenario.EffectiveSeeds.Count, ["profiles"] = byProfile });
        }
        root["scenarios"] = scenarios;

        var usage = new JsonObject();
        foreach (var p in ran)
        {
            var t = Evaluation.Tokens(p.Grades);
            var inTok = p.Grades.Sum(g => g.Usage.InputTokens);
            var outTok = p.Grades.Sum(g => g.Usage.OutputTokens);
            var cost = r.Prices?.CostOf(p.Profile.ModelId, inTok, outTok);
            usage[p.Profile.Name] = new JsonObject
            {
                ["tokensPerInterviewMean"] = t is null ? null : Math.Round(t.Value.Mean, 1),
                ["tokensPerInterviewMin"] = t?.Min,
                ["tokensPerInterviewMax"] = t?.Max,
                ["modelCallsTotal"] = p.Grades.Sum(g => g.Usage.ModelCalls),
                ["inputTokens"] = inTok,
                ["outputTokens"] = outTok,
                ["cost"] = cost is { } c ? new JsonObject { ["amount"] = Math.Round(c, 6), ["currency"] = r.Prices!.Currency, ["priceTableAsOf"] = r.Prices.As_Of, ["priceSource"] = r.Prices.Source } : "not computed: no price for this model in a user-supplied price table (--prices); tokens are reported instead",
            };
        }
        root["usage"] = usage;

        root["layer2"] = Layer2Json(r.Layer2);
        if (!r.Deterministic)
        {
            var lat = new JsonObject { ["note"] = "VOLATILE: wall time on this machine; with the mock it measures the harness, not a model. Never gated." };
            foreach (var p in ran)
            {
                var roles = new JsonObject();
                foreach (var g in p.Grades.SelectMany(x => x.Latency.ChatMsByRole).GroupBy(x => x.Key))
                {
                    var all = g.SelectMany(x => x.Value).Order().ToList();
                    roles[g.Key] = new JsonObject { ["calls"] = all.Count, ["p50Ms"] = Math.Round(Percentile(all, 0.5), 3), ["p95Ms"] = Math.Round(Percentile(all, 0.95), 3) };
                }
                var sessions = p.Grades.Select(x => x.Latency.SessionMs).Order().ToList();
                lat[p.Profile.Name] = new JsonObject { ["sessionP50Ms"] = Math.Round(Percentile(sessions, 0.5), 3), ["sessionP95Ms"] = Math.Round(Percentile(sessions, 0.95), 3), ["chatByRole"] = roles };
            }
            root["volatile"] = lat;
        }
        return root;
    }

    private static string ClassOf(ReportInput r, RunGrade g) => r.Corpus.First(l => l.Id == g.ScenarioId).Scenario.Class;

    private static double Percentile(IReadOnlyList<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1 < 0 ? 0 : (int)Math.Ceiling(p * sorted.Count) - 1)];

    public static JsonObject Cnt(Count c) => new()
    {
        ["k"] = c.K,
        ["n"] = c.N,
        ["rate"] = c.Rate is { } x ? Math.Round(x, 4) : null,
        ["low"] = c.Interval is { } i ? Math.Round(i.Low, 4) : null,
        ["high"] = c.Interval is { } j ? Math.Round(j.High, 4) : null,
    };

    private static JsonObject Layer2Json(Layer2Result l)
    {
        var o = new JsonObject
        {
            ["status"] = l.Status,
            ["judgeModelConfigured"] = l.JudgeModelConfigured,
            ["judgeModelAnswering"] = l.JudgeModelAnswering,
            ["rubricSha256"] = l.RubricSha,
            ["promptSha256"] = l.PromptSha,
            ["gating"] = l.Gate.Gating,
            ["calibration"] = l.Gate.Reason,
            ["labels"] = new JsonObject { ["present"] = l.Gate.AllLabels, ["countedAsHuman"] = l.Gate.HumanLabels },
            ["ruleScreensVsAuthorLabels"] = new JsonArray(l.RuleScreens.Select(a => (JsonNode)a.ToString()).ToArray()),
        };
        if (l.JudgeAgreement is { } a) o["judgeVsAuthorLabels"] = a.ToString();
        var scores = new JsonObject();
        foreach (var g in l.ValidScores.GroupBy(s => s.Rubric))
        {
            var vals = g.Select(s => s.Score.Score!.Value).ToList();
            var mi = Agreement.MeanInterval(vals);
            scores[g.Key] = new JsonObject
            {
                ["n"] = vals.Count,
                ["mean"] = mi is null ? null : Math.Round(mi.Value.Mean, 3),
                ["low"] = mi is null ? null : Math.Round(mi.Value.Low, 3),
                ["high"] = mi is null ? null : Math.Round(mi.Value.High, 3),
                ["levels"] = new JsonObject(vals.GroupBy(v => v).OrderBy(x => x.Key).Select(x => KeyValuePair.Create(x.Key.ToString(CultureInfo.InvariantCulture), (JsonNode?)x.Count()))),
            };
        }
        o["scores"] = scores;
        o["invalidJudgeAnswers"] = l.Items.Count(i => i.Score.Score is null);
        o["injectionDifferential"] = new JsonArray(l.InjectionChecks.Select(c => (JsonNode)new JsonObject { ["scenario"] = c.ScenarioId, ["seed"] = c.Seed, ["items"] = c.Items, ["scoreMoved"] = c.Moved }).ToArray());
        return o;
    }

    // ---- markdown ---------------------------------------------------------------------------------------------------------

    public static string ToMarkdown(ReportInput r)
    {
        var ran = r.Profiles.Where(p => p.Ran).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Conformance report");
        sb.AppendLine();
        sb.AppendLine($"Spec version **{r.SpecVersion}**, corpus digest `{r.CorpusDigest}`, harness {Baseline.HarnessVersion}. Command: `{r.Command}`.");
        sb.AppendLine("Unit of evaluation: the interview artifact (one scenario and seed). There is no composite score and no ranking of profiles.");
        sb.AppendLine();
        sb.AppendLine("> **What this run does and does not show.** " + MockLimits);
        sb.AppendLine();
        sb.AppendLine("## Profiles");
        sb.AppendLine();
        sb.AppendLine("| Profile | Model id | Status | Runs | Harness errors |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var p in r.Profiles) sb.AppendLine($"| `{p.Profile.Name}` | `{p.Profile.ModelId}` | {p.Status} | {p.Grades.Count} | {p.Errors.Count} |");

        sb.AppendLine();
        sb.AppendLine("## Hard constraints (must hold in 100% of runs)");
        sb.AppendLine();
        sb.AppendLine("Cells are `fail / pass / not-applicable` over all runs. Any non-zero fail is a failed constraint.");
        sb.AppendLine();
        sb.AppendLine("| Constraint | " + string.Join(" | ", ran.Select(p => $"`{p.Profile.Name}`")) + " |");
        sb.AppendLine("|---|" + string.Concat(ran.Select(_ => "---|")));
        foreach (var id in ConstraintIds)
        {
            sb.Append($"| {id} |");
            foreach (var p in ran)
            {
                var res = p.Grades.SelectMany(g => g.Assertions).Where(a => a.Id == "L1." + id).ToList();
                sb.Append($" {res.Count(a => a.Verdict == Verdict.Fail)} / {res.Count(a => a.Verdict == Verdict.Pass)} / {res.Count(a => a.Verdict == Verdict.NotApplicable)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## Behaviour metrics (k/n, rate, 95% Wilson interval)");
        sb.AppendLine();
        sb.AppendLine("Read each metric with its counter-metric. `n` is the number of observations in the denominator, not the number of runs. Overlapping intervals are not different.");
        sb.AppendLine();
        sb.AppendLine("| Metric | Counter | " + string.Join(" | ", ran.Select(p => $"`{p.Profile.Name}`")) + (ran.Count > 1 ? " | Distinguishable? |" : " |"));
        sb.AppendLine("|---|---|" + string.Concat(ran.Select(_ => "---|")) + (ran.Count > 1 ? "---|" : ""));
        foreach (var def in MetricCatalog.All)
        {
            var counts = ran.Select(p => Evaluation.Pool(p.Grades, def.Id)).ToList();
            sb.Append($"| {def.Title} | {def.Counter ?? "-"} |");
            foreach (var c in counts) sb.Append($" {c} |");
            if (ran.Count > 1) sb.Append(counts.Count(c => c.N > 0) < 2 ? " n/a |" : counts.Skip(1).All(c => Count.Indistinguishable(counts[0], c)) ? " no (intervals overlap) |" : " yes (intervals do not overlap) |");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("### By scenario class");
        sb.AppendLine();
        foreach (var p in ran)
        {
            sb.AppendLine($"`{p.Profile.Name}`");
            sb.AppendLine();
            sb.AppendLine("| Metric | " + string.Join(" | ", Corpus.Classes) + " |");
            sb.AppendLine("|---|" + string.Concat(Corpus.Classes.Select(_ => "---|")));
            foreach (var def in MetricCatalog.All)
            {
                var cells = Corpus.Classes.Select(cls => Evaluation.Pool(p.Grades, def.Id, g => ClassOf(r, g) == cls)).ToList();
                if (cells.All(c => c.N == 0)) continue;
                sb.AppendLine($"| {def.Id} | " + string.Join(" | ", cells.Select(c => c.N == 0 ? "-" : c.ToString())) + " |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Scenarios");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Class | Gate | Runs | " + string.Join(" | ", r.Profiles.Select(p => $"`{p.Profile.Name}`")) + " |");
        sb.AppendLine("|---|---|---|---|" + string.Concat(r.Profiles.Select(_ => "---|")));
        foreach (var l in r.Corpus)
        {
            sb.Append($"| {l.Id} | {l.Scenario.Class} | {l.Scenario.Gate} | {l.Scenario.EffectiveSeeds.Count} |");
            foreach (var p in r.Profiles)
            {
                if (!p.Ran) { sb.Append($" {p.Status.Split(' ')[0]} |"); continue; }
                if (l.Scenario.Skip is not null) { sb.Append(" skipped:unimplemented |"); continue; }
                var g = p.Grades.Where(x => x.ScenarioId == l.Id).ToList();
                var failed = g.SelectMany(x => x.Failures).Select(f => f.Id).Distinct().Order(StringComparer.Ordinal).ToList();
                sb.Append($" {Evaluation.ScenarioStatus(p.Grades, p.Errors, l.Id)} ({g.Count(x => x.Passed)}/{g.Count}){(failed.Count > 0 ? " " + string.Join(", ", failed) : "")} |");
            }
            sb.AppendLine();
        }

        var failures = ran.SelectMany(p => p.Grades.SelectMany(g => g.Failures.Select(f => (Profile: p.Profile.Name, g.ScenarioId, g.Seed, f)))).ToList();
        if (failures.Count > 0 || ran.Any(p => p.Errors.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine("## Failed assertions and harness errors");
            sb.AppendLine();
            sb.AppendLine("Messages carry counts and ids only, never interview text.");
            sb.AppendLine();
            foreach (var (profile, id, seed, f) in failures) sb.AppendLine($"- `{profile}` {id}#{seed} **{f.Id}**: {f.Message}");
            foreach (var p in ran) foreach (var e in p.Errors) sb.AppendLine($"- `{p.Profile.Name}` {e.ScenarioId}#{e.Seed} **harness error** {e.ExceptionType} (never counted as a pass or as a caught failure)");
        }

        sb.AppendLine();
        sb.AppendLine("## Tokens and cost");
        sb.AppendLine();
        sb.AppendLine("| Profile | Tokens per interview (mean, min, max) | Model calls | Cost |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var p in ran)
        {
            var t = Evaluation.Tokens(p.Grades);
            var cost = r.Prices?.CostOf(p.Profile.ModelId, p.Grades.Sum(g => g.Usage.InputTokens), p.Grades.Sum(g => g.Usage.OutputTokens));
            sb.AppendLine($"| `{p.Profile.Name}` | {(t is null ? "-" : $"{t.Value.Mean:0.#}, {t.Value.Min}, {t.Value.Max}")} | {p.Grades.Sum(g => g.Usage.ModelCalls)} | {(cost is { } c ? c.ToString("0.0000", CultureInfo.InvariantCulture) + " " + r.Prices!.Currency + " (price table as of " + r.Prices.As_Of + ")" : "not computed: no price table for this model (`--prices`); tokens are reported instead")} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Layer 2 (judge)");
        sb.AppendLine();
        var l2 = r.Layer2;
        sb.AppendLine($"- Status: **{l2.Status}**");
        sb.AppendLine($"- Rubric SHA-256 `{l2.RubricSha[..16]}`, judge prompt SHA-256 `{l2.PromptSha[..16]}`; judge model configured: {l2.JudgeModelConfigured ?? "none"}; answering: {l2.JudgeModelAnswering ?? "n/a"}");
        sb.AppendLine($"- Calibration: {l2.Gate.Reason}");
        foreach (var s in l2.RuleScreens) sb.AppendLine($"- {s}");
        if (l2.JudgeAgreement is { } ja) sb.AppendLine($"- {ja}");
        foreach (var g in l2.ValidScores.GroupBy(s => s.Rubric))
        {
            var vals = g.Select(s => s.Score.Score!.Value).ToList();
            var mi = Agreement.MeanInterval(vals)!.Value;
            sb.AppendLine($"- {g.Key}: mean {mi.Mean:0.00} [{mi.Low:0.00}, {mi.High:0.00}] over n={vals.Count} (levels: {string.Join(", ", vals.GroupBy(v => v).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Count()}"))}); advisory only")
                ;
        }
        foreach (var c in l2.InjectionChecks) sb.AppendLine($"- Judge-injection differential, {c.ScenarioId}#{c.Seed}: {c.Moved} of {c.Items} scores moved when the instruction sentences were removed");

        if (!r.Deterministic)
        {
            sb.AppendLine();
            sb.AppendLine("## Latency (volatile; not gated)");
            sb.AppendLine();
            sb.AppendLine("Wall time on this machine. With the mock this measures the harness, not a model.");
            sb.AppendLine();
            sb.AppendLine("| Profile | Session p50 / p95 (ms) | Model call p50 / p95 (ms), by role |");
            sb.AppendLine("|---|---|---|");
            foreach (var p in ran)
            {
                var sessions = p.Grades.Select(x => x.Latency.SessionMs).Order().ToList();
                var roles = p.Grades.SelectMany(x => x.Latency.ChatMsByRole).GroupBy(x => x.Key).OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(g => { var a = g.SelectMany(x => x.Value).Order().ToList(); return $"{g.Key}: {Percentile(a, .5):0.000} / {Percentile(a, .95):0.000}"; });
                sb.AppendLine($"| `{p.Profile.Name}` | {Percentile(sessions, .5):0.0} / {Percentile(sessions, .95):0.0} | {string.Join("; ", roles)} |");
            }
        }

        var skipped = r.Profiles.Where(p => !p.Ran).ToList();
        if (skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Skipped profiles");
            sb.AppendLine();
            foreach (var p in skipped) sb.AppendLine($"- `{p.Profile.Name}`: {p.Status}");
        }
        return sb.ToString();
    }

    public static string Serialize(JsonObject o) => o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
}
