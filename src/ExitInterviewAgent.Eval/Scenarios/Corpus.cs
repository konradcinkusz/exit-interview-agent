using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer2;
using ExitInterviewAgent.Personas;

namespace ExitInterviewAgent.Eval.Scenarios;

public sealed record CorpusReport(IReadOnlyList<string> Errors, IReadOnlyDictionary<string, int> PerClass, int Scenarios, int Runs)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// The rules the schema cannot express, enforced on every build (a test runs them, and so does <c>eval validate</c>): no empty class, ids agree with
/// file names and classes, the two-assertion rule, spec citations resolve in both directions, every persona reply has a hand label, faults are
/// well formed. It cannot catch a citation aimed at a scenario that exists and tests something else; a green validator is evidence of what it
/// checks and of nothing more.
/// </summary>
public static partial class Corpus
{
    public static readonly string[] Classes = ["happy", "ambiguity", "hostile", "adversarial", "degradation", "consent"];
    private static readonly Dictionary<string, string> Prefix = new() { ["hap"] = "happy", ["amb"] = "ambiguity", ["hos"] = "hostile", ["adv"] = "adversarial", ["deg"] = "degradation", ["con"] = "consent" };

    public static string ShortId(string id) => ShortIdPattern().Match(id).Value;

    [GeneratedRegex(@"^[a-z]{3}-[0-9]{3}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex ShortIdPattern();

    public static string SpecVersion(string? specText = null)
    {
        var m = SpecVersionPattern().Match(specText ?? File.ReadAllText(RepoLayout.SpecPath));
        return m.Success ? m.Groups[1].Value : throw new InvalidDataException("docs/eval/SPEC.md has no 'Spec version: x.y.z' line.");
    }

    [GeneratedRegex(@"\*\*Spec version:\s*([0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex SpecVersionPattern();

    public static CorpusReport Validate(IReadOnlyList<LoadedScenario> corpus)
    {
        var errors = new List<string>();
        void Err(string id, string msg) => errors.Add($"{id}: {msg}");

        foreach (var cls in Classes)
            if (!corpus.Any(s => s.Scenario.Class == cls)) errors.Add($"class '{cls}' has no scenario (a suite without it is testing the demo)");

        foreach (var l in corpus)
        {
            var s = l.Scenario;
            var dir = Path.GetFileName(Path.GetDirectoryName(l.Path)!);
            if (Path.GetFileNameWithoutExtension(l.Path) != s.Id) Err(s.Id, "id does not match the file name");
            if (dir != s.Class) Err(s.Id, $"lives in '{dir}' but is class '{s.Class}'");
            if (!Prefix.TryGetValue(s.Id[..3], out var cls) || cls != s.Class) Err(s.Id, "id prefix disagrees with class");
            if (!PersonaCatalog.TryGet(s.Persona, out var persona)) { Err(s.Id, $"unknown persona '{s.Persona}'"); continue; }
            if (s.Class is "adversarial" or "consent" && s.Gate != "constraint") Err(s.Id, $"class {s.Class} must be gated as constraint");
            if (s.Class is "adversarial" or "consent" && (s.Expect.Absent is null || s.Expect.Absent.IsEmpty))
                Err(s.Id, $"class {s.Class} needs at least one absence assertion in expect.absent (the two-assertion rule)");
            if (s.Class == "degradation" && s.Expect.Record == "absent" && (s.Expect.Absent is null || s.Expect.Absent.IsEmpty)) Err(s.Id, "a degradation scenario that expects no record must say which spans are absent");
            if (s.Class != "adversarial" && s.Class != "consent" && s.Class != "degradation" && s.Expect.Record == "absent") Err(s.Id, "only consent, adversarial and degradation scenarios may expect no record");
            if (s.Control is not null && persona!.InjectionTargets is not { Count: > 0 }) Err(s.Id, "control run requested for a persona with no injection targets");
            if (s.Class == "degradation" && (s.Faults is null || s.Faults.Count == 0)) Err(s.Id, "a degradation scenario must inject a fault");

            foreach (var f in s.Faults ?? [])
            {
                if (f.Kind == "compromised")
                {
                    if (f.Variants is not { Count: > 0 }) { Err(s.Id, "a compromised fault needs variants"); continue; }
                    var interviewerSide = f.Roles.All(r => r is "interviewer" or "prober");
                    var extractorSide = f.Roles.All(r => r == "extractor");
                    if (!interviewerSide && !extractorSide) Err(s.Id, "a compromised fault targets interviewer/prober roles or the extractor, not both");
                    foreach (var v in f.Variants)
                    {
                        var known = interviewerSide ? FaultInjectingChatClient.InterviewerVariants.ContainsKey(v) : v is "obeys_injection" or "fabricates" or "affect_field" or "pii_quote";
                        if (!known) Err(s.Id, $"variant '{v}' does not apply to roles [{string.Join(",", f.Roles)}]");
                    }
                }
                else if (f.Variants is not null) Err(s.Id, "variants only apply to kind compromised");
                if (f.Kind == "usage_inflated" && f.Tokens is null) Err(s.Id, "usage_inflated needs tokens");
                if (f.Kind == "malformed_json" && !f.Roles.All(r => r == "extractor")) Err(s.Id, "malformed_json only makes sense for the extractor");
            }
        }

        var dup = corpus.GroupBy(l => ShortId(l.Id)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var d in dup) errors.Add($"short id '{d}' is used twice");

        ValidateSpecCitations(corpus, errors);
        ValidateRubricIds(corpus, errors);
        ValidateLabels(errors);

        var perClass = Classes.ToDictionary(c => c, c => corpus.Count(l => l.Scenario.Class == c));
        return new CorpusReport(errors, perClass, corpus.Count, corpus.Sum(l => l.Scenario.EffectiveSeeds.Count));
    }

    private static void ValidateSpecCitations(IReadOnlyList<LoadedScenario> corpus, List<string> errors)
    {
        var rows = SpecRows();
        var byShort = corpus.ToDictionary(l => ShortId(l.Id), l => l.Scenario);
        foreach (var (id, cited) in rows)
        {
            foreach (var sid in cited)
                if (!byShort.ContainsKey(sid)) errors.Add($"SPEC.md row {id} cites scenario '{sid}' which does not exist");
            if (cited.Count == 0 && id != "B-10") errors.Add($"SPEC.md row {id} cites no scenario");
        }
        foreach (var l in corpus)
            foreach (var spec in l.Scenario.Spec)
            {
                if (!rows.TryGetValue(spec, out var cited)) errors.Add($"{l.Id}: spec id {spec} does not exist in SPEC.md");
                else if (spec != "B-10" && !cited.Contains(ShortId(l.Id))) errors.Add($"{l.Id}: claims {spec} but the SPEC.md row for {spec} does not cite it");
            }
        foreach (var (id, cited) in rows.Where(r => r.Key != "B-10"))
            foreach (var sid in cited)
                if (byShort.TryGetValue(sid, out var s) && !s.Spec.Contains(id)) errors.Add($"SPEC.md says {sid} proves {id} but the scenario does not list {id}");
    }

    /// <summary>Spec id to the short scenario ids its row cites (the last cell of every <c>| C-xx |</c> and <c>| B-xx |</c> table row).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> SpecRows(string? specText = null)
    {
        var rows = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var line in (specText ?? File.ReadAllText(RepoLayout.SpecPath)).Split('\n'))
        {
            var m = SpecRowPattern().Match(line);
            if (!m.Success) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            rows[m.Groups[1].Value] = ShortIdsPattern().Matches(cells[^1]).Select(x => x.Value).Distinct().ToList();
        }
        return rows;
    }

    [GeneratedRegex(@"^\|\s*((?:C|B)-[0-9]{2})\s*\|", RegexOptions.CultureInvariant, 200)]
    private static partial Regex SpecRowPattern();

    [GeneratedRegex(@"\b[a-z]{3}-[0-9]{3}\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex ShortIdsPattern();

    private static void ValidateRubricIds(IReadOnlyList<LoadedScenario> corpus, List<string> errors)
    {
        var known = Rubric.Load().Criteria.Select(c => c.Id).ToHashSet();
        foreach (var l in corpus)
            foreach (var r in l.Scenario.Rubrics ?? [])
                if (!known.Contains(r)) errors.Add($"{l.Id}: unknown rubric '{r}'");
    }

    private static void ValidateLabels(List<string> errors)
    {
        var index = LabelSets.ReplyIndex(LabelSets.LoadVagueness());
        var file = LabelSets.LoadVagueness();
        foreach (var r in file.Replies.Where(r => r.Label is not ("vague" or "specific" or "decline"))) errors.Add($"vagueness label '{r.Label}' is not vague|specific|decline");
        foreach (var r in file.Pairs.Where(r => r.Label is not ("contradiction" or "consistent"))) errors.Add($"pair label '{r.Label}' is not contradiction|consistent");
        foreach (var r in file.Replies.Where(r => string.IsNullOrWhiteSpace(r.Rationale))) errors.Add("a vagueness label has no written rationale");
        foreach (var persona in PersonaCatalog.All)
        {
            var r = persona.Responses;
            var texts = new[] { r.Topics, r.Probes, r.Clarifications, r.Redirects }.Where(d => d is not null).SelectMany(d => d!.Values).SelectMany(x => x);
            var missing = texts.Count(t => !index.ContainsKey(LabelSets.Key(t)));
            if (missing > 0) errors.Add($"persona '{persona.Id}' has {missing} reply text(s) without a hand label in evals/labels/vagueness.yaml");
        }
    }

    /// <summary>
    /// A digest of everything that decides what a run measures: the scenarios' semantic content (canonical JSON, so key order and whitespace do
    /// not move it), the label sets, the rubric and judge prompt, the spec version, and the persona and protocol data the scenarios run against.
    /// Rewording a <c>why</c> does move it (it is part of the scenario); that is deliberate: the baseline is re-recorded with a justification
    /// whenever what is measured may have changed, and a reviewer reads the justification.
    /// </summary>
    public static string Digest(IReadOnlyList<LoadedScenario> corpus)
    {
        var sb = new StringBuilder();
        sb.Append("spec:").Append(SpecVersion()).Append('\n');
        foreach (var l in corpus.OrderBy(x => x.Id, StringComparer.Ordinal)) sb.Append(l.CanonicalJson).Append('\n');
        foreach (var f in new[] { LabelSets.VaguenessPath, LabelSets.JudgePath, Path.Combine(RepoLayout.RubricsDir, "judge.yaml"), Path.Combine(RepoLayout.RubricsDir, "judge-prompt.md"), RepoLayout.SchemaPath })
            sb.Append(Path.GetFileName(f)).Append(':').Append(File.Exists(f) ? Sha(File.ReadAllText(f)) : "missing").Append('\n');
        foreach (var persona in PersonaCatalog.All) sb.Append(persona.Id).Append(':').Append(Sha(System.Text.Json.JsonSerializer.Serialize(persona))).Append('\n');
        sb.Append("protocol:").Append(Sha(System.Text.Json.JsonSerializer.Serialize(Agent.Protocol.InterviewProtocol.Current.Limits))).Append(Sha(Agent.Protocol.InterviewProtocol.Current.Opening));
        return "sha256:" + Sha(sb.ToString())[..16];
    }

    public static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
