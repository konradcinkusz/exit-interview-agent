namespace ExitInterviewAgent.Eval.Scenarios;

/// <summary>A fault injected at the <c>IChatClient</c> seam (the decorator in <c>Execution/FaultInjectingChatClient</c>), by role and call ordinal.</summary>
public sealed record FaultSpec(string Kind, IReadOnlyList<string> Roles, int? Times = null, IReadOnlyList<string>? Variants = null, int? Tokens = null);

public sealed record ExpectMin(
    int? Probes = null, int? Clarifications = null, int? Redirects = null, int? QuestionsRejected = null,
    int? NamesMasked = null, int? TopicsCovered = null, int? ExtractionAttempts = null);

public sealed record ExpectMax(int? Probes = null, int? ExtractionAttempts = null);

public sealed record ExpectAbsent(IReadOnlyList<string>? Spans = null, IReadOnlyList<string>? Events = null, IReadOnlyList<string>? TurnKinds = null)
{
    public bool IsEmpty => (Spans?.Count ?? 0) + (Events?.Count ?? 0) + (TurnKinds?.Count ?? 0) == 0;
}

public sealed record ScenarioExpect(
    string Outcome, string Record, string? EndReason = null, ExpectMin? Min = null, ExpectMax? Max = null,
    IReadOnlyList<string>? EventsPresent = null, ExpectAbsent? Absent = null);

/// <summary>
/// One scenario as data (evals/scenarios/&lt;class&gt;/&lt;id&gt;.yaml). The world is the persona catalog and the protocol that ship in the box (the
/// "named base world"); a scenario names a persona and writes only its delta: seeds, faults, a canary, what it expects.
/// </summary>
public sealed record Scenario(
    string Id, string Class, string Gate, string Title, string Why, string Persona, ScenarioExpect Expect, IReadOnlyList<string> Spec,
    IReadOnlyList<int>? Seeds = null, bool Canary = false, string? Control = null, IReadOnlyList<FaultSpec>? Faults = null,
    IReadOnlyList<string>? Measure = null, IReadOnlyList<string>? Rubrics = null, string? Skip = null, string? Origin = null)
{
    public IReadOnlyList<int> EffectiveSeeds => Seeds is { Count: > 0 } ? Seeds : [1];

    public bool Measures(string metric) => Measure?.Contains(metric) == true;
}

/// <summary>A scenario together with the file it came from and the canonical JSON the digest is computed over.</summary>
public sealed record LoadedScenario(Scenario Scenario, string Path, string CanonicalJson)
{
    public string Id => Scenario.Id;
}
