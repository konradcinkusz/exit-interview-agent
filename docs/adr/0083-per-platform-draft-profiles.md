# ADR-0083: Per-platform draft profiles, one writer draft per family, and the Google review tile retired

- Status: accepted
- Date: 2026-10-12
- Principle or guide served (or deviated from): P10 (interface and registration: a platform is a profile in data behind `IPlatformProfiles`, not a new kind of code), ADR-0008 and ADR-0009 (schemas validated; versioned, and the version 1 writer schema is kept for history). Amends [ADR-0074](0074-draft-tiles-from-the-record.md) and [ADR-0075](0075-polish-responsive-interview-and-platform-tiles.md). Design in [platform-drafts-spec.md](../architecture/platform-drafts-spec.md); sources in [review-platforms-2026-10-12.md](../research/review-platforms-2026-10-12.md).

## Context

ADR-0075 gave the interview three platform-shaped tiles: a Glassdoor entry, a Google review and a Reddit post. The research of 2026-10-12 (its §3.11) found that the Google review tile steers a former employee toward a prohibited act. Google's contribution policy names "current or former employment" as a conflict of interest, and such content is removed (`support.google.com/contributionpolicy/answer/7400114`, cited in the research as read from search summaries; WP6 re-checks it). The same research found three defects in the code:

1. `google_review` conflicts with that policy.
2. The service and the web parser disagree on which kinds exist. An item of an unknown kind makes `parseResult` return `null` for the whole result.
3. Facts about each platform are scattered over four files.

Ten platforms were chosen for the drafts (research §2), each with a different norm. Writing one model call per platform would multiply the cost of an interview by about ten, and the billing decision that would need is not this program's to take (spec §3.3).

## Decision

1. **Google review is retired.** No profile and no kind carry it after WP3. In this change the old kinds stay in code and in the version 1 schema so that nothing changes in behaviour; WP3 removes them.
2. **Platforms are data.** Each platform is one entry in `platform-profiles.v1.json`, validated against `schemas/platform-profiles.v1.schema.json` when loaded, and held as typed records (`PlatformProfile`, `IPlatformProfiles`). The ten profiles, and the disabled Kununu one, are WP2.
3. **One writer draft per family.** The model writes one draft for each of four families (`structured_review`, `narrative`, `professional_post`, `short_post`) in the same single call. Code renders each enabled platform's draft from its family's sections. Rendering never edits a word. The output is `schemas/tile-writer-output.v2.schema.json`; version 1 stays for history.
4. **The wire contract changes additively.** `tiles` gains `drafts`. `items` carries generic kinds only, once the service builds drafts (WP4). A result without `drafts` has none. A malformed draft fails the result, because a link or a warning must never be shown half-read. The web parser skips an item of a kind it does not know.
5. **Amendment to ADR-0074.** A writer draft may cover several platforms through one family, and the per-platform text is code's rendering of that draft. The rule that drafts come from the record, or from the masked transcript as ADR-0075 allows, is unchanged.
6. **Amendment to ADR-0075, point 3.** The three platform kinds and `short_note` are replaced by the four family kinds in WP3. Until then both sets exist.

## Alternatives considered

- **One prompt per platform.** Rejected for this cut: about ten times the model cost per interview, and a billing change this program is not allowed to make on its own (spec §3.3). It stays possible behind the same profile model after a real-model evaluation.
- **Keep the Google tile with a warning.** Rejected. The policy is about the content: a warning on the card does not change what the text is.
- **Keep platform kinds in the service's `items`.** Rejected: it keeps the two sides disagreeing about kinds, which is research defect 2.

## Consequences

- Adding a platform is a data change with its own tests and no new code path. One guard run covers every platform.
- Platforms inside one family share their writer text, so their differences are labels, order and warnings only. Option B (per-platform wording) is the follow-up if the evaluation shows that this is not enough (spec §5).
- Until WP3 and WP4 are merged the service sends an empty `drafts` list and the writer still produces the old kinds. Nothing a user sees changes in this change.
- Platform rules were read from search summaries, not from the full pages. The profile's `sources[].level` records that, and the WP6 re-check and an open problem in `OPEN-PROBLEMS.md` track it.
- Nothing here is legal advice. Checklist L1, L2 and L4 stay NOT REVIEWED.
- Revisit when: WP6 finds a platform rule that differs from the research, or the real-model evaluation (spec §4) scores a family draft poorly on one platform.

## Implementation notes

Added at implementation (WP1). They describe what the code does; the Decision above is unchanged.

- The version 2 writer output has no tile-level `title` or `text` for a family tile. Its headline is the section `title` (structured review and narrative), and the body-only families have none. The schema caps each section's length and checks the section keys per family. It does not check minimum lengths or emptiness: the guard drops an empty section as `empty` and a short one as `too_short`, so one bad section cannot cost the whole reply.
- `platform-profiles.v1.json` is an object with a `profiles` array. Identity, detail, leave-out codes and source levels are closed in the schema and typed as strings in C#, so the loader (WP2) decides what is valid.
- The contract's `InterviewTiles` keeps its three-argument constructor, which sends an empty `drafts`. The service's call sites are unchanged until WP4.
- Two existing test lists that enumerate every tile kind leave out the four family kinds until the guard and the v1 writer know them (WP3). They still cover every kind that exists today.
- The web parser treats a family or risk word it does not know, or a URL that is not `https`, as a malformed draft.
