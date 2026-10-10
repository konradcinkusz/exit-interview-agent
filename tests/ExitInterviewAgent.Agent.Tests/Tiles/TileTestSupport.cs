using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

/// <summary>Golden records and small record edits for the tile tests. Records come through the public validator, as in production.</summary>
internal static class TileTestSupport
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExitInterviewAgent.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>The golden records owned by the Records tests: the same files the validator is proven against.</summary>
    public static IEnumerable<object[]> GoldenRecords() =>
        Directory.GetFiles(Path.Combine(RepoRoot(), "tests", "ExitInterviewAgent.Records.Tests", "Fixtures", "valid"), "*.json")
            .Order()
            .Select(f => new object[] { Path.GetFileName(f) });

    public static InterviewRecord Golden(string name)
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "ExitInterviewAgent.Records.Tests", "Fixtures", "valid", name));
        var outcome = new RecordValidator().Validate(json);
        return outcome.Record ?? throw new InvalidOperationException($"Golden record {name} did not validate.");
    }

    public static InterviewRecord Full() => Golden("full.json");

    public static InterviewRecord WithTopic(InterviewRecord r, Topic topic, TopicEntry entry) =>
        r with { Topics = Set(r.Topics, topic, entry) };

    public static InterviewRecord WithLanguage(InterviewRecord r, string language) =>
        r with { Interview = r.Interview with { Language = language } };

    private static TopicSet Set(TopicSet s, Topic t, TopicEntry e) => t switch
    {
        Topic.Onboarding => s with { Onboarding = e },
        Topic.Management => s with { Management = e },
        Topic.Growth => s with { Growth = e },
        Topic.PayVsPromises => s with { PayVsPromises = e },
        Topic.Culture => s with { Culture = e },
        _ => s with { ReasonForLeaving = e },
    };
}
