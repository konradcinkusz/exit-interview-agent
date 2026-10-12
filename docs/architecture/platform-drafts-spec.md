# Per-platform drafts: spec and work packages

Status: **proposed** (design only, no code yet). Date: 2026-10-12.
Research and sources: [review-platforms-2026-10-12.md](../research/review-platforms-2026-10-12.md). Builds on
[draft-tiles.md](draft-tiles.md), [ADR-0074](../adr/0074-draft-tiles-from-the-record.md) and
[ADR-0075](../adr/0075-polish-responsive-interview-and-platform-tiles.md). WP1 records the decision as ADR-0083.

Nothing in this spec is legal advice. Drafts are proposals, not facts, and the program never posts.

## 1. Goal and non-goals

**Goal.** After an interview, the person sees **one draft per platform**, shaped to that platform's norms. Each draft
comes with:

- a copy button per field;
- a link that opens the site;
- the platform's own warning;
- the same guard as today.

The draft is in the interview's language (PL or EN). Each platform is described once, in a typed profile held in a config
file. Every profile has its own tests.

**Non-goals.**

- Posting, prefilling a form, deep links into a platform's review form, browser automation, or any platform API. Section 3.13
  of the research explains why.
- Filling in the employer's name: the record holds only an opaque reference.
- Ratings typed for the user: the user picks them on the site.
- Any billing change.
- A per-platform model call (deferred; see 3.3).
- German drafts for Kununu (that profile ships disabled).

## 2. What exists today

| Piece | Where | Today |
|---|---|---|
| Kinds | `Agent/Tiles/TileContracts.cs` `TileKind` | `Facts`, 5 generic kinds, `Glassdoor`, `GoogleReview`, `Reddit` |
| Limits | `TileLimits` | hard-coded per kind: 280 / 900 / 500 / 4500; title 60 |
| Writer | `TilePrompts.WriterSystem`, `TileWriter` (4000 output tokens), `schemas/tile-writer-output.v1.schema.json` | one call that writes up to 8 kinds; record plus masked transcript (ADR-0075) |
| Guard | `TileGuard` | empty, length, absolute banned terms, experience terms (Reddit only, first person), PII (fail closed), grounding, copied runs (7 words; 13 for Reddit); drops and never edits |
| Generator | `TileGenerator` | facts by code, one retry carrying only the drop codes |
| Service | `InterviewSessions.BuildTilesAsync` → `InterviewTiles(Items, Dropped, Notice)` | maps **every** kind to snake case |
| Web | `lib/interview-contract.ts` `TILE_KINDS` (6), `ResultPanel.tsx` `TilesView`, `messages/interview.ts` `kinds` | one card per tile, one copy button, one notice for all tiles; an unknown kind makes `parseResult` return `null` for the whole result |
| CLI | `Cli/Tiles/TileRenderer.cs`, `tiles` command | text, HTML, JSON; JSON has no notice |
| Legal | `docs/legal/terms.*.md` §3 names Glassdoor, Google reviews, Reddit; `CHECKLIST.md` L1, L2, L4 | NOT REVIEWED |

The research (§5) records three defects. `google_review` conflicts with Google's policy. The service and the web parser
disagree on which kinds exist. Platform facts are scattered across four files.

## 3. Design

### 3.1 Data model: a typed, config-driven profile

There is one JSON file, `src/ExitInterviewAgent.Agent/Tiles/Platforms/platform-profiles.v1.json`, embedded as a resource. It
is validated against `schemas/platform-profiles.v1.schema.json` (JsonSchema.Net, ADR-0008) when it is loaded, and
deserialised into C# records. A file that does not load fails fast: in tests, and at service start through the startup
check. Example entry:

```json
{
  "id": "glassdoor",
  "enabled": true,
  "family": "structured_review",
  "names": { "en": "Glassdoor", "pl": "Glassdoor" },
  "url": { "en": "https://www.glassdoor.com/", "pl": "https://www.glassdoor.com/" },
  "markets": ["en", "pl"],
  "identity": "anonymous_verified_email",
  "detail": "low",
  "risk": "medium",
  "sections": [
    { "key": "title",  "label": { "en": "Headline", "pl": "Nagłówek" } },
    { "key": "pros",   "label": { "en": "Pros", "pl": "Plusy" } },
    { "key": "cons",   "label": { "en": "Cons", "pl": "Minusy" } },
    { "key": "advice", "label": { "en": "Advice to management", "pl": "Rada dla zarządu" }, "optional": true }
  ],
  "joinSections": false,
  "limits": { "titleMax": 60, "sectionMin": 40, "sectionMax": 500, "totalMax": 1100 },
  "leaveOut": ["individuals", "executives", "employer_name", "confidential", "accusations", "protected", "contact", "identifying_detail"],
  "fieldsOnSite": [
    { "en": "Employment status and job title", "pl": "Status zatrudnienia i stanowisko" },
    { "en": "Star ratings", "pl": "Oceny w gwiazdkach" }
  ],
  "warning": { "en": "…", "pl": "…" },
  "tone": "neutral, balanced; at least one pro when any covered topic is rated 3 or more",
  "sources": [ { "url": "https://help.glassdoor.com/s/article/Community-Guidelines", "accessed": "2026-10-12", "level": "search_summary" } ]
}
```

Closed vocabularies (schema enums):

- `family`: `structured_review`, `narrative`, `professional_post`, `short_post`.
- `identity`: `real_name`, `pseudonymous`, `anonymous`, `anonymous_verified_email`, `verified_work_email`.
- `detail`: `low`, `moderate`, `high`.
- `risk`: `medium`, `high`.
- `leaveOut`: as above.
- `sources[].level`: `fetched`, `search_summary`, `unverified`.

C#: `PlatformProfile`, `PlatformSection`, `PlatformLimits`, `LocalizedText(En, Pl)` (a record with `For(language)`),
`DraftFamily`, `PlatformRisk`, and `IPlatformProfiles { IReadOnlyList<PlatformProfile> Enabled; PlatformProfile Get(string id); }`.
The id is the wire word.

The ten profiles, with their values, are in the research, [§4](../research/review-platforms-2026-10-12.md#4-draft-profiles):
glassdoor, indeed, kununu (`enabled: false`), gowork, linkedin, facebook_group, reddit, wykop, blind, x. Google review is
**not** a profile. Its retirement is recorded in ADR-0083, with the policy citation.

### 3.2 Invariants that the profile tests enforce (one test class, plus one golden test per profile)

1. The file validates against the schema. Ids are unique and match `^[a-z_]+$`.
2. `names`, `warning` and every section `label` are non-empty in **both** `en` and `pl`. Each warning ends with the fixed
   sentence "Draft, not fact. Not legal advice." / "To szkic, nie fakt. To nie jest porada prawna."
3. `url.*` is `https`, has no query string or fragment, and its host is on that id's allowlist in the test. Example:
   gowork → `www.gowork.pl`. A change of URL therefore needs a reviewed test change.
4. **Risk rule:** `risk == high` if `identity == real_name` or `detail == high` or `id == gowork`; otherwise `medium`.
5. Limits fit the family's writer limits (3.3), so the builder never needs to cut text. Where a source records a platform
   cap, limits stay below it: linkedin ≤ 3000, x ≤ 280.
6. `leaveOut` contains at least the base set: `individuals`, `employer_name`, `confidential`, `accusations`, `protected`,
   `contact`.
7. Every enabled profile has a golden rendering in `pl` and `en` (3.4). A meta-test fails when an id has no golden file, so
   a profile cannot be added without its test.
8. `sources` has at least one entry. If every entry is `unverified`, the profile must be `enabled: false`.
9. No profile has id `google_review`, and no family or label mentions Google reviews or Trustpilot. This test guards the
   retirement.

### 3.3 Generation: one writer call, four families, ten renderings

**Decision.** The model writes **one draft per family**, in the same single call it makes today. Code then renders each
enabled profile from its family's draft: it picks and orders sections, adds labels, and either joins the sections or keeps
them separate. **Rendering never edits a word.** Each section's text is exactly the guarded writer text.

Why this rather than a prompt per profile:

- It keeps one model call and the same budget. A web interview spends one credit, and the hosted budget (90k tokens,
  ADR-0078) is shared with the interview. Ten calls would multiply the tile cost by about ten. That needs a billing decision
  this spec is not allowed to make.
- One guard run covers every platform.
- The members of a family differ mainly in labels, order and warning, and those are deterministic.

Per-profile wording (option B) can follow later, behind the same profile model. It needs a real-model evaluation first
(section 5).

Families and writer limits. Each limit is the strictest of the family's members:

| Family | Members | Writer output | Writer limits (code points) | Experience terms | Copied-run limit |
|---|---|---|---|---|---|
| `structured_review` | glassdoor, indeed, kununu, gowork, blind | `title`, `pros`, `cons`, `advice` (optional) | title ≤ 60; each section 40–500; total ≤ 1100 | banned | 7 words |
| `narrative` | reddit, wykop | `title`, `body` | title ≤ 120; body 600–4000 | as Reddit today (interviewee used it, first-person framing) | 13 words |
| `professional_post` | linkedin, facebook_group | `body` | 300–1300 | banned | 7 words |
| `short_post` | x | `body` | 80–270 | banned | 7 words |

Changes to the writer and the guard (WP3):

- **TileKind.** `Glassdoor`, `GoogleReview`, `Reddit` and `ShortNote` are replaced by `StructuredReview`, `Narrative`,
  `ProfessionalPost` and `ShortPost`. The other generic kinds and `Facts` stay. `TileSet.CurrentVersion` becomes `"2"`.
- **Candidates.** `CandidateTile` gains `IReadOnlyList<TileSection> Sections`, where `TileSection(string Key, string Text)`.
  `Text` is the sections joined by a blank line, so the existing guard checks keep working on the whole text.
- **Schema.** `schemas/tile-writer-output.v2.schema.json` carries the sections per family. v1 is kept for history.
- **Prompt.** `TilePrompts.WriterSystem` is **built from the family table**, not hand-written per platform. It lists each
  family's sections, limits and tone. The tone is the strictest of the family's members: opinions framed as opinions, no
  blame, and a pro wherever a rating supports one.
- **Guard.** Per-family limits replace per-kind limits; each section is checked against its own min and max. A required
  section that is empty is dropped as `empty`. A section below its minimum is dropped with a new code, `too_short`. The rest
  are unchanged and run in the same order: experience terms are allowed only in `narrative`, PII fails closed, then
  grounding, then copied runs.
- **New lexical check `identifying_detail`.** Its drop code is `identifying_detail`. It flags a full calendar date
  (`\d{1,2}[./-]\d{1,2}[./-]\d{2,4}`, and month name plus day in EN and PL), an email or phone number (already covered by
  PII), and a team-size phrase (`team of \d+`, `zespół \d+`, `\d+-osobow\w*`). It is a lexical floor, like the banned terms.
- **Leave-out codes and how each is enforced.** The spec tests this table so that no code is silently unenforced:

| leaveOut code | Enforced by |
|---|---|
| individuals, contact | PII guard (fail closed) + prompt |
| employer_name | prompt + `[COMPANY]`/`[FIRMA]` token; **not detectable** (the employer is unknown to the program) |
| executives | prompt only (the PII guard catches a capitalised name, not a title) |
| confidential | prompt only + warning; **not detectable lexically** |
| accusations | absolute banned terms + prompt |
| protected | absolute banned terms (health) + prompt; other characteristics are prompt only |
| identifying_detail | new lexical check + prompt |

- **Output budget.** `MaxOutputTokens` stays at 4000. The sum of the writer limits (4 × 600 generic + 1100 + 4120 + 1300 +
  270 = 9190 code points) is close to today's 8580. If the first real-model measurement shows truncation, raising it to
  6000 is allowed **inside** the existing per-interview budget. That changes no price and no credit.

**Builder (WP4).** `PlatformDraftBuilder.Build(TileSet set, IPlatformProfiles profiles, string language)` produces a
`PlatformDraft` for each enabled profile whose family tile was accepted. A profile whose family tile was dropped gets no
draft; the drop is already counted. Each draft holds:

- `PlatformDraft(Id, Name, Family, Risk, Url, Warning, Sections[(Key, Label, Text)], CopyText, FieldsOnSite[])`. All
  strings are resolved to the language.
- `CopyText` is the sections in profile order. Labels are included only when `joinSections` is true, and only as the
  platform's own heading words; GoWork has no labels at all.
- Property test: the concatenation of the section texts equals the guarded writer sections, and `CopyText` adds only labels
  and newlines.

### 3.4 Wire contract (the only API change)

`GET /interviews/{id}/result` → `tiles` gains one **additive** field, and nothing else changes in the API, billing or
credits:

```jsonc
"tiles": {
  "items":   [ { "kind": "facts", "text": "…" }, … ],   // generic kinds only (facts, overview, what_worked, what_could_improve, for_the_next_person)
  "drafts":  [ { "platform": "gowork", "name": "GoWork.pl", "family": "structured_review", "risk": "high",
                 "url": "https://www.gowork.pl/", "warning": "…",
                 "sections": [ { "key": "pros", "label": "Plusy", "text": "…" } ],
                 "copyText": "…", "fieldsOnSite": [ "…" ] } ],
  "dropped": [ { "code": "too_short" } ],
  "notice":  "…"
}
```

- `Contracts/InterviewContracts.cs`: `InterviewTiles` gains `IReadOnlyList<InterviewPlatformDraft> Drafts`.
- `items` no longer carries platform kinds. The service sends only the generic kinds and facts.
- The web parser **skips** an item of unknown kind instead of failing the whole result. This fixes research finding 2.
- The parser treats a missing `drafts` as `[]`.
- Nothing is deployed, so there is no compatibility window.

### 3.5 UI (web, PL/EN)

`ResultPanel.tsx` gets a section "Drafts for a platform" / "Szkice dla serwisów", after the generic tiles.

- **Order.** Profiles whose `markets` include the interview language come first, in profile-file order. In PL: gowork,
  linkedin, facebook_group, wykop, glassdoor, indeed, blind, reddit, x. The others sit inside a `<details>` named "Other
  platforms" / "Inne serwisy".
- **One card per draft:**
  - the platform name (`h4`);
  - a **risk label in words** ("Higher risk" / "Wyższe ryzyko"), never colour alone;
  - the warning (`role="note"`, `lang` set);
  - each section with its label, its text, and a **copy button for that field**;
  - a "Copy all" button using `copyText`;
  - "You fill in on the site:" / "Uzupełnisz w serwisie:" and the `fieldsOnSite` list;
  - an **"Open {name}"** link (`target="_blank" rel="noopener noreferrer"`, with the visible hint "opens a new tab" /
    "otwiera nową kartę"). The `href` is the profile URL only, with no parameters.
- **High-risk gate.** For `risk: high` the copy buttons stay disabled until the person ticks "I have read this warning" /
  "Przeczytałem/am to ostrzeżenie". The tick lives in component state only: it is not stored or sent. This is the "warning
  before publication" that checklist L1 asks about, pending the lawyer.
- **Labels.** The section labels, warnings and names come from the server, already in the interview's language, like
  `notice` today. Chrome strings ("Copy all", "Open", the gate) go into `messages/interview.ts` in both languages.
  `kinds.google_review`, `glassdoor`, `reddit` and `short_note` are removed. The generic kinds gain labels for
  `what_worked`, `what_could_improve` and `for_the_next_person`.
- **Downloads.** `tilesToHtml` and `tilesToJson` include the drafts with their warnings. The JSON gains the `notice`, which
  closes the gap draft-tiles.md records.
- **CLI.** `TileRenderer` prints the drafts in the same order and adds them to the HTML and JSON. No new flags.

### 3.6 Language

- Drafts are written in the interview language, which is `pl` or `en`; there is no German.
- Labels and warnings come from `LocalizedText.For(language)`.
- The Polish experience-term and first-person rules already in `TileGuard` apply to `narrative`.
- `identifying_detail` gets Polish month names (stycznia … grudnia) and number forms.
- Profiles whose audience differs from the interview language, such as English Glassdoor after a Polish interview, are shown
  in the interview language. The card says "Write in the language the site's readers use" only when the markets do not
  include the language.

## 4. Test and evaluation plan

| Layer | What | Where | Runs in CI |
|---|---|---|---|
| Profile | invariants 3.2 (1–9), a test **per profile** (golden `pl` and `en` from the scripted mock), the meta-test for missing goldens | `tests/ExitInterviewAgent.Agent.Tests/Tiles/Platforms/` | yes |
| Guard | per-family limits, `too_short`, `identifying_detail` (EN/PL positives and negatives), experience terms outside `narrative` dropped, the leave-out enforcement table checked against the code | `…/Tiles/FamilyTileGuardTests.cs` | yes |
| Red team | the fixed record plus transcript fixtures: names a manager, gives a customer name and a revenue figure, mentions a diagnosis, accuses of theft, gives an exact date and team size, contains an injection telling the writer to name the CEO. Each asserts a drop with the expected code, or, for the prompt-only codes, records "prompt only, not provable offline" | `…/Tiles/RedTeamFixtures/` | yes (mock) |
| Builder | the never-edit property; order; disabled profiles absent; a dropped family gives no drafts | `…/Platforms/PlatformDraftBuilderTests.cs` | yes |
| Contract | C# serialisation of `drafts`; TS parser: unknown kind skipped, missing `drafts` gives `[]`, malformed draft makes the result `null` | `tests/ExitInterviewAgent.InterviewService.Tests`, `web/app/lib/interview-contract.test.ts` | yes |
| Web | per-section copy, Copy all, high-risk gate, link attributes, `lang`, risk in words, PL/EN labels, no Google card | `web/app/app/interview/*.test.tsx` | yes |
| E2E | stub backend returns drafts; the PL journey shows GoWork first and opens no network request on copy; the EN journey shows Glassdoor first | `tests/e2e/specs/` | yes |
| Mutation | delete one warning, raise x's limit to 300, add `google_review`, remove a golden: each must turn a test red (recorded once, as for ADR-0041) | recorded in the WP7 PR | once |
| Real model | 10 recorded interviews (5 PL, 5 EN, from the persona simulator) give 100 drafts. Two reviewers score each draft against its profile: no names, no confidential detail, length, tone, structure, warning relevance. The pass rate per profile is reported, and the cost per run is measured | `docs/research/RESULTS.md` new section | **NOT RUN**, needs the owner's key; deferred like draft-tiles.md |

The eval harness (`docs/eval/SPEC.md`) is **not** changed by this spec. Draft-level Layer-1 constraints are a separate,
reviewed change (ADR-0040 rule), proposed in WP7 as a document only.

## 5. Risks and open points

- **Platform rules drift, and none was read in full** (research banner). Mitigation: `sources[].level` in each profile, the
  WP6 re-check, and a new open problem, "Platform rules are cited from search summaries", added to `OPEN-PROBLEMS.md` in WP6.
- **Families blur differences within a family.** For example, GoWork's opinion framing is applied to Glassdoor too. This
  is accepted for the first cut; option B follows a real-model evaluation.
- **High-risk profiles** (LinkedIn, Facebook, Reddit, Wykop, GoWork) still produce text someone can publish. The guard is a
  floor, not a guarantee. The legal checklist (L1, L2, L4, confidentiality) stays NOT REVIEWED. An AI-assistance disclosure
  line is **not** added automatically: no chosen platform requires one per the summaries, and Reddit leaves it to each
  community. Checklist L2 keeps the question open.
- **The Kununu profile is disabled.** It is enabled by a one-line change after WP6 verifies its rules.

## 6. Work packages

Waves:

- Wave 1: WP1 merges first, because it holds the contracts and keeps the build green.
- Wave 2: WP2, WP3, WP5 and WP6 run in parallel, and touch disjoint files.
- WP4 starts in wave 2 against fixtures and merges after WP2 and WP3.
- Wave 3: WP7.

Every package follows [AGENTS.md](../../AGENTS.md): run the gates it lists, put no secrets, personal data or model
identifiers anywhere, and report a gate it could not run as NOT RUN. Each opens one PR from the repository template.

| WP | Title | Wave | Depends on | Owns (files) |
|---|---|---|---|---|
| WP1 | Contracts and ADR-0083 | 1 | — | `Agent/Tiles/Platforms/PlatformContracts.cs` (new), `TileContracts.cs` (add the four family kinds and `TileSection`, keep the old kinds), `Contracts/InterviewContracts.cs` (`InterviewPlatformDraft`, `Drafts` defaulting to empty), `schemas/platform-profiles.v1.schema.json`, `schemas/tile-writer-output.v2.schema.json`, `web/app/lib/interview-contract.ts` + test (draft types, tolerant parser), `docs/adr/0083-per-platform-draft-profiles.md` |
| WP2 | Profile data, loader, per-profile tests | 2 | WP1 | `Agent/Tiles/Platforms/platform-profiles.v1.json`, `PlatformProfiles.cs`, the `.csproj` embed line, `tests/…/Tiles/Platforms/PlatformProfileTests.cs` |
| WP3 | Writer and guard by family; retire `google_review` | 2 | WP1 | `TilePrompts.cs`, `TileWriterOutput.cs`, `TileGuard.cs`, `TileLimits` in `TileContracts.cs` (limits block only), `TileGenerator.cs`, `Mock/ScriptedTileResponses.cs`, the `TileKind` cleanup and its compile fallout in `Cli/Tiles/TileRenderer.cs` (one line), Agent tile tests, red-team fixtures |
| WP4 | Draft builder, service and CLI wiring, goldens | 2→3 | WP1; merges after WP2, WP3 | `Agent/Tiles/Platforms/PlatformDraftBuilder.cs`, `InterviewService/Interviews/InterviewSessions.cs` (`BuildTilesAsync` only), DI registration, `Cli/Tiles/TileRenderer.cs` (drafts), golden files `tests/…/Platforms/Golden/*.{pl,en}.txt`, service and CLI tests |
| WP5 | Web UI | 2 | WP1 | `web/app/app/interview/ResultPanel.tsx` (+ new `PlatformDrafts.tsx`), `interview.module.css`, `lib/messages/interview.ts`, `lib/interview-render.ts` + tests |
| WP6 | Docs, legal drafts and source re-check | 2 | WP1 (WP2 for the final `sources` update) | `docs/architecture/draft-tiles.md`, `README.md` (tiles rows), `docs/legal/terms.{en,pl}.md` §3, `docs/legal/CHECKLIST.md` (L2 notes), `docs/OPEN-PROBLEMS.md`, this research file's §6 |
| WP7 | E2E, mutation record, eval proposal | 3 | WP4, WP5 | `tests/e2e/support/stub-backend.mjs`, `tests/e2e/specs/*journey*`, `docs/eval/platform-drafts-proposal.md` (new, a proposal only) |

### Session briefs

Each brief is self-contained. Paste one into a fresh session on `konradcinkusz/exit-interview-agent`.

**WP1 — Contracts and ADR-0083**
> Read AGENTS.md, docs/architecture/PROJECT-BRIEF.md, then docs/architecture/platform-drafts-spec.md §3.1, §3.3 (types only)
> and §3.4. Add the types exactly as named:
> - `PlatformProfile`, `PlatformSection`, `PlatformLimits`, `LocalizedText`, `DraftFamily`, `PlatformRisk`,
>   `IPlatformProfiles`, `PlatformDraft`, `DraftSection` in a new `Agent/Tiles/Platforms/PlatformContracts.cs`;
> - `TileSection` and the kinds `StructuredReview`, `Narrative`, `ProfessionalPost`, `ShortPost` in `TileContracts.cs`. Do
>   NOT remove the old kinds: WP3 does that;
> - `InterviewPlatformDraft`, and `Drafts` on `InterviewTiles` defaulting to empty. The service keeps sending an empty list.
>
> Write `schemas/platform-profiles.v1.schema.json` and `schemas/tile-writer-output.v2.schema.json` from §3.1 and §3.3. In
> `web/app/lib/interview-contract.ts`, add the draft types, make `parseResult` skip unknown item kinds, and default
> `drafts` to `[]`; test both. Write ADR-0083 from the template. It cites research §3.11 for retiring `google_review` and
> amends ADR-0074 and ADR-0075.
> Acceptance: `dotnet build -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes`, web
> lint/typecheck/test/build and `python3 scripts/check-doc-links.py` are green; no behaviour changes; a schema test loads
> both new schemas.

**WP2 — Profile data, loader, per-profile tests**
> Read the spec §3.1–3.2 and research §4, and use only the WP1 types. Write `platform-profiles.v1.json` with the ten
> profiles. Kununu has `enabled: false`. Every `sources[].level` is `search_summary` or `unverified` as the research marks
> it. The warnings are the research §4 texts, plus Polish versions you write in the same register as
> `InterviewNotice.Pl`, each ending with the fixed sentence. Implement `PlatformProfiles` (embedded resource, schema
> validation, fail fast). Write the invariant tests 1–6, 8 and 9 as `[Theory]` over all profiles, plus one named
> `[Fact]` per profile asserting its family, risk and URL host. Invariant 7 (goldens) belongs to WP4; leave a skipped
> placeholder only if the repository allows one, otherwise none.
> Acceptance: the gates above are green; changing any warning to empty, or any URL to http, fails a test (check it by
> hand once and say so in the PR).

**WP3 — Writer and guard by family; retire `google_review`**
> Read the spec §3.3, `TileGuard.cs`, `TilePrompts.cs`, `TileWriterOutput.cs`, ADR-0075. Then:
> 1. Replace the kinds `Glassdoor`, `GoogleReview`, `Reddit` and `ShortNote` with the four family kinds.
> 2. Build the writer prompt from a family table in code, with the limits from §3.3.
> 3. Parse the v2 schema into `CandidateTile.Sections`.
> 4. Apply per-section limits, add `too_short`, and add the `identifying_detail` check (EN and PL).
> 5. Allow experience terms only in `narrative`; copied-run limits are 13 for `narrative` and 7 for the rest.
> 6. Update the scripted mock to emit the four families in `pl` and `en`.
> 7. Add the red-team fixtures from spec §4.
> 8. Write a test that enumerates the leave-out enforcement table.
>
> Fix the one compile line in `Cli/Tiles/TileRenderer.cs`, and nothing else in that file. `TileSet.CurrentVersion` becomes
> `"2"`. Do not touch the service or the web.
> Acceptance: the gates are green, tests are written first for the guard rules, and no tile text is ever edited by code.

**WP4 — Draft builder, service and CLI wiring, goldens**
> Read the spec §3.3 (Builder), §3.4 and §3.5 (CLI). Implement `PlatformDraftBuilder` against the WP1 types. Develop it
> with fixture profiles and fixture family tiles; switch to the real ones when WP2 and WP3 are on `main`.
> - Wire it into `InterviewSessions.BuildTilesAsync`: `items` carries the generic kinds only, and `drafts` carries the
>   built drafts.
> - Render the drafts in the CLI text, HTML and JSON output, and add `notice` to the JSON.
> - Add the golden files for each enabled profile in `pl` and `en` from the scripted mock, plus the meta-test (invariant 7).
> - Write a property test that rendering never edits text.
>
> Acceptance: the gates are green, and the service's result test asserts that `drafts` exist and that no platform kind is
> in `items`.

**WP5 — Web UI**
> Read the spec §3.5 and §3.6, ADR-0051 (message catalogue and accessibility gate) and ADR-0047 (CSP), and use only the WP1
> types. Build `PlatformDrafts.tsx` with:
> - the order by market, with the "Other platforms" `<details>`;
> - one card per draft: risk in words, warning `role="note"`, per-section copy, Copy all, the fields-on-site list, and an
>   "Open {name}" link with `target=_blank rel="noopener noreferrer"` and the visible new-tab hint;
> - the high-risk acknowledgement gate, held in state only.
>
> Put the chrome strings in `messages/interview.ts` (EN and PL). Remove the platform kinds from `kinds`, and add labels for
> the generic kinds. Update `tilesToHtml` and `tilesToJson`. Test with fixture data in both languages.
> Acceptance: `pnpm lint && pnpm typecheck && pnpm test && pnpm build` are green; there is no Google card; the copy buttons
> of a high-risk card are disabled until the box is ticked; the link has no query string.

**WP6 — Docs, legal drafts and source re-check**
> From a session with normal web access, open every URL in research §3 and set each fact's level to `fetched` or keep it.
> Record the date, and correct any number that differs; say which ones did. Fill research §6 with the outcome. If Kununu's
> rules are now readable, propose enabling it in a separate follow-up PR. Then:
> - update `draft-tiles.md` (status, link to this spec, `google_review` retired) and the README tiles row;
> - in `terms.{en,pl}.md` §3, replace "Google reviews" with the chosen platforms and add a sentence that some platforms
>   forbid reviews by former employees;
> - add notes to `CHECKLIST.md` L2 from research §3.13, keeping it NOT REVIEWED;
> - add the open problem from spec §5 to `OPEN-PROBLEMS.md`.
>
> No legal text becomes "reviewed". Run `python3 scripts/check-doc-links.py`. If the network blocks you, say so and change
> only the docs.

**WP7 — E2E, mutation record, eval proposal**
> After WP4 and WP5 are merged:
> - Make the stub backend return `drafts` for PL and EN.
> - Extend the PL and EN journeys: market order, no Google card, the high-risk gate, the per-section copy, and the link
>   attributes.
> - Run the four mutations from spec §4 by hand, record the failing test for each in the PR, and revert them.
> - Write `docs/eval/platform-drafts-proposal.md`: draft-level Layer-1 constraints and the real-model review protocol of
>   §4, marked as a proposal that needs its own review under ADR-0040. Do not change `docs/eval/SPEC.md` or the baseline.
>
> Acceptance: the e2e suite is green after the web build; the doc link check passes.
