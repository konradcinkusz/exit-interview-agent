using ExitInterviewAgent.Eval.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExitInterviewAgent.Eval.Layer2;

public sealed class Criterion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Applies_To { get; set; } = "";
    public string Summary { get; set; } = "";
    public int Scale { get; set; }
    public double Threshold { get; set; }
    public Dictionary<int, string> Anchors { get; set; } = [];
}

public sealed class CalibrationGate
{
    public int Minimum_Labels { get; set; }
    public int Minimum_Items { get; set; }
    public double Minimum_Kappa { get; set; }
    public string Owner_Handle { get; set; } = "";
}

/// <summary>The judge's criteria and anchors, as data (evals/rubrics/judge.yaml). Every level needs an anchor: a rubric with a gap fails to load.</summary>
public sealed class Rubric
{
    public string Version { get; set; } = "";
    public CalibrationGate Calibration { get; set; } = new();
    public List<Criterion> Criteria { get; set; } = [];

    public static string Path => System.IO.Path.Combine(RepoLayout.RubricsDir, "judge.yaml");
    public static string PromptPath => System.IO.Path.Combine(RepoLayout.RubricsDir, "judge-prompt.md");

    public static Rubric Load(string? path = null)
    {
        var d = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        var r = d.Deserialize<Rubric>(File.ReadAllText(path ?? Path));
        foreach (var c in r.Criteria)
            for (var level = 0; level <= c.Scale; level++)
                if (!c.Anchors.TryGetValue(level, out var a) || string.IsNullOrWhiteSpace(a))
                    throw new InvalidDataException($"Criterion {c.Id} has no anchor for level {level}: a score without an anchor has no meaning to regress against.");
        return r;
    }

    public Criterion Get(string id) => Criteria.Single(c => c.Id == id);

    /// <summary>SHA-256 of the rubric file and the prompt template, recorded in every report: a score compared across an edit is a measuring stick that changed length.</summary>
    public static (string Rubric, string Prompt) Hashes() =>
        (Corpus.Sha(File.ReadAllText(Path)), Corpus.Sha(File.ReadAllText(PromptPath)));
}
