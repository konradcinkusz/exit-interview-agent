using System.Globalization;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Layer2;

public sealed record AgreementReport(string Name, int N, int Exact, int WithinOne, double? Kappa, double? KappaLow, double? KappaHigh, string Note)
{
    public static AgreementReport From(string name, IReadOnlyList<(int A, int B)> pairs, string note)
    {
        var ci = Agreement.KappaInterval(pairs);
        return new AgreementReport(name, pairs.Count, pairs.Count(p => p.A == p.B), pairs.Count(p => Math.Abs(p.A - p.B) <= 1), Agreement.Kappa(pairs), ci?.Low, ci?.High, note);
    }

    public override string ToString()
    {
        var kappa = Kappa is { } k ? k.ToString("F2", CultureInfo.InvariantCulture) : "undefined";
        var ci = KappaLow is { } l && KappaHigh is { } h ? $" 95% bootstrap [{l.ToString("F2", CultureInfo.InvariantCulture)}, {h.ToString("F2", CultureInfo.InvariantCulture)}]" : "";
        return $"{Name}: n={N} exact={Exact}/{N} within-one={WithinOne}/{N} kappa={kappa}{ci}" + (Note.Length > 0 ? $" ({Note})" : "");
    }
}

/// <summary>Whether the judge's scores may gate anything, and why not. Printed on every run: an uncalibrated judge that silently gates is worse than none.</summary>
public sealed record GateDecision(bool Gating, int HumanLabels, int AllLabels, int Items, double? Kappa, string Reason);

/// <summary>
/// Calibration arithmetic: the judge-versus-label agreement (when a judge is available), the rule screens' agreement with the same labels (always
/// computable offline), and the gate that decides whether judge scores may block anything. Labels written by the harness's own author are
/// reported as such and never count towards the gate; a model as first rater is a rehearsal (docs/eval/SPEC.md §9).
/// </summary>
public static class Calibration
{
    public static GateDecision Decide(JudgeLabelFile labels, Rubric rubric, double? kappa)
    {
        var gate = rubric.Calibration;
        var humanKind = string.Equals(labels.Labeller_Kind, "human", StringComparison.OrdinalIgnoreCase);
        var owner = !string.IsNullOrWhiteSpace(gate.Owner_Handle) && string.Equals(labels.Labeller, gate.Owner_Handle, StringComparison.OrdinalIgnoreCase);
        var human = humanKind && owner ? labels.Items.Count : 0;
        var items = labels.Items.Select(i => i.Id).Distinct().Count();
        var reasons = new List<string>();
        if (human < gate.Minimum_Labels)
            reasons.Add(string.IsNullOrWhiteSpace(gate.Owner_Handle)
                ? $"0 of {gate.Minimum_Labels} human labels (no owner handle configured; the {labels.Items.Count} label(s) present are labeller_kind '{labels.Labeller_Kind}', so none count)"
                : $"{human} of {gate.Minimum_Labels} labels from the owner's handle '{gate.Owner_Handle}' ({labels.Items.Count} present, kind '{labels.Labeller_Kind}')");
        if (items < gate.Minimum_Items) reasons.Add($"{items} of {gate.Minimum_Items} items");
        if (kappa is null) reasons.Add("kappa not computed (no judge ran, or it is undefined)");
        else if (kappa < gate.Minimum_Kappa) reasons.Add(FormattableString.Invariant($"kappa {kappa:F2} below the required {gate.Minimum_Kappa:F2}"));
        var gating = reasons.Count == 0;
        return new GateDecision(gating, human, labels.Items.Count, items, kappa,
            gating ? "Calibrated: Layer 2 scores may gate." : "NOT calibrated: " + string.Join("; ", reasons) + ". Layer 2 scores are reported and block nothing.");
    }

    /// <summary>The offline, reproducible half: how far each rule screen agrees with the hand labels (binary: acceptable = level 2).</summary>
    public static IReadOnlyList<AgreementReport> RuleScreensVsLabels(JudgeLabelFile labels)
    {
        var reports = new List<AgreementReport>();
        foreach (var rubric in new[] { "R-01", "R-02" })
        {
            var items = labels.Items.Where(i => i.Rubric == rubric).ToList();
            var pairs = items.Select(i => (Human: i.Label == 2 ? 1 : 0, Screen: Screens.Acceptable(rubric, i.Question) ? 1 : 0)).Select(p => (p.Human, p.Screen)).ToList();
            var fp = pairs.Count(p => p.Human == 0 && p.Screen == 1);
            var fn = pairs.Count(p => p.Human == 1 && p.Screen == 0);
            var note = rubric == "R-01"
                ? $"screen = QuestionGuard.LeadingReason or IndependentRules; screen accepts a question the human rated below 2: {fp}; screen rejects one the human rated 2: {fn}"
                : $"screen = example-request pattern, one question, no bundling, no name, not leading; accepts a probe rated below 2: {fp}; rejects one rated 2: {fn}";
            reports.Add(AgreementReport.From($"{rubric} rule screen vs author labels (acceptable = level 2)", pairs, note));
        }
        return reports;
    }

    public static AgreementReport JudgeVsLabels(JudgeLabelFile labels, IReadOnlyDictionary<string, int?> judged, string rubric)
    {
        var pairs = labels.Items.Where(i => i.Rubric == rubric && judged.TryGetValue(i.Id, out var s) && s is not null).Select(i => (Judge: judged[i.Id]!.Value, Human: i.Label)).Select(p => (p.Judge, p.Human)).ToList();
        return AgreementReport.From($"{rubric} judge vs labels (3 levels)", pairs, "unweighted kappa");
    }
}

/// <summary>Rule screens used as the offline stand-in for the judge's two criteria. They are screens, not judges: lexical and structural only.</summary>
public static partial class Screens
{
    public static bool Acceptable(string rubric, string question) => rubric switch
    {
        "R-01" => QuestionGuard.LeadingReason(question) is null && !IndependentRules.IsLeading(question),
        "R-02" => ProbeAcceptable(question),
        _ => throw new ArgumentOutOfRangeException(nameof(rubric)),
    };

    private static bool ProbeAcceptable(string q) =>
        ExampleRequest().IsMatch(q) && q.Count(c => c == '?') == 1 && !Bundled().IsMatch(q) && !AsksNames().IsMatch(q) && !SuggestsContent().IsMatch(q)
        && QuestionGuard.LeadingReason(q) is null && !IndependentRules.IsLeading(q) && !GenericExample().IsMatch(q);

    [GeneratedRegex(@"\b(example|instance|specific|particular|moment|situation|concrete|case)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex ExampleRequest();

    [GeneratedRegex(@"\band\b\s+(also|was|were|did|do|how|what|why|tell|name)\b|,\s*and\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Bundled();

    [GeneratedRegex(@"\bnames?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex AsksNames();

    [GeneratedRegex(@"\b(worst|best|wrong|unfair|clearly)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex SuggestsContent();

    /// <summary>"Could you give an example?" with nothing tying it to what was said.</summary>
    [GeneratedRegex(@"^\W*(could|can)\s+you\s+give\s+(me\s+)?an?\s+example\s*\?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex GenericExample();
}

public sealed record ConfusionMatrix(IReadOnlyList<string> Classes, int[,] Cells)
{
    public int Total => Cells.Cast<int>().Sum();

    public int Row(int i) => Enumerable.Range(0, Classes.Count).Sum(j => Cells[i, j]);

    public int Col(int j) => Enumerable.Range(0, Classes.Count).Sum(i => Cells[i, j]);
}

public sealed record ClassifierReport(string Name, string Status, int N, ConfusionMatrix? Confusion, Count? Recall, Count? Precision, AgreementReport? Agreement, string Note);

/// <summary>
/// The experiment T4 recommended: how well does the rule-based reply analyser agree with hand labels on vagueness and on contradiction, and (when a
/// model is available) how does a model-assisted classifier compare on the same items? The analyser half is offline and reproducible; the model
/// half reports <c>skipped:no-credential</c> without a judge profile.
/// </summary>
public static class ClassifierExperiment
{
    public static readonly string[] Classes = ["vague", "specific", "decline"];

    public static string AnalyzerClass(string text)
    {
        var s = ReplyAnalyzer.Analyze(text, InterviewProtocol.Current.Limits, namesPerson: false);
        return s.Withdrawal || s.Terse || s.Hostile ? "decline" : s.Vague ? "vague" : "specific";
    }

    public static ClassifierReport VaguenessAnalyzer(VaguenessLabelFile labels) =>
        Score("reply analyser (rules) vs hand labels, vagueness", "ran", labels.Replies.Select(r => (r.Label, AnalyzerClass(r.Text))).ToList(),
            "classes: vague, specific, decline (terse, hostile and withdrawal map to decline)");

    public static ClassifierReport Score(string name, string status, IReadOnlyList<(string Human, string Predicted)> pairs, string note)
    {
        var idx = Classes.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var cells = new int[Classes.Length, Classes.Length];
        foreach (var (h, p) in pairs) cells[idx[h], idx[p]]++;
        var m = new ConfusionMatrix(Classes, cells);
        var v = idx["vague"];
        var recall = new Count(cells[v, v], m.Row(v));
        var precision = new Count(cells[v, v], m.Col(v));
        var agreement = AgreementReport.From(name, pairs.Select(p => (idx[p.Human], idx[p.Predicted])).ToList(), "unweighted kappa over the three classes");
        return new ClassifierReport(name, status, pairs.Count, m, recall, precision, agreement, note);
    }

    public static ClassifierReport ContradictionAnalyzer(VaguenessLabelFile labels)
    {
        var limits = InterviewProtocol.Current.Limits;
        var pairs = labels.Pairs.Select(p =>
        {
            var first = ReplyAnalyzer.Analyze(p.Earlier, limits, false);
            var second = ReplyAnalyzer.Analyze(p.Later, limits, false, first.Polarity);
            return (Human: p.Label == "contradiction" ? 1 : 0, Predicted: second.Contradiction ? 1 : 0);
        }).ToList();
        return ScoreBinary("reply analyser (polarity flip) vs hand labels, contradiction", "ran", pairs);
    }

    public static ClassifierReport ScoreBinary(string name, string status, IReadOnlyList<(int Human, int Predicted)> pairs)
    {
        var tp = pairs.Count(p => p.Human == 1 && p.Predicted == 1);
        return new ClassifierReport(name, status, pairs.Count, null, new Count(tp, pairs.Count(p => p.Human == 1)), new Count(tp, pairs.Count(p => p.Predicted == 1)),
            AgreementReport.From(name, pairs.Select(p => (p.Human, p.Predicted)).ToList(), "unweighted kappa, positive = contradiction"), "recall = contradictions found, precision = flagged pairs that are contradictions");
    }

    public static ClassifierReport Skipped(string name, string reason) => new(name, reason, 0, null, null, null, null, "not run");
}
