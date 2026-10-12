using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Protocol;

/// <summary>One of the six topics: the record topic, its wire id, its neutral opening question, and its label in a 1-to-5 rating question.</summary>
public sealed record TopicSpec(Topic Topic, string Id, string Title, string Question, string ScaleLabel = "");

/// <summary>Numeric bounds of an interview. Every one is enforced by <see cref="Machine.InterviewMachine"/> or the runner.</summary>
/// <remarks>
/// The last ten fields are protocol 1.3 (spec §3.4, §3.5). A protocol file that omits them (1.2) takes these defaults. Until WP5 wires them,
/// the machine does not read them, so a 1.3 file behaves as 1.2 did.
/// </remarks>
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
    int MinWordsForCoverage,
    int MaxFollowUpsCore = 2,
    int MaxFollowUpsReason = 2,
    int MaxFollowUpsLight = 1,
    int SubstantiveWords = 12,
    int MaxCoreTopics = 3,
    int TargetQuestions = 14,
    int MaxQuestions = 24,
    int CheckInAfterQuestions = 7);

/// <summary>A numbered rule and how it is enforced: <c>code</c>, <c>prompt</c> or <c>both</c>.</summary>
public sealed record ProtocolRule(string Id, string Text, string Enforcement);

/// <summary>
/// A fixed text that may carry register tokens (<c>{Pan}</c>, <c>{Pana}</c>, <c>{Panu}</c>). <see cref="Ty"/> is its informal variant. It is
/// required when <see cref="Text"/> has tokens and must not have any.
/// </summary>
public sealed record RegisterText(string Text, string? Ty = null);

/// <summary>
/// The versioned interview protocol, loaded from the embedded <c>interview-protocol.v1.json</c> (English) or <c>interview-protocol.pl.v1.json</c>
/// (Polish, the same instrument; see <see cref="For"/>). Everything a client
/// must say verbatim lives here; the model only ever words the topic questions and the moves, and each of those has a fixed fallback below.
/// Protocol 1.3 adds the <see cref="Moves"/>, <see cref="Prefaces"/>, register variants and scale labels (spec §4.3); a 1.2 file still loads.
/// </summary>
public sealed partial class InterviewProtocol
{
    /// <summary>The move keys a 1.3 file must define (spec §3.2). The topic openers are the topics' own questions.</summary>
    private static readonly string[] MoveKeys = ["warmup", "example", "elaborate", "ladder", "consequence", "scale", "scaleFollowUp", "retention", "anythingElse"];

    /// <summary>The preface keys a 1.3 file must define (spec §3.7 and §4.3).</summary>
    private static readonly string[] PrefaceKeys = ["checkIn", "alreadySaid", "support", "legalNotice"];

    private const string EnglishResource = "interview-protocol.v1.json";
    private const string PolishResource = "interview-protocol.pl.v1.json";

    /// <summary>The protocol this build ships (English).</summary>
    public static InterviewProtocol Current { get; } = Load(EnglishResource);

    private static InterviewProtocol PolishProtocol { get; } = Load(PolishResource);

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

    /// <summary>The fallback wording of each move (protocol 1.3). Empty for a 1.2 file.</summary>
    public IReadOnlyDictionary<string, RegisterText> Moves { get; }
    /// <summary>The preface wording (protocol 1.3). Empty for a 1.2 file.</summary>
    public IReadOnlyDictionary<string, RegisterText> Prefaces { get; }

    /// <summary>
    /// Every fixed text with its key (<c>opening</c>, <c>probe</c>, <c>deepeningSeeds.0</c>, <c>closings.budget</c>, <c>topics.culture.question</c>,
    /// <c>moves.scale</c>, <c>prefaces.checkIn</c> and so on). The tests and the register rules run over this list.
    /// </summary>
    public IReadOnlyList<(string Key, RegisterText Text)> FixedTexts { get; }

    private readonly Dto _dto;

    private InterviewProtocol(Dto d)
    {
        _dto = d;
        var ty = d.TyVariants ?? new Dictionary<string, string>();
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
        Topics = d.Topics.Select(t => new TopicSpec(Wire.TryParse<Topic>(t.Id, out var topic) ? topic : throw new InvalidDataException("Unknown topic id in protocol."), t.Id, t.Title, t.Question, t.ScaleLabel ?? "")).ToArray();
        Limits = d.Limits;
        Rules = d.Rules;
        Moves = Wrap(d.Moves, "moves", ty);
        Prefaces = Wrap(d.Prefaces, "prefaces", ty);
        FixedTexts = BuildFixedTexts(ty);
        Validate(ty);
    }

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

    /// <summary>Parses a protocol file (1.2 or 1.3). Refuses a file that breaks the protocol's own rules, so a bad edit fails at start-up.</summary>
    public static InterviewProtocol Parse(string json)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        return new InterviewProtocol(JsonSerializer.Deserialize<Dto>(json, options) ?? throw new InvalidDataException("Empty protocol."));
    }

    /// <summary>The same protocol with different bounds, for embedders and tests that need a tiny budget.</summary>
    public InterviewProtocol WithLimits(ProtocolLimits limits) => new(_dto with { Limits = limits });

    public TopicSpec Spec(Topic topic) => Topics.Single(t => t.Topic == topic);

    /// <summary>True when the text carries a register token (<c>{Pan}</c>, <c>{Pana}</c> or <c>{Panu}</c>).</summary>
    public static bool HasRegisterTokens(string text) => RegisterToken().IsMatch(text);

    /// <summary>
    /// Renders the register tokens of a fixed text for an address form. <c>Pan</c> and <c>Pani</c> give the one form; <see cref="AddressForm.Neutral"/>
    /// gives "Pan lub Pani" (never a slash). A text with tokens in the <see cref="AddressForm.Ty"/> form needs its own variant: see the other overload.
    /// </summary>
    public string Render(string text, AddressForm form)
    {
        if (!HasRegisterTokens(text)) return text;
        if (form == AddressForm.Ty) throw new ArgumentException("A text with register tokens has a ty variant; render its RegisterText instead.", nameof(form));
        return RegisterToken().Replace(text, m => Token(m.Groups[1].Value, form));
    }

    /// <summary>Renders a fixed text in an address form, using its ty variant for <see cref="AddressForm.Ty"/>.</summary>
    public string Render(RegisterText text, AddressForm form) => form == AddressForm.Ty && text.Ty is { } ty ? ty : Render(text.Text, form);

    /// <summary>The rating question for a light topic: the scale move with the topic's label in it (spec §3.2, <c>Scale</c>).</summary>
    public string ScaleQuestion(TopicSpec topic, AddressForm form)
    {
        if (!Moves.TryGetValue("scale", out var scale)) throw new InvalidOperationException("This protocol has no scale move (a 1.2 file).");
        return Render(scale, form).Replace("{scaleLabel}", topic.ScaleLabel, StringComparison.Ordinal);
    }

    private void Validate(Dictionary<string, string> ty)
    {
        if (Topics.Select(t => t.Topic).Order().SequenceEqual(Enum.GetValues<Topic>().Order()) is false)
            throw new InvalidDataException("The protocol must define each of the six topics exactly once.");
        if (!Opening.Contains("AI", StringComparison.Ordinal))
            throw new InvalidDataException("The opening turn must disclose that the interviewer is an AI.");
        if (DeepeningSeeds.Count != Enum.GetValues<DeepFocus>().Length)
            throw new InvalidDataException("The protocol must give one deepening question for each element of the deepening menu.");

        foreach (var (key, text) in FixedTexts)
        {
            if (HasRegisterTokens(text.Text) && string.IsNullOrEmpty(text.Ty))
                throw new InvalidDataException($"The fixed text '{key}' has register tokens and needs a ty variant.");
            if (text.Ty is not null && HasRegisterTokens(text.Ty))
                throw new InvalidDataException($"The ty variant of '{key}' still has register tokens.");
        }
        var keys = FixedTexts.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        if (ty.Keys.FirstOrDefault(k => !keys.Contains(k)) is { } orphan)
            throw new InvalidDataException($"tyVariants names '{orphan}', which is not a fixed text.");

        if (Moves.Count == 0 && Prefaces.Count == 0) return;
        if (!Moves.Keys.Order().SequenceEqual(MoveKeys.Order()))
            throw new InvalidDataException("A 1.3 protocol must define exactly the moves warmup, example, elaborate, ladder, consequence, scale, scaleFollowUp, retention and anythingElse.");
        if (!Prefaces.Keys.Order().SequenceEqual(PrefaceKeys.Order()))
            throw new InvalidDataException("A 1.3 protocol must define exactly the prefaces checkIn, alreadySaid, support and legalNotice.");
        if (!Moves["scale"].Text.Contains("{scaleLabel}", StringComparison.Ordinal))
            throw new InvalidDataException("The scale move must name the topic's scale label.");
        if (Topics.Any(t => t.Topic != Topic.ReasonForLeaving && t.ScaleLabel.Length == 0))
            throw new InvalidDataException("Every topic but reason_for_leaving needs a scale label.");
    }

    private static IReadOnlyDictionary<string, RegisterText> Wrap(Dictionary<string, string>? texts, string prefix, Dictionary<string, string> ty)
    {
        var result = new Dictionary<string, RegisterText>(StringComparer.Ordinal);
        foreach (var (key, text) in texts ?? new Dictionary<string, string>())
            result[key] = new RegisterText(text, ty.GetValueOrDefault($"{prefix}.{key}"));
        return result;
    }

    private IReadOnlyList<(string Key, RegisterText Text)> BuildFixedTexts(Dictionary<string, string> ty)
    {
        RegisterText Fixed(string key, string text) => new(text, ty.GetValueOrDefault(key));
        var list = new List<(string Key, RegisterText Text)>
        {
            ("opening", Fixed("opening", Opening)),
            ("consentReask", Fixed("consentReask", ConsentReask)),
            ("probe", Fixed("probe", Probe)),
            ("clarification", Fixed("clarification", Clarification)),
            ("redirectNames", Fixed("redirectNames", RedirectNames)),
            ("deepeningReminder", Fixed("deepeningReminder", DeepeningReminder)),
            ("acknowledgements.withdrawn", Fixed("acknowledgements.withdrawn", AckWithdrawn)),
            ("acknowledgements.declined", Fixed("acknowledgements.declined", AckDeclined)),
            ("acknowledgements.frustration", Fixed("acknowledgements.frustration", AckFrustration)),
        };
        for (var i = 0; i < DeepeningSeeds.Count; i++) list.Add(($"deepeningSeeds.{i}", Fixed($"deepeningSeeds.{i}", DeepeningSeeds[i])));
        foreach (var reason in Enum.GetValues<CloseReason>())
        {
            var key = ClosingKey(reason);
            list.Add(($"closings.{key}", Fixed($"closings.{key}", Closings[reason])));
        }
        foreach (var topic in Topics) list.Add(($"topics.{topic.Id}.question", Fixed($"topics.{topic.Id}.question", topic.Question)));
        foreach (var (key, text) in Moves) list.Add(($"moves.{key}", text));
        foreach (var (key, text) in Prefaces) list.Add(($"prefaces.{key}", text));
        return list;
    }

    private static string ClosingKey(CloseReason reason) => reason switch
    {
        CloseReason.AllTopicsCovered => "allTopicsCovered",
        CloseReason.Unresponsive => "unresponsive",
        CloseReason.Hostile => "hostile",
        CloseReason.BudgetExhausted => "budget",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static string Token(string token, AddressForm form)
    {
        // Nominative, genitive and dative of "Pan"; "Pani" is the same in all three cases.
        var (pan, pani, neutral) = token switch
        {
            "Pan" => ("Pan", "Pani", "Pan lub Pani"),
            "Pana" => ("Pana", "Pani", "Pana lub Pani"),
            "Panu" => ("Panu", "Pani", "Panu lub Pani"),
            _ => throw new InvalidOperationException("Unknown register token."),
        };
        return form switch
        {
            AddressForm.Pan => pan,
            AddressForm.Pani => pani,
            _ => neutral,
        };
    }

    [GeneratedRegex(@"\{(Pan|Pana|Panu)\}", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterToken();

    private static string ReadResourceText(string resource)
    {
        using var stream = typeof(InterviewProtocol).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Embedded protocol is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static InterviewProtocol Load(string resource) => Parse(ReadResourceText(resource));

    private sealed record Dto(
        string ProtocolVersion, string Language, string Opening, string ConsentReask, Acks Acknowledgements,
        string RedirectNames, string Probe, string Clarification, Closes Closings, List<TopicDto> Topics,
        ProtocolLimits Limits, List<ProtocolRule> Rules, string DeepeningReminder, List<string> DeepeningSeeds,
        Dictionary<string, string>? Moves = null, Dictionary<string, string>? Prefaces = null, Dictionary<string, string>? TyVariants = null);

    private sealed record Acks(string Withdrawn, string Declined, string Frustration);

    private sealed record Closes(string AllTopicsCovered, string Unresponsive, string Hostile, string Budget);

    private sealed record TopicDto(string Id, string Title, string Question, string? ScaleLabel = null);
}
