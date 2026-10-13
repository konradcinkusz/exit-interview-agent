namespace ExitInterviewAgent.Agent.Tiles.Platforms;

// The per-platform draft builder (docs/architecture/platform-drafts-spec.md §3.3; ADR-0083). For each enabled profile whose family tile
// was accepted, it picks and orders the family's sections and adds the platform's own labels. It never edits a word: a section's text
// is the guarded writer text, byte for byte, and the copy text adds only labels and blank lines. Pure: no model, no clock, no I/O.

/// <summary>Renders the platform drafts from an accepted tile set. A profile whose family tile is absent gets no draft.</summary>
public static class PlatformDraftBuilder
{
    /// <summary>The blank line between sections in <see cref="PlatformDraft.CopyText"/> (spec §3.3).</summary>
    internal const string SectionSeparator = "\n\n";

    /// <summary>
    /// One draft per enabled profile. The profiles whose markets include the interview's language come first (<see cref="PlatformDraft.Primary"/>),
    /// then the rest; each group keeps profile-file order (spec §3.5). A section the profile lists but the family tile does not carry (an
    /// optional one the writer left out, or a blank text) is left out; nothing is filled in for it.
    /// </summary>
    public static IReadOnlyList<PlatformDraft> Build(TileSet set, IPlatformProfiles profiles, string language)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(profiles);

        var primary = new List<PlatformDraft>();
        var others = new List<PlatformDraft>();
        foreach (var profile in profiles.Enabled)
        {
            var kind = KindOf(profile.Family);
            var tile = set.Tiles.FirstOrDefault(t => t.Kind == kind);
            if (tile?.Sections is not { } familySections) continue;

            var sections = new List<DraftSection>();
            foreach (var rule in profile.Sections)
            {
                var section = familySections.FirstOrDefault(s => s.Key == rule.Key);
                if (section is null || string.IsNullOrWhiteSpace(section.Text)) continue;
                sections.Add(new DraftSection(rule.Key, rule.Label.For(language), section.Text));
            }
            if (sections.Count == 0) continue;

            // Labels go into the copy text only when the platform's form joins them under its own headings (spec §3.1, joinSections).
            var copy = profile.JoinSections
                ? string.Join(SectionSeparator, sections.Select(s => $"{s.Label}\n{s.Text}"))
                : string.Join(SectionSeparator, sections.Select(s => s.Text));

            var primaryDraft = profile.Markets.Contains(language, StringComparer.Ordinal);
            (primaryDraft ? primary : others).Add(new PlatformDraft(
                profile.Id,
                profile.Names.For(language),
                profile.Family,
                profile.Risk,
                profile.Url.For(language),
                profile.Warning.For(language),
                sections,
                copy,
                profile.FieldsOnSite.Select(f => f.For(language)).ToArray(),
                primaryDraft));
        }
        primary.AddRange(others);
        return primary;
    }

    /// <summary>The tile kind that carries a family's draft (the same names as <see cref="DraftFamily"/> in <see cref="TileKind"/>).</summary>
    internal static TileKind KindOf(DraftFamily family) => family switch
    {
        DraftFamily.StructuredReview => TileKind.StructuredReview,
        DraftFamily.Narrative => TileKind.Narrative,
        DraftFamily.ProfessionalPost => TileKind.ProfessionalPost,
        DraftFamily.ShortPost => TileKind.ShortPost,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown draft family."),
    };
}
