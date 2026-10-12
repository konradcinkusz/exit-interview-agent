namespace ExitInterviewAgent.Agent.Tiles;

// The writer families of the per-platform drafts (docs/architecture/platform-drafts-spec.md §3.3; ADR-0083). The model writes one
// draft per family; the writer prompt is built from this table, and the guard enforces its limits per section. The platform
// profiles (WP2) say which family each platform uses; this file knows only the families.

/// <summary>
/// One section a family draft carries. <paramref name="Min"/> and <paramref name="Max"/> are code points. A required section that
/// is empty drops the draft as <see cref="TileDropReason.Empty"/>; a non-empty section below its minimum drops it as
/// <see cref="TileDropReason.TooShort"/>. An optional section that is blank is left out and is not a reason to drop.
/// </summary>
public sealed record FamilySection(string Key, int Min, int Max, bool Required);

/// <summary>
/// One family: its kind and wire word, its sections, the limit of the whole draft, the copy limit in words, whether the experience
/// terms may appear (the narrative only), and the tone the writer is asked for (the strictest tone of its members).
/// </summary>
public sealed record FamilySpec(
    TileKind Kind,
    string WireKind,
    IReadOnlyList<FamilySection> Sections,
    int TotalMax,
    int CopyRunWords,
    bool ExperienceTerms,
    string Tone);

public static class TileFamilies
{
    public static readonly FamilySpec StructuredReview = new(
        TileKind.StructuredReview,
        "structured_review",
        [
            new FamilySection("title", 1, 60, Required: true),
            new FamilySection("pros", 40, 500, Required: true),
            new FamilySection("cons", 40, 500, Required: true),
            new FamilySection("advice", 40, 500, Required: false),
        ],
        TotalMax: 1100,
        CopyRunWords: TileLimits.QuoteRunWordsForbidden,
        ExperienceTerms: false,
        Tone: "Neutral and balanced. Opinions framed as opinions, no blame, and a pro wherever a covered topic is rated 3 or more.");

    public static readonly FamilySpec Narrative = new(
        TileKind.Narrative,
        "narrative",
        [
            new FamilySection("title", 1, 120, Required: true),
            new FamilySection("body", 600, 4000, Required: true),
        ],
        TotalMax: 4120,
        CopyRunWords: TileLimits.NarrativeQuoteRunWordsForbidden,
        ExperienceTerms: true,
        Tone: "First person and concrete: what happened, roughly when, how often, how it affected me. Opinions framed as my own experience, no blame of named or identifiable people.");

    public static readonly FamilySpec ProfessionalPost = new(
        TileKind.ProfessionalPost,
        "professional_post",
        [new FamilySection("body", 300, 1300, Required: true)],
        TotalMax: 1300,
        CopyRunWords: TileLimits.QuoteRunWordsForbidden,
        ExperienceTerms: false,
        Tone: "Calm, factual and measured, in a professional register. No accusation, and a pro wherever a covered topic is rated 3 or more.");

    public static readonly FamilySpec ShortPost = new(
        TileKind.ShortPost,
        "short_post",
        [new FamilySection("body", 80, 270, Required: true)],
        TotalMax: 270,
        CopyRunWords: TileLimits.QuoteRunWordsForbidden,
        ExperienceTerms: false,
        Tone: "Calm, factual and measured, one or two sentences. No accusation.");

    /// <summary>Every family, in the order the writer prompt lists them.</summary>
    public static IReadOnlyList<FamilySpec> All { get; } = [StructuredReview, Narrative, ProfessionalPost, ShortPost];

    /// <summary>The family of a kind, or null for the generic kinds (the Facts tile and the four general tiles).</summary>
    public static FamilySpec? Find(TileKind kind) => All.FirstOrDefault(f => f.Kind == kind);
}
