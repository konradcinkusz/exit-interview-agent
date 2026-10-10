using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.InterviewService.Tests.Contracts;

/// <summary>
/// The golden samples of the wire contract (web-app-plan §10). One sample per success response and the list of stable codes live
/// in <c>tests/contracts/*.json</c>, and the BFF's typed parser reads the same files (web/app/lib/interview-contract.test.ts). A
/// change to a shape or a code fails here until the file is updated on purpose:
/// <c>UPDATE_CONTRACT_SAMPLES=1 dotnet test --filter ContractSamplesTests</c>, then the diff is reviewed like any contract change.
/// </summary>
public sealed class ContractSamplesTests
{
    private const string UpdateVariable = "UPDATE_CONTRACT_SAMPLES";

    // The same options the minimal API writes responses with (camelCase, nulls written).
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string Dir = FindContractsDir();

    public static TheoryData<string, object> Samples()
    {
        var at = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        const string id = "int_sample0000000000000000";
        var topicCovered = new { status = "covered", rating = 2, confidence = "medium", quotes = new[] { "Onboarding was chaotic." } };
        var topicEmpty = new { status = "no_data", rating = (int?)null, confidence = (string?)null, quotes = Array.Empty<string>() };
        var recordJson = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            schemaVersion = "1",
            interviewId = id,
            piiMasked = true,
            context = new { tenureBand = "1y_3y" },
            interview = new { protocolVersion = "1.2", language = "en", aiDisclosed = true, durationBand = "10m_20m", turnBand = "10_20" },
            topics = new { onboarding = topicCovered, management = topicEmpty, growth = topicEmpty, pay_vs_promises = topicEmpty, culture = topicEmpty, reason_for_leaving = topicEmpty },
        })).RootElement.Clone();

        var tiles = new InterviewTiles(
            new List<InterviewTile> { new("glassdoor", "Onboarding was chaotic, but the team helped."), new("short_note", "Chaotic onboarding, good team.") },
            new List<InterviewDroppedTile> { new("too_long") },
            "These are draft texts, not facts.");
        var result = new InterviewResultResponse(recordJson, tiles, new InterviewUsage(4, 5400));

        var data = new TheoryData<string, object>
        {
            { "interview-started", new InterviewStarted(id, "in_progress", "en", at.AddMinutes(30), new InterviewTurn(0, "opening", "Hello. How did onboarding go?")) },
            { "interview-reply-topic", new InterviewReplyResponse("in_progress", new InterviewTurn(1, "topic", "Thank you. How was your manager?"), null) },
            // The closing turn: still in_progress and no ending; the status turns completed once the tiles are ready (plan §10).
            { "interview-reply-close", new InterviewReplyResponse("in_progress", new InterviewTurn(3, "close", "Thank you, that is all my questions."), null) },
            { "interview-reply-stopped", new InterviewReplyResponse("stopped", new InterviewTurn(2, "stop", "Understood. Nothing is kept."), new InterviewEnding("consent_withdrawn")) },
            { "interview-state", new InterviewState(id, "completed", "en", 4, at.AddMinutes(30)) },
            { "interview-result", result },
            { "credits", new CreditsResponse(1) },
            { "checkout", new CheckoutResponse("https://checkout.example.invalid/session/sample") },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Sample_matches_its_golden_file(string name, object value)
    {
        var actual = JsonNode.Parse(JsonSerializer.Serialize(value, Wire))!;
        var path = Path.Combine(Dir, name + ".json");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual.ToJsonString(Wire) + "\n");
            return;
        }

        Assert.True(File.Exists(path), $"Missing golden file {name}.json; run with {UpdateVariable}=1 and review it.");
        var expected = JsonNode.Parse(File.ReadAllText(path));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Contract drift in {name}.json. Expected {expected}, got {actual}.");
    }

    [Fact]
    public void Every_golden_file_has_a_sample()
    {
        var names = Samples().Select(row => (string)row[0]).ToHashSet();
        var files = Directory.GetFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n != "codes");
        Assert.Equal(names.OrderBy(n => n), files.OrderBy(n => n));
    }

    [Fact]
    public void Stable_codes_match_the_golden_list()
    {
        var declared = typeof(InterviewCodes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Concat(typeof(BillingCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var path = Path.Combine(Dir, "codes.json");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { codes = declared }, Wire) + "\n");
            return;
        }

        var golden = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("codes").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(golden.Order(StringComparer.Ordinal), declared);
    }

    private static string FindContractsDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "contracts");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests/contracts not found above the test binary.");
    }
}
