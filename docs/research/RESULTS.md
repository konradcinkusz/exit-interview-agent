# Results: what was measured, with what, and what it does not show

Date: 2026-10-05. Commit under test: `062cd48` (`main` before this document was corrected). Environment: Linux sandbox, .NET SDK 10.0.112, no network access to any model provider, no Docker, no PostgreSQL.

**How to read this document.** Every number below was produced by the command printed next to it, run on the commit above. Where a number was not re-run in this pass it is marked *not re-run*, with its source. **None of the results in this document says anything about how a real language model behaves.** The interview agent was only ever run against a scripted mock model that understands nothing; the LLM judge scored nothing; the calibration labels were written by the same AI session that wrote the rules they are compared with; no real person, employer or record was involved at any point. The results show that the code-side protections hold and that the harness can detect their failure, and they report how well three rule-based classifiers agree with author labels. That is all.

An earlier version of this file (pushed directly to `main` by a Haiku-class session without running the commands) contained wrong figures (26 scenarios, "Happy 3/3", a constraint table that did not match `docs/eval/SPEC.md`, an invented token statistic). It was replaced by this one.

## 1. Evaluation harness, mock profile

Command: `scripts/run-evals.sh all` (validate, gate, report, calibrate). Exit code 0.

| Fact | Value |
|---|---|
| Scenarios / runs | 27 scenarios, 45 runs (one per scenario and seed) |
| Classes | happy 2, ambiguity 4, hostile 2, adversarial 8, degradation 9, consent 2 |
| Constraint-gated / behaviour-gated scenarios | 15 / 12 |
| Spec version, corpus digest | 1.0.0, `sha256:28b3204f76d07435` |
| Assertions evaluated, harness errors | 433 constraint assertions, 0 harness errors |
| Gate | PASSED (Layer 1 only; Layer 2 is advisory and blocks nothing) |
| Determinism | `exit-interview demo --persona talkative --seed 1` run twice gave byte-identical output (112 lines) |

### 1.1 Hard constraints (`docs/eval/SPEC.md` §3), fail / pass / not applicable over 45 runs

All twelve have **0 failures**. "Not applicable" means the run had no situation to test the constraint (for example C-03 applies only to the 4 runs that withdraw or abandon).

| ID | Constraint (short) | fail / pass / n.a. |
|---|---|---|
| C-01 | No individual's name stored | 0 / 41 / 4 |
| C-02 | No other PII in the record | 0 / 41 / 4 |
| C-03 | Consent withdrawal means no record | 0 / 4 / 41 |
| C-04 | `aiDisclosed` only after disclosure | 0 / 45 / 0 |
| C-05 | Injection never alters protocol or structure | 0 / 45 / 0 |
| C-06 | Quotes are verbatim | 0 / 37 / 8 |
| C-07 | No affect field | 0 / 37 / 8 |
| C-08 | Canary absent from telemetry | 0 / 15 / 30 |
| C-09 | Termination by decision, within bounds | 0 / 45 / 0 |
| C-10 | Only declared operations | 0 / 45 / 0 |
| C-11 | No per-person identifier or timestamp field | 0 / 37 / 8 |
| C-12 | No record without a validated extraction | 0 / 41 / 4 |

### 1.2 Behaviour metrics (k/n, 95% Wilson interval; intervals are over scenarios and seeds, not over model sampling)

| Metric | Result |
|---|---|
| Topic coverage (B-01) | 120/120 = 100.0 % [96.9, 100.0] |
| Depth of covered topics (counter-metric of coverage) | 104/120 = 86.7 % [79.4, 91.6] |
| Leading-question rate (B-02) | 0/60 = 0.0 % [0.0, 6.0] |
| Follow-up on vague answers (B-03) | 33/33 = 100.0 % [89.6, 100.0] |
| Over-probe rate (counter-metric) | 0/51 = 0.0 % [0.0, 7.0] |
| Contradiction clarified once (B-04) | 3/3 [43.9, 100.0] |
| Hostile or terse interviewee released (B-05, B-06) | 7/7 [64.6, 100.0] |
| Budget respected (B-07) | 1/1 [20.7, 100.0] |
| Degradation graceful (B-08) | 7/7 [64.6, 100.0] |
| Names masked and redirected once (B-09) | 5/5 [56.6, 100.0] |
| Edge-case handling vs the persona's expectation (B-10) | 32/32 [89.3, 100.0] |
| Double-barrelled questions (report) | 0/60 = 0.0 % [0.0, 6.0] |
| Transcript fidelity / quote support | 323/323 and 107/107 |
| Leading questions: guard rules / independent rules / disagreements | 0/60, 0/60, 0/60 |

Many cells have a small n (1 to 7); their intervals are wide and a "100 %" there is weak evidence. A scripted model cannot be talked into anything, so most of these are 100 % by construction; what they test is the harness and the code around the model.

Protocol change visible in the baseline (`evals/baseline.json`, recorded 2026-10-05): protocol 1.1 and the `QuestionGuard` rule (ADR-0062) took double-barrelled questions from 14/60 to 0/60 and mean tokens per interview from 8543.25 to 8407.3. The 8543.25 figure is the pre-1.1 baseline, not a current one.

### 1.3 Calibration (`report/calibration.md`, same command)

| Comparison | Result |
|---|---|
| R-01 rule screen vs author labels (n=30) | exact 23/30, within-one 30/30, kappa 0.55, 95 % bootstrap [0.30, 0.80] |
| R-02 rule screen vs author labels (n=18) | exact 16/18, within-one 18/18, kappa 0.77, [0.37, 1.00] |
| Reply analyser vs labels, vagueness (n=74, 3 classes) | exact 55/74, kappa 0.56, [0.38, 0.73]; recall of the vague class 11/21 = 52.4 % [32.4, 71.7] |
| Reply analyser vs labels, contradiction (n=16) | exact 12/16, kappa 0.52, [0.16, 0.88]; recall 5/9 = 55.6 % [26.7, 81.1] |
| LLM judge | **skipped:no-credential**. Judge-versus-label agreement was not computed. |
| Model-assisted classifier experiment | **skipped:no-credential**. Not run. |
| Calibration gate | **Not calibrated**: 0 of 40 required human labels. All 48 judge labels are `ai-author`, so none count. Layer 2 scores block nothing. |

The reply analyser finds about half of the vague answers and about half of the contradictions in the label set. That is a real limitation of the rule-based analyser, measured against labels that are themselves weak (AI-written).

## 2. PII detector

Command: `dotnet test tests/ExitInterviewAgent.Privacy.Tests --filter "Category=PiiEvaluation" --logger "console;verbosity=detailed"`. Synthetic English and Polish corpora written by the same author as the rules; the held-out set was written before the detector was run on it, but this remains a regression guard and an optimistic one, not a prediction for real transcripts.

| Corpus / mode | Gold spans | Recall | Precision |
|---|---|---|---|
| dev / default | 68 | 68/68 = 100 % (95 % CI 95-100) | 68/68 = 100 % |
| dev / fail-closed | 68 | 68/68 = 100 % | 68/74 = 91.9 % (CI 83-96) |
| held-out / default | 49 | 47/49 = 95.9 % (CI 86-99) | 47/47 = 100 % (CI 92-100) |
| held-out / fail-closed | 49 | 49/49 = 100 % (CI 93-100) | 49/52 = 94.2 % (CI 84-98) |
| over-masking set / fail-closed (7 names, written for the defects fixed in ADR-0063) | 7 | 7/7 | 7/7 |

The two held-out misses in default mode are unknown given names standing alone mid-sentence. Names of people with no other cue, other languages, and identification without a name are not caught (`docs/privacy/pii-detector.md`). The obfuscated-email rule's worst-case cost was cut from 1358 ms to 0.0 ms on 200 000 unbroken letters by ADR-0063 (figures from the PR #17 description; *not re-run in this pass*).

## 3. Aggregation intervals

Command: `dotnet test tests/ExitInterviewAgent.Signals.Tests --filter IntervalCoverageTests --logger "console;verbosity=detailed"`. Both tests passed. Printed coverage of the nominal 95 % interval, n=5 to n=40, over the synthetic battery, ranged from 93.3 % to 100 % in the columns visible in the console line (for example n=5: uniform 97.1 %, polarised 93.3 %, extreme 50/50 94.4 %); the test asserts that every distribution stays above 90 %. For a near-degenerate population at n=5 the plain t interval covers 22.9 % and the regularised one 100 %. The T10 session's reported minimum of 92.4 % (n=20, rare low tail) is in a column the console line truncated; *not re-read in this pass*. The prior's constants were tuned on this same battery; the cells are not independent draws; ratings are model output and that error is not in the interval; nothing was checked on real data.

## 4. Test suite

Command: `dotnet build -warnaserror` (0 warnings, 0 errors) then `dotnet test --no-build`. No failures.

| Project | Passed | Skipped |
|---|---|---|
| Records | 110 | 0 |
| Personas | 18 | 0 |
| Agent | 401 | 0 |
| Privacy | 184 | 0 |
| Cli | 130 | 0 |
| Providers | 155 | 2 (`Category=Live`, need a provider key) |
| Signals | 102 | 0 |
| Eval | 161 | 0 |
| InterviewService | 359 | 15 (PostgreSQL-only; CI sets `TEST_POSTGRES_CONNECTION` and ran them) |
| **Total** | **1620** | **17** |

Web (Vitest and Playwright) and the image builds were not re-run in this pass; CI ran them on every merged pull request.

## 5. Dependency audit

- NuGet: `dotnet list package --vulnerable --include-transitive` reports no vulnerable package in any project.
- npm: `pnpm audit` in `web/` reports 1 high: `braces`, vulnerable `<=3.0.3`, patched "None", advisory GHSA-vfj7-8cjw-p6xm, path `app>eslint-config-next>@next/eslint-plugin-next>fast-glob>micromatch>braces`. `pnpm audit --prod` reports "No known vulnerabilities found", so it is not in the production dependency set. Mitigation is an owner decision (`docs/OWNER-FOLLOWUPS.md`).

## 6. What was never measured

- Any real model, in any mode (API key, Ollama, or Claude through MCP). No successful provider call was made by any session; one deliberately invalid key reached `api.anthropic.com` and got HTTP 401 (T6's report; not repeated here).
- The LLM judge, and anything a model-assisted classifier would add.
- Mode A host fidelity: whether Claude follows the protocol the MCP server exposes (OP-18). No real Claude client has connected to the server.
- Two-factor sign-in, registration and export against the real `authservice` image (OP-17): only a contract-mirroring stub was used. T2 reports driving a real authorization-code flow with PKCE against `authservice` built from source (not repeated here, not part of CI).
- The web pages against the real backend (OP-28), over real TLS, on Windows or macOS (OP-25), or under screen readers (OP-16).
- Recall of the PII detector on real transcripts.
- Anything about people or employers. No result in this repository is about a person or an employer.

## 7. What to measure next

1. Run the eval harness against one real model through `evals/profiles.yaml` with a key, n of at least 5 per scenario, and report intervals over model sampling.
2. Have human labellers (at least 40) label the judge set, then calibrate the judge before it blocks anything.
3. Run the mode A conformance suite: the same personas through Claude as host.
4. A full-stack journey against the AppHost with `Signals:Demo:Mode=Seed` (closes OP-17 and OP-28).
5. Measure the PII detector on a larger corpus not written by its author.
