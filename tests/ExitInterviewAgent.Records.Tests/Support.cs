using System.Text.Json;

namespace ExitInterviewAgent.Records.Tests;

internal static class Fixtures
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Read(string relative) => File.ReadAllText(Path.Combine(Dir, relative));

    public static IEnumerable<object[]> Valid() =>
        Directory.GetFiles(Path.Combine(Dir, "valid"), "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    public static IReadOnlyDictionary<string, string[]> InvalidExpectations() =>
        JsonSerializer.Deserialize<Dictionary<string, string[]>>(Read("invalid/expectations.json"))!;

    public static IEnumerable<object[]> InvalidCases() => InvalidExpectations().Keys.Order().Select(k => new object[] { k });

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExitInterviewAgent.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static InterviewRecord Sample(Func<InterviewRecord, InterviewRecord>? change = null)
    {
        var record = new RecordValidator().Validate(Read("valid/full.json")).Record!;
        return change is null ? record : change(record);
    }
}
