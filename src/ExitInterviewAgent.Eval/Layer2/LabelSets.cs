using ExitInterviewAgent.Eval.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExitInterviewAgent.Eval.Layer2;

public sealed class ReplyLabel
{
    public string Text { get; set; } = "";
    public string Label { get; set; } = "";
    public string Rationale { get; set; } = "";
}

public sealed class PairLabel
{
    public string Earlier { get; set; } = "";
    public string Later { get; set; } = "";
    public string Label { get; set; } = "";
    public string Rationale { get; set; } = "";
}

/// <summary>Hand labels of interviewee replies (vague / specific / decline) and of reply pairs (contradiction / consistent). Synthetic text only.</summary>
public sealed class VaguenessLabelFile
{
    public string Version { get; set; } = "";
    public string Labeller { get; set; } = "";
    public string Labeller_Kind { get; set; } = "";
    public string Definition { get; set; } = "";
    public string Provenance { get; set; } = "";
    public List<ReplyLabel> Replies { get; set; } = [];
    public List<PairLabel> Pairs { get; set; } = [];
}

/// <summary>One item to be judged for a Layer 2 criterion: a question (and what it follows), with a hand label and a written rationale.</summary>
public sealed class JudgeItem
{
    public string Id { get; set; } = "";
    public string Rubric { get; set; } = "";
    public string Question { get; set; } = "";
    public string Preceded_By { get; set; } = "";
    public int Label { get; set; }
    public string Rationale { get; set; } = "";
}

public sealed class JudgeLabelFile
{
    public string Version { get; set; } = "";
    public string Labeller { get; set; } = "";
    public string Labeller_Kind { get; set; } = "";
    public string Provenance { get; set; } = "";
    public List<JudgeItem> Items { get; set; } = [];
}

public static class LabelSets
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();

    public static string VaguenessPath => Path.Combine(RepoLayout.LabelsDir, "vagueness.yaml");
    public static string JudgePath => Path.Combine(RepoLayout.LabelsDir, "judge.yaml");

    public static VaguenessLabelFile LoadVagueness(string? path = null) => Yaml.Deserialize<VaguenessLabelFile>(File.ReadAllText(path ?? VaguenessPath));

    public static JudgeLabelFile LoadJudge(string? path = null) => Yaml.Deserialize<JudgeLabelFile>(File.ReadAllText(path ?? JudgePath));

    /// <summary>Whitespace-normalised key, so a label matches the text however the YAML wrapped it.</summary>
    public static string Key(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static IReadOnlyDictionary<string, string> ReplyIndex(VaguenessLabelFile file) =>
        file.Replies.GroupBy(r => Key(r.Text)).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);
}
