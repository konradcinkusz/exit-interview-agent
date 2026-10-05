using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Personas;

public sealed record PersonaEmployer(string Ref, IReadOnlyList<string> Names);

public sealed record PersonaContext(string TenureBand, string? SeniorityBand, string? FunctionBand)
{
    /// <summary>The record context this persona would give.</summary>
    public RecordContext ToRecordContext() => new(
        Wire.TryParse<TenureBand>(TenureBand, out var t) ? t : throw new InvalidDataException("Bad tenure band."),
        SeniorityBand is not null && Wire.TryParse<SeniorityBand>(SeniorityBand, out var s) ? s : null,
        FunctionBand is not null && Wire.TryParse<FunctionBand>(FunctionBand, out var f) ? f : null);
}

public sealed record PersonaTyping(int WordsPerMinute, int ThinkSeconds);

/// <summary>Alternatives for each kind of turn. A seed picks one deterministically; repeated asks rotate through them.</summary>
public sealed record PersonaResponses(
    IReadOnlyList<string> Consent,
    IReadOnlyList<string>? ConsentReask,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Topics,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Probes,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Clarifications,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Redirects,
    IReadOnlyList<string> Fallback);

/// <summary>What a correct agent run against this persona looks like. The e2e tests and the eval harness assert it.</summary>
public sealed record PersonaExpectation(string Outcome, string EndReason, bool Submittable, int MinProbes = 0, int MinRedirects = 0, int MinClarifications = 0);

/// <summary>
/// An interviewee persona: pure data, validated against <c>schemas/persona.v1.schema.json</c>, synthetic content only.
/// The public shape (these records, <see cref="PersonaCatalog"/> and <see cref="PersonaInterviewee"/>) is the API the
/// eval harness builds on; changing a persona's text is a behaviour change for every consumer and is reviewed as one.
/// </summary>
public sealed record PersonaDefinition(
    string SchemaVersion,
    string Id,
    string Title,
    string Description,
    string Language,
    PersonaEmployer Employer,
    PersonaContext Context,
    IReadOnlyList<string> Behaviors,
    IReadOnlyList<string>? InjectionTargets,
    IReadOnlyList<string>? PlantedLiterals,
    PersonaTyping Typing,
    PersonaResponses Responses,
    PersonaExpectation Expected)
{
    public IReadOnlyList<string> Planted => PlantedLiterals ?? [];
}
