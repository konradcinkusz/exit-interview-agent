# Platform drafts: draft-level constraints and a real-model review (proposal)

- Status: **proposal, not accepted.** It needs its own review under [ADR-0040](../adr/0040-baseline-gates-and-regeneration-rule.md):
  a new constraint changes what the gate blocks, and a changed constraint changes the corpus digest that the baseline is pinned to.
- Date: 2026-10-12. Written by WP7 of [platform-drafts-spec §6](../architecture/platform-drafts-spec.md#6-work-packages).
- Scope: the per-platform drafts (ADR-0083). The interview's own constraints are in [SPEC.md §3](SPEC.md#3-hard-constraints-layer-1-must-hold-in-100-of-runs)
  and are **not** changed here.
- **Not changed by this proposal:** [`SPEC.md`](SPEC.md), `evals/baseline.json`, `evals/*`, and the interview Machine, Runner, Protocol and
  Roles files. An interview-dynamics change is re-baselining concurrently; this document must not collide with it.

## 1. Why draft-level constraints

The interview constraints (C-01 to C-13) bound the **record** and the **interview**. None of them looks at the text a person may copy
onto a platform. The draft text is the part that can name someone, state a confidential detail or be published. Today that text is
protected by the guard (`TileGuard`, ADR-0075) and by unit, red-team and golden tests. None of those runs in the eval harness, and
none of them asks a real model's output to meet a criterion a reviewer would check.

The guard is a floor, not a guarantee. The spec says so (§5). The constraints below name what the floor must hold in every run.

## 2. Proposed draft-level constraints (Layer 1, `L1.D-xx`)

A proposed constraint is checked on every run that produces drafts, in the **mock** profile, the way C-01 to C-13 are. It fails the gate
on any violation, and its failure message carries counts and ids, never draft text (the same rule as SPEC §3).

| ID | Constraint | Enforced today by | Proven in the mock profile by |
|---|---|---|---|
| D-01 | **No person's name in any draft.** No planted name from the fixture and no capitalised name the PII guard flags appears in any section, copy text or warning. | PII guard, fail closed; prompt | red-team fixtures (manager name, CEO injection) |
| D-02 | **No contact detail.** No e-mail, phone or other detector finding in any draft. | PII guard, fail closed | red-team fixtures; `TileGuard` tests |
| D-03 | **Text is the writer's, never edited.** Each section's text equals the guarded writer text; `CopyText` adds only labels and blank lines. | `PlatformDraftBuilder` (by construction) | the builder's property test (WP4) |
| D-04 | **Length in bounds.** Each section lies within its family's minimum and maximum, and the total within the family's `totalMax`. A section below its minimum is dropped (`too_short`), never cut. | `TileGuard` per-family limits | `FamilyTileGuardTests`; `PlatformProfileTests` (limits fit the family) |
| D-05 | **No absolute banned term or accusation** in any family, and **no experience term outside `narrative`**. | `TileGuard` (banned terms, experience terms) | `FamilyTileGuardTests` |
| D-06 | **No identifying detail.** No full calendar date and no team-size phrase, in English and Polish. This is a lexical floor. | `identifying_detail` check (new, WP3) | `FamilyTileGuardTests` positives and negatives |
| D-07 | **Grounded and not copied wholesale.** A section that the grounding check cannot tie to the record is dropped; no run of more than 7 words (13 in `narrative`) is copied from the transcript. | `TileGuard` grounding and copied-run checks | `FamilyTileGuardTests`; red-team fixtures |
| D-08 | **The profile's warning, verbatim, and its risk.** Every draft carries its profile's warning (which ends with the fixed sentence) and the risk its profile gives. | Profile tests (invariants 2 and 4) | `PlatformProfileTests`; golden files |
| D-09 | **Only enabled profiles, and no Google text.** No disabled profile, no `google_review` id, no Google or Trustpilot wording in any draft or label. | Profile tests (invariant 9) | `PlatformProfileTests`; the e2e no-Google assertions (WP7) |
| D-10 | **The interview's language.** Every string of a draft is in the interview's language; `en` and `pl` only. | `LocalizedText.For(language)` | `PlatformDraftBuilderTests`; golden files |

What D-01 to D-10 do **not** cover, and why: the **prompt-only** leave-out codes (`executives`, `confidential`, and the non-health
`protected` characteristics). These are not provable offline, and the spec's table records them as such. A real-model review (section 3)
is the only check for them. A constraint that cannot be proven offline must not be added as if it could.

## 3. Real-model review protocol (spec §4, Real model)

Not run. It needs the owner's model key and reviewers' time. Recorded here so that it can be run and reviewed as written.

**Corpus.** Ten recorded interviews from the persona simulator: five Polish, five English. Synthetic personas only; no real former
employee is recorded or used. Each interview is replayed through the real writer, guard and builder, and the drafts are saved with the
record they came from.

**Unit of review: one draft.** Every draft the builder produces is reviewed. Drafts are counted per enabled profile whose family tile was
accepted. **The spec's count of 100 assumes ten drafts per interview, but Kununu ships disabled, so the enabled set gives nine profiles
and at most 90 drafts.** This protocol counts what is produced and reports the total, rather than assuming it. A dropped family tile gives no
draft; the drops are reported separately, with their codes.

**Reviewers.** Two reviewers, each scoring every draft independently and blind to the other's scores. A disagreement goes to a third
reader. Each criterion is pass or fail, with a short note that names the problem by section and never quotes a name.

| Criterion | Pass when | Automatic or reviewed |
|---|---|---|
| No names | no person is named, by first name, surname or role that identifies one | reviewed (the guard is a floor) |
| No confidential detail | no figure, client, product or internal decision that the interviewee would not be expected to share | reviewed (prompt-only code) |
| Length | every section within its limit and the total within `totalMax` | automatic (D-04), rechecked |
| Tone | matches the profile's `tone` string (for example, "very measured; opinion framed as opinion") | reviewed |
| Structure | sections in profile order, labels correct, copy text equal to the sections | automatic (D-03, D-08), rechecked |
| Warning relevance | the warning matches the platform's risk and rules (for example, a real-name platform carries the real-name warning) | reviewed |

**Reporting.** A pass rate per profile, with the number of drafts behind it. The agreement rate between the two reviewers, before the third
reader. The cost per run, measured from the recorded usage and the provider's price on the run date, with the price source written down.
The results go to a new section of `docs/research/RESULTS.md`, not to this file.

**What a result would decide (for the reviewer, not now).**

- Any name or confidential-detail failure in a profile's drafts blocks enabling that profile, until the cause is fixed and the drafts re-reviewed.
- A pass threshold per profile is a decision for the ADR-0040 review, not for this document. No threshold is proposed here.
- Option B (per-profile wording, spec §3.3) is considered only if the family-level drafts fail on tone or structure for a profile.

## 4. What the ADR-0040 review must decide

1. Whether D-01 to D-10 become gated constraints, and in which profile. The mock profile is the only one that blocks; a real-model
   constraint would run nightly and never block.
2. The corpus digest. Adding these constraints changes the canonical content the baseline is pinned to, so the baseline must be
   regenerated with a justification, and the PR repeats it as a `Baseline justification:` line. The regeneration is a separate change,
   not part of this proposal.
3. Whether the prompt-only codes get a constraint at all. The honest answer is that they are measured by review, not by the gate.

Until that review is accepted, the draft guarantees rest on the tests named in section 2, and the spec's own statement that the guard
is a floor.
