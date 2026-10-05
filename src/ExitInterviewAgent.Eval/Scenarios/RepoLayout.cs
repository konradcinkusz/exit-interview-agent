namespace ExitInterviewAgent.Eval.Scenarios;

/// <summary>Where the evals live. The repository root is found by walking up to the solution file, so the tool works from any directory.</summary>
public static class RepoLayout
{
    private static string? _root;

    public static string Root => _root ??= Find();

    public static string EvalsDir => Path.Combine(Root, "evals");
    public static string ScenariosDir => Path.Combine(EvalsDir, "scenarios");
    public static string SchemaPath => Path.Combine(EvalsDir, "schema", "scenario.schema.json");
    public static string LabelsDir => Path.Combine(EvalsDir, "labels");
    public static string RubricsDir => Path.Combine(EvalsDir, "rubrics");
    public static string BaselinePath => Path.Combine(EvalsDir, "baseline.json");
    public static string ProfilesPath => Path.Combine(EvalsDir, "profiles.yaml");
    public static string SpecPath => Path.Combine(Root, "docs", "eval", "SPEC.md");

    public static void Override(string root) => _root = root;

    private static string Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "ExitInterviewAgent.sln"))) return dir.FullName;
        throw new InvalidOperationException("Cannot find the repository root (ExitInterviewAgent.sln) from the working directory or the tool's directory.");
    }
}
