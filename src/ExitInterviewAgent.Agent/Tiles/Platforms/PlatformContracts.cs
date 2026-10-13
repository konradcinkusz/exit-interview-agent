namespace ExitInterviewAgent.Agent.Tiles.Platforms;

// Contract for the per-platform drafts (docs/architecture/platform-drafts-spec.md §3.1, §3.3, §3.4; ADR-0083). A platform
// profile describes one site once, in a typed record loaded from platform-profiles.v1.json. A PlatformDraft is one profile's
// rendering of one family tile. This file holds types only: the loader (WP2), the writer and guard by family (WP3) and the
// builder (WP4) live in their own files.

/// <summary>A text in the two languages the interview runs in (spec §3.6). <see cref="For"/> picks the one for a language.</summary>
public sealed record LocalizedText(string En, string Pl)
{
    public string For(string language) => language switch
    {
        "en" => En,
        "pl" => Pl,
        _ => throw new ArgumentOutOfRangeException(nameof(language), "Only the interview languages, en and pl, have a text."),
    };
}

/// <summary>
/// The writer family of a platform (spec §3.3): one model draft per family, rendered by code for each member. The wire word is
/// the snake case name (<c>structured_review</c>, <c>narrative</c>, <c>professional_post</c>, <c>short_post</c>).
/// </summary>
public enum DraftFamily { StructuredReview, Narrative, ProfessionalPost, ShortPost }

/// <summary>The risk label of a profile and its draft. Shown as a word in the UI, never as colour alone (spec §3.5).</summary>
public enum PlatformRisk { Medium, High }

/// <summary>
/// One section of a profile: the key the writer's family draft uses, the heading the platform shows, and whether the section may
/// be empty. Labels are the platform's own words (spec §3.1).
/// </summary>
public sealed record PlatformSection(string Key, LocalizedText Label, bool Optional = false);

/// <summary>The design limits of a profile, in code points (spec §3.1). They fit the family's writer limits (spec §3.3).</summary>
public sealed record PlatformLimits(int TitleMax, int SectionMin, int SectionMax, int TotalMax);

/// <summary>
/// Where a fact in a profile was read: <c>Url</c>, the date it was read and the level of that reading (fetched, search summary or
/// unverified). A profile with only unverified sources is disabled (spec §3.2, invariant 8).
/// </summary>
public sealed record PlatformSource(string Url, DateOnly Accessed, string Level);

/// <summary>
/// One platform, described once (spec §3.1). The id is the wire word. <see cref="Identity"/>, <see cref="Detail"/>,
/// <see cref="LeaveOut"/> and <see cref="PlatformSource.Level"/> are closed vocabularies enforced by
/// schemas/platform-profiles.v1.schema.json; they are strings here so that the loader, not the record type, decides what is valid.
/// </summary>
public sealed record PlatformProfile(
    string Id,
    bool Enabled,
    DraftFamily Family,
    LocalizedText Names,
    LocalizedText Url,
    IReadOnlyList<string> Markets,
    string Identity,
    string Detail,
    PlatformRisk Risk,
    IReadOnlyList<PlatformSection> Sections,
    bool JoinSections,
    PlatformLimits Limits,
    IReadOnlyList<string> LeaveOut,
    IReadOnlyList<LocalizedText> FieldsOnSite,
    LocalizedText Warning,
    string Tone,
    IReadOnlyList<PlatformSource> Sources);

/// <summary>The profiles the program knows, loaded and validated once (spec §3.1). The loader is WP2.</summary>
public interface IPlatformProfiles
{
    /// <summary>The profiles with <c>enabled: true</c>, in profile-file order.</summary>
    IReadOnlyList<PlatformProfile> Enabled { get; }

    /// <summary>The profile with this id, enabled or not. Throws <see cref="KeyNotFoundException"/> for an unknown id.</summary>
    PlatformProfile Get(string id);
}

/// <summary>One labelled section of a draft: the profile's label and the guarded writer text, never edited (spec §3.3).</summary>
public sealed record DraftSection(string Key, string Label, string Text);

/// <summary>
/// A draft for one platform, in the interview's language (spec §3.3). <see cref="CopyText"/> is the sections in profile order, with
/// labels only when the profile joins them. <see cref="Url"/> is the profile's URL with no parameters. <see cref="Primary"/> is true when the
/// profile's markets include the interview's language.
/// </summary>
public sealed record PlatformDraft(
    string Id,
    string Name,
    DraftFamily Family,
    PlatformRisk Risk,
    string Url,
    string Warning,
    IReadOnlyList<DraftSection> Sections,
    string CopyText,
    IReadOnlyList<string> FieldsOnSite,
    bool Primary);
