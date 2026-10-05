# 0040. Gates, the committed baseline, and the rule that regenerating it needs a justification

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): `ai-evals` §6 and §9, `metric-ethics` §2 (the counter-metric is gated with its primary), `research-documentation` (every number reproducible)

## Context

A suite that cannot tell a regression from a Tuesday is decoration, and a baseline that is regenerated casually is a ceiling that moves with the code.

## Decision

- **Constraints**: 100% of runs, hard block, the baseline is never consulted. Any failing `L1.C-xx` anywhere fails the gate; any failing assertion in a constraint-gated scenario fails it; a harness error fails it and is never counted as a pass or as a caught failure.
- **Behaviours**: each gated metric must not be worse than `evals/baseline.json` by more than its stated tolerance (an absolute change in rate; `default` 0 for the deterministic mock profile, because with no sampling any change is a change of behaviour, and a reviewer should read it). Lower-is-better metrics (leading-question rate, over-probe rate, double-barrelled rate) are gated on their rise. **A primary and its counter-metric are separate gated lines**, so neither can improve by hiding a regression in the other (coverage and depth, follow-up on vague and over-probing). Tokens per interview are gated with a relative tolerance (10%) so that a cheaper-and-worse change and a costlier-and-same change are both visible. Improvements are printed, never blocked.
- **The gate runs the `mock` profile only.** Real-model profiles are nightly and never block.
- **The baseline is pinned** to the spec version, the harness version, the profile and a **corpus digest** (the canonical content of the scenarios, the label sets, the rubric and judge prompt, the schema, the persona data and the protocol). The harness refuses to compare across any of them and says to regenerate. An unrecorded scenario and a stale entry both fail.
- **Regeneration**: `exit-interview-eval baseline --justification "<why>"` (refused without it); it refuses to record a run with a harness error or a constraint violation. The justification is stored in the file, and **the PR description must repeat it as a `Baseline justification:` line**, which CI checks whenever `evals/baseline.json` changes (`scripts/check-baseline-justification.sh`). Tolerances are the one part edited by hand, and are reviewed as the decision they are.

## Consequences

- Strict zero tolerance means every behavioural change in the mock path needs a justification line; that is intended, the corpus is small and the mock is deterministic.
- The gate and the digest do not cover a prompt change that keeps the corpus identical: `.github/workflows/ci.yml` runs the gate on every pull request regardless of the paths touched, which is the change-detection rule of `ai-evals` §6 in its simplest form.
