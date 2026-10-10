using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Protocol;

/// <summary>One of the six topics: the record topic, its wire id, and the neutral opening question.</summary>
public sealed record TopicSpec(Topic Topic, string Id, string Title, string Question);

/// <summary>Numeric bounds of an interview. Every one is enforced by <see cref="Machine.InterviewMachine"/> or the runner.</summary>
public sealed record ProtocolLimits(
    int MaxProbesPerTopic,
    int MaxDeepProbesPerTopic,
    int MaxClarificationsPerTopic,
    int MaxRedirectsPerTopic,
    int MaxConsentAsks,
    int MaxInterviewerTurns,
    int MaxModelCalls,
    int MaxEstimatedTokens,
    int MaxReplyChars,
    int TerseWordLimit,
    int TerseStreakToClose,
    int HostileToClose,
    int MinWordsForCoverage);

/// <summary>A numbered rule and how it is enforced: <c>code</c>, <c>prompt</c> or <c>both</c>.</summary>
public sealed record ProtocolRule(string Id, string Text, string Enforcement);

/// <summary>
/// The versioned interview protocol, loaded from the embedded <c>interview-protocol.v1.json</c> (English) or <c>interview-protocol.pl.v1.json</c>
/// (Polish, the same instrument; see <see cref="For"/>). Everything a client
/// must say verbatim lives here; the model only ever words the six topic questions, the probe, the clarification
/// and the redirect, and each of those has a fixed fallback below.
/// </summary>
public sealed class InterviewProtocol
{
    public string ProtocolVersion { get; }
    public string Language { get; }
    public string Opening { get; }
    public string ConsentReask { get; }
    public string Probe { get; }
    public string Clarification { get; }
    public string RedirectNames { get; }
    /// <summary>Said once, before the first deepening question of an interview (ADR-0075): that the interviewee may skip or stop.</summary>
    public string DeepeningReminder { get; }
    /// <summary>The deepening wording for each <see cref="DeepFocus"/>, index for index (the element order is code).</summary>
    public IReadOnlyList<string> DeepeningSeeds { get; }
    public string AckWithdrawn { get; }
    public string AckDeclined { get; }
    public string AckFrustration { get; }
    public IReadOnlyDictionary<CloseReason, string> Closings { get; }
    public IReadOnlyList<TopicSpec> Topics { get; }
    public ProtocolLimits Limits { get; }
    public IReadOnlyList<ProtocolRule> Rules { get; }

    private readonly Dto _dto;

    private InterviewProtocol(Dto d)
    {
        _dto = d;
        ProtocolVersion = d.ProtocolVersion;
        Language = d.Language;
        Opening = d.Opening;
        ConsentReask = d.ConsentReask;
        Probe = d.Probe;
        Clarification = d.Clarification;
        RedirectNames = d.RedirectNames;
        DeepeningReminder = d.DeepeningReminder;
        DeepeningSeeds = d.DeepeningSeeds;
        AckWithdrawn = d.Acknowledgements.Withdrawn;
        AckDeclined = d.Acknowledgements.Declined;
        AckFrustration = d.Acknowledgements.Frustration;
        Closings = new Dictionary<CloseReason, string>
        {
            [CloseReason.AllTopicsCovered] = d.Closings.AllTopicsCovered,
            [CloseReason.Unresponsive] = d.Closings.Unresponsive,
            [CloseReason.Hostile] = d.Closings.Hostile,
            [CloseReason.BudgetExhausted] = d.Closings.Budget,
        };
        Topics = d.Topics.Select(t => new TopicSpec(Wire.TryParse<Topic>(t.Id, out var topic) ? topic : throw new InvalidDataException("Unknown topic id in protocol."), t.Id, t.Title, t.Question)).ToArray();
        Limits = d.Limits;
        Rules = d.Rules;
        Validate();
    }

    private const string EnglishResource = "interview-protocol.v1.json";
    private const string PolishResource = "interview-protocol.pl.v1.json";

    /// <summary>The protocol this build ships (English).</summary>
    public static InterviewProtocol Current { get; } = Load(EnglishResource);

    private static InterviewProtocol PolishProtocol { get; } = Load(PolishResource);

    /// <summary>
    /// The protocol for an interview language: <c>en</c> is <see cref="Current"/>, <c>pl</c> is the Polish wording of the same
    /// instrument (same topics, limits and rule ids). Any other language is refused rather than guessed.
    /// </summary>
    public static InterviewProtocol For(string language) => language switch
    {
        "en" => Current,
        "pl" => PolishProtocol,
        _ => throw new ArgumentException("The interview language must be 'en' or 'pl'."),
    };

    /// <summary>The exact bytes (as text) of the embedded protocol file, for clients that publish the protocol document itself (MCP resource, T8).</summary>
    public static string CurrentJson { get; } = ReadResourceText(EnglishResource);

    /// <summary>The same protocol with different bounds, for embedders and tests that need a tiny budget.</summary>
    public InterviewProtocol WithLimits(ProtocolLimits limits) => new(_dto with { Limits = limits });

    public TopicSpec Spec(Topic topic) => Topics.Single(t => t.Topic == topic);

    private void Validate()
    {
        if (Topics.Select(t => t.Topic).Order().SequenceEqual(Enum.GetValues<Topic>().Order()) is false)
            throw new InvalidDataException("The protocol must define each of the six topics exactly once.");
        if (!Opening.Contains("AI", StringComparison.Ordinal))
            throw new InvalidDataException("The opening turn must disclose that the interviewer is an AI.");
        if (DeepeningSeeds.Count != Enum.GetValues<DeepFocus>().Length)
            throw new InvalidDataException("The protocol must give one deepening question for each element of the deepening menu.");
    }

    private static string ReadResourceText(string resource)
    {
        using var stream = typeof(InterviewProtocol).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Embedded protocol is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static InterviewProtocol Load(string resource)
    {
        using var stream = typeof(InterviewProtocol).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Embedded protocol is missing.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        return new InterviewProtocol(JsonSerializer.Deserialize<Dto>(stream, options) ?? throw new InvalidDataException("Empty protocol."));
    }

    private sealed record Dto(
        string ProtocolVersion, string Language, string Opening, string ConsentReask, Acks Acknowledgements,
        string RedirectNames, string Probe, string Clarification, Closes Closings, List<TopicDto> Topics,
        ProtocolLimits Limits, List<ProtocolRule> Rules, string DeepeningReminder, List<string> DeepeningSeeds);

    private sealed record Acks(string Withdrawn, string Declined, string Frustration);

    private sealed record Closes(string AllTopicsCovered, string Unresponsive, string Hostile, string Budget);

    private sealed record TopicDto(string Id, string Title, string Question);
}
