# 0039. Layer 2: a pinned, rubric-anchored judge that gates nothing until humans have calibrated it

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): `ai-evals` §5 (calibrated against humans or discarded; a non-human first rater is a rehearsal), `metric-ethics` §3 and §4, threat model T-05 (judge manipulation)

## Context

Two properties of an interview cannot be decided by rules: whether a question's wording is neutral, and whether a follow-up is a good probe. A judge model can score them, but an uncalibrated judge that gates a merge blocks work on a number nobody has checked against a person.

## Decision

- **Rubric-anchored per criterion** (`evals/rubrics/judge.yaml`: R-01 neutral wording, R-02 probe quality, levels 0 to 2, every level with an anchor; a rubric with a gap fails to load). **Pinned**: the model id comes from configuration and is recorded with the id that actually answered; the SHA-256 of the rubric and of the prompt template (`judge-prompt.md`) are recorded in every report.
- **Judge input is untrusted data.** Interview text appears only inside a nonce-marked block of one JSON object per line, never in the system prompt; the reply is parsed strictly (exactly `score` and `justification`, score in range, anything else is `invalid`, never a pass). A **judge-injection differential** scores the same item with and without the instruction-like sentences and reports whether the score moved; it runs on the judge-targeting scenario whenever a judge exists. Tests use a judge double that obeys instructions in the data to prove the differential detects it.
- **Layer 2 blocks nothing.** Scores are thresholded and trended; the calibration gate (at least 40 labels, at least 8 items, unweighted Cohen's kappa at least 0.6, thresholds taken from the reference implementation) additionally requires labels with `labeller_kind: human` under the configured owner handle. The gate's state is printed on every run.
- **The committed label set is author-labelled and non-human**: 48 items (two rubrics) and, separately, 74 replies and 16 reply pairs for the vagueness and contradiction experiment, hand-labelled with written rationales by the AI session that wrote the harness, before any classifier or judge ran. It is a **rehearsal of the protocol and a weak calibration at best**: the same author wrote the rubric and the labels. It counts for nothing towards the gate (tested). The reproducible command `eval calibrate` computes what is computable offline (the rule screens' and the reply analyser's agreement with the labels, with bootstrap intervals) and reports `skipped:no-credential` for the judge and the model-assisted classifier.
- **The experiment T4 recommended** (a model-assisted vagueness and contradiction classifier compared with the rule-based analyser) is implemented on the same labels and the same skip rule.
- Without a credential Layer 2 reports `skipped:no-credential`; a report never shows an empty Layer 2 as a pass.

## Consequences

- A keyed run is possible with one environment variable and the T6 factory; until labels from a human exist, its scores stay advisory. [OP-11](../OPEN-PROBLEMS.md#op-11-judge-calibration-labels) stays open.
- Layer 1 never reads judge output, so a fooled judge cannot waive a constraint (tested).
