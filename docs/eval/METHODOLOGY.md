# Evaluation methodology

This document fixes **what is measured, on what, and how a number is allowed to be reported**, and records the numbers the harness produced. The behaviour
contract is [SPEC.md](SPEC.md) (version 1.0.0); a tour of the code and commands is [README.md](README.md). Status vocabulary:
[ADR-0017](../adr/0017-documentation-layout-and-claim-status.md). Guides loaded:
[`ai-evals`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md),
[`metric-ethics`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md),
[`research-documentation`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/research/00-RESEARCH-DOCUMENTATION.md),
[`testing-strategy`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/TESTING-STRATEGY.md),
[`demo-data-and-seeding`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/DEMO-DATA-AND-SEEDING.md).

**Status: Implemented (T7), mock profile only.** Every number in §11 and §12 comes from a command listed next to it ([§13](#13-reproduction-commands)), run on the
state of the repository this file ships with. **No real model has been evaluated and the Layer 2 judge has not scored anything** (no credential exists in the
environment that produced these numbers). A number that could not be measured is marked as such.

## 1. Unit of evaluation, and what is not evaluated

The unit is the **interview artifact**: for one simulated persona run, the triple *(transcript, OpenTelemetry trace, record)*, identified by (scenario, seed). Not
the person, not the employer ([metric-ethics §5](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md)).

**Anti-goals** (stated first, as the guide asks; each is enforced by an absence, not only by prose):

| Anti-goal | How the architecture makes it true | Check |
|---|---|---|
| The harness never scores an employer | the eval project has no employer entity and references only `Agent`, `Personas`, `Records` and `Privacy`; fixtures use fictional employers; the Signals module does not exist in its dependency graph | `ArchitectureTests` (**Implemented**) |
| Never scores or labels a person | personas are test inputs; "hostile" or "vague" describes a scripted input, not a judgement about any human; no real transcript is ingested (brief §2); no type in the harness models an emotion, sentiment or mood | `ArchitectureTests` (**Implemented**) |
| No single composite score for an interview configuration | the report is a table of metrics, each with its counter-metric and interval; there is no weighted total and no ranking in the report schema | `ReportTests` scans every key of the JSON report (**Implemented**) |
| No heuristic about a human's emotional state in any score | the record has no affect field (constraint C-07, graded on every run); no gated metric is about interviewee state | `ArchitectureTests`, C-07 (**Implemented**) |
| Not a leaderboard of models | the conformance report compares profiles on the same scenarios with intervals and states "no (intervals overlap)" when they overlap | `ReportTests` (**Implemented**) |

## 2. The two layers

Ported from the `ai-evals` guide ([§1, §4, §5, §6](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md)) and its reference implementation.

- **Layer 1: deterministic assertions over the trace and artifacts.** No model, no network. The twelve hard constraints are evaluated on every run of every
  scenario; behaviours are measured as k of n and compared with a baseline. Most coverage lives here. (**Implemented**.)
- **Layer 2: LLM judge** for what rules cannot grade (neutrality of wording, quality of probes). Rubric-anchored per criterion, a pinned model, SHA-256 of the
  rubric and prompt recorded, input treated as untrusted data, **`skipped:no-credential` rather than a silent green**, and gating nothing until calibrated against
  *human* labels ([ADR-0039](../adr/0039-layer-2-judge-and-calibration-policy.md)). (**Implemented, never run keyed**.)

## 3. Scenarios (corpus, YAML validated by JSON Schema)

Six classes, from [brief §7](../architecture/PROJECT-BRIEF.md) and the guide: happy, ambiguity, hostile, adversarial (injection aimed at the interviewer, the extractor and the
judge; manager naming; PII bait; a compromised model), degradation (timeout, 5xx, empty answer, malformed extractor JSON, missing usage, budget) and consent
withdrawal. The eight personas of T4 are used unchanged; scenario-level deltas (seeds, faults at the `IChatClient` seam, a canary, a `probe_reply`) replace new
personas. Counts are produced by `validate` ([§11](#11-the-first-run)); a validator refuses an empty class, a missing `why`, a constraint-class scenario without an absence
assertion, a dangling spec citation, a persona reply without a hand label, and an ill-formed fault. *Judge-targeting text* is the `prompt-injection` persona
(its `injectionTargets` include the judge) in adv-007; *special-category disclosure* was proposed in the skeleton and is **not covered**: the PII detector does not claim
to detect it ([SPEC §6](SPEC.md#6-out-of-scope-and-negative-findings)), so a scenario would grade a known gap, not a behaviour.

## 4. Metrics, formulas and counter-metrics

Formulas are normative in [SPEC §7](SPEC.md#7-metrics-counter-metrics-and-confidence); the implemented definitions, with the degenerate strategy each counter-metric catches:

| Metric | Formula (implemented) | Degenerate strategy | Counter-metric, reported in the same row and gated on its own line |
|---|---|---|---|
| `coverage` | covered topics / 6, over scenarios that measure it | rush: one question per topic | `depth` = covered topics with a quote showing a concrete detail (independent rule) / covered topics |
| `lqr` | questions flagged by `QuestionGuard.LeadingReason` **or** by the independent rules / questions the interviewee saw | never probe or clarify | `fuv` |
| `fuv` | hand-labelled *vague* answers followed by a probe / hand-labelled *vague* answers | interrogate everything | `opr` = probes after a *specific* answer or a *decline* / those answers |
| `clarified`, `released`, `budget_graceful`, `degraded_ok`, `redirected`, `edge_case` | per-run binary conditions defined in SPEC §4 | | pooled with a per-class breakdown; a failing class is never averaged away |
| `tf` / `qs` (report only) | record quotes found in the transcript / quotes; rated topics with at least one quote / rated topics; **undefined, not 1.0, with no quotes** | emit no quotes | each other |
| tokens, latency, cost (report only) | usage from the `chat` spans; wall time of spans (volatile); cost only from a user-supplied price table | cheap and useless | gated beside coverage (tokens, 10% relative tolerance) |

Two differences from the skeleton, with reasons: `fuv` uses **hand labels of the replies** (`evals/labels/vagueness.yaml`) as ground truth, because using the
agent's own `vague` signal would grade the analyser against itself; and the leading-question screen uses **two rule sets** (the guard's and an independent one).
Heuristics about the interviewee are not computed.

## 5. Confidence on every number

- Proportions: `k/n` and a **Wilson score interval at 95%** in the same payload (`n` is the denominator, not the run count). With no observations the rate is
  **undefined (`n/a`), never 0 and never 1**.
- Judge scores (ordinal): mean with a seeded **bootstrap interval** and the level distribution; agreement as unweighted Cohen's kappa with a seeded bootstrap interval,
  **undefined, never 1.0**, when all pairs fall in one category.
- Deterministic path: one run per (scenario, seed); the interval is **over scenarios and seeds, not over model sampling**. It says how much the corpus can tell, not how a
  model varies.
- Live-model nightly matrix: `n = 5` per scenario is the starting assumption (ported, **not implemented or justified here**: no keyed run exists); a constraint that holds
  4 of 5 times is a **failed** constraint, not an average.
- How to read a pair: a wide interval means *early, not wrong*; two values with overlapping intervals are not different (the report says so).

## 6. Hard constraints (Layer 1, gate at 100%)

The twelve constraints are in [SPEC §3](SPEC.md#3-hard-constraints-layer-1-must-hold-in-100-of-runs); each has an assertion id `L1.C-xx`. They replace the skeleton's K-1..K-9
(same intent, finer split: names and other PII are separate; the loop-ends-by-decision rule is C-09; "no write before confirmation" became the operation table and C-10 because the agent has
no write operation, and C-12 was added: no record without a validated extraction). A constraint blocks wherever it appears, in every scenario, whatever its gate.
"Write-classified" is a normative per-tool table, currently with no row, not a name prefix ([ai-evals §4](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md)).

## 7. Gates, baseline and judge calibration (policy, implemented)

| Layer | Trigger | Gate |
|---|---|---|
| Layer 1 constraints | every pull request (the `evals` CI job and `dotnet test` both run the gate) | **100%, hard block** |
| Layer 1 behaviours | same | not worse than `evals/baseline.json` by more than its tolerance, per metric line |
| Layer 2 | when a judge credential exists | **blocks nothing** until calibrated |
| Real-model profiles and the model matrix | nightly / pre-release (not run) | report and diff against the baseline; **never blocks** |

- **Baseline** is pinned to the spec version, the corpus digest, the harness version and the profile; the harness refuses to compare across any of them. A baseline never softens a
  constraint. Regeneration needs `--justification`, and the PR description must repeat it as a `Baseline justification:` line ([ADR-0040](../adr/0040-baseline-gates-and-regeneration-rule.md)).
- **Judge gate**: at least 40 labels, at least 8 items, unweighted kappa at least 0.6, **and** labels with `labeller_kind: human` under the owner's handle (thresholds from the reference
  implementation; unchanged, with no reason found to change them). A model as first rater is a rehearsal, and is labelled so; the committed labels are such a rehearsal.
- **Prove the suite can fail**: the 23 tests of `MutationTests` (broken variants of the artifacts, the PII guard and the harness itself) and a 12-mutation pass over the real Agent code ([MUTATION-EVIDENCE.md](MUTATION-EVIDENCE.md)).
- **Two kinds of skip**, reported separately: `skipped:no-credential` and `skipped:no-provider`; plus `skipped:unimplemented` for a scenario marked `skip` (none is).

## 8. Conformance report

`run --profile <name>...` runs the same corpus against each profile and writes JSON and Markdown: constraints, metrics with intervals and counter-metrics, per-class breakdown, scenarios, tokens and
cost, Layer 2 status and calibration, latency (volatile), and one column per profile. A profile that cannot run is listed with its skip reason. **Limits stated in advance**: in mode A the host runs the
interview and we see only the record, so process metrics (leading questions, follow-ups) cannot be measured from production traffic; host conformance is measured by driving the MCP protocol with a
simulated persona (T8), which approximates but is not the host's behaviour. Only the `mock` profile has run; the comparison across profiles is exercised by tests with two copies of the mock, which prove the
report's shape and its "not distinguishable" logic and **say nothing about any model**.

## 9. What is ported from `konradcinkusz/agent-eval-bench`, and what is not

Read in this session (read-only clone of the public repository): `docs/SPEC.md`, `docs/CALIBRATION.md`, `evals/README.md`, `evals/baselines/README.md`, `evals/schema/`, `evals/rubrics/`, and the
harness under `tests/AbsenceConcierge.Evals/` (`ScenarioRunner`, trace recording, `AssertionEvaluator`, `Calibration`, `Baseline`, `RubricJudge`, the broken-agent variants).

| Ported (concept, adapted; no code copied) | Where it lives here |
|---|---|
| spec first, constraints separated from behaviours, spec version bumped with behaviour | `docs/eval/SPEC.md` |
| scenarios as strict-schema YAML, `why` and `gate`, five-class structure, the validator rules (empty class, two-assertion rule, citations both ways) | `evals/`, `Scenarios/Corpus.cs` |
| trace events as the contract; a normative operation table instead of a name prefix | SPEC §2, `OperationTable`, C-10 |
| determinism: no sampling on the gated path, one run per (scenario, seed) | SPEC §8 |
| baseline pinned to the spec version and the fixtures; refusal across a change; a digest of what is measured | `Reporting/Baseline.cs`, `Corpus.Digest` |
| rubric anchors with a load-time check, hashed rubric and prompt, calibration gate, kappa undefined rather than 1.0 | `Layer2/` |
| deliberately broken variants the constraint layer must catch; a harness crash is never a catch | `MutationTests`, `scripts/mutate-agent.py` |
| two kinds of skip; a judge that skips is reported, not green | `ProfileCatalog`, `Layer2Run` |

| Changed here, and why |
|---|
| the domain: this agent has no write tool in interview mode, so the "confirmation before write" constraint became the operation table plus C-03 (no record after withdrawal) |
| Layer 1 grades a trace, a record **and** a transcript, because the record is the product here; transcript fidelity and PII leakage are artifact measures |
| faults at the `IChatClient` seam and a **compromised-model** simulation replace tool-result fixtures: the equivalent of "injection through tool results" is a model that has been fooled |
| the corpus digest covers the label sets, the persona data and the protocol, not only scenario files and fixtures |
| the leading-question screen runs two rule sets and reports their disagreement; the reference has no such cross-check |
| constraint C-03 carries an independent screen for explicit withdrawals, found necessary by the mutation pass ([MUTATION-EVIDENCE](MUTATION-EVIDENCE.md)) |
| its numbers: nothing from its results is reused; every number here is produced by this repository's own run |

## 10. Reporting conventions

Results are written following [`research-documentation`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/research/00-RESEARCH-DOCUMENTATION.md): every number is traceable to a command (§13); claim is
separated from evidence; negative and surprising results are written up (§12); a result pins the code it describes (the corpus digest and spec version are in every report). The full results write-up is T12.

## 11. The first run

All numbers below: `dotnet run --project src/ExitInterviewAgent.Eval -- run --profile mock --deterministic --out report/` unless another command is named; profile `mock`
(scripted model), spec 1.0.0, corpus digest `sha256:28b3204f76d07435`. They describe **the mock and the code-side protections**, nothing else.

**Corpus** (`validate`): 27 scenarios, 45 runs (one per scenario and seed); happy 2, ambiguity 4, hostile 2, adversarial 8, degradation 9, consent 2; 15 constraint-gated and 12 behaviour-gated.

**Constraints** (fail / pass / not-applicable over the 45 runs): C-01 0/41/4, C-02 0/41/4, C-03 0/4/41, C-04 0/45/0, C-05 0/45/0, C-06 0/37/8, C-07 0/37/8, C-08 0/15/30, C-09 0/45/0,
C-10 0/45/0, C-11 0/37/8, C-12 0/41/4: **no constraint failed in any run**, 433 constraint assertions evaluated (`gate`). Every constraint is applicable and passes in at least one run (a test asserts it,
so none is green by being always "not applicable"). *Reading:* the code-side protections held on this corpus; this is not a statement about a model.

| Metric (higher is better unless marked) | k/n | rate | 95% Wilson interval | Counter-metric in the same row |
|---|---|---|---|---|
| Topic coverage (B-01) | 120/120 | 100.0% | [96.9%, 100.0%] | depth 104/120 = 86.7% [79.4%, 91.6%] |
| Leading-question rate (B-02, lower) | 0/60 | 0.0% | [0.0%, 6.0%] | fuv below |
| Follow-up on vague (B-03) | 33/33 | 100.0% | [89.6%, 100.0%] | over-probe rate (lower) 0/51 = 0.0% [0.0%, 7.0%] |
| Contradiction clarified once (B-04) | 3/3 | 100.0% | [43.9%, 100.0%] | |
| Hostile or terse released (B-05, B-06) | 7/7 | 100.0% | [64.6%, 100.0%] | |
| Budget respected (B-07) | 1/1 | 100.0% | [20.7%, 100.0%] | |
| Degradation graceful (B-08) | 7/7 | 100.0% | [64.6%, 100.0%] | |
| Names masked and redirected once (B-09) | 5/5 | 100.0% | [56.6%, 100.0%] | |
| Edge-case handling vs the persona's expectation (B-10) | 32/32 | 100.0% | [89.3%, 100.0%] | |
| Double-barrelled questions (report, lower) | 0/60 | 0.0% | [0.0%, 6.0%] | was 14/60 = 23.3% before protocol 1.1 ([ADR-0062](../adr/0062-double-barrelled-questions-protocol-1-1-and-guard.md)) |
| Transcript fidelity (report) | 323/323 | 100.0% | [98.8%, 100.0%] | quote support 107/107 = 100.0% [96.5%, 100.0%] |

*Reading these:* 100% with a small `n` has a wide interval (B-07 is one observation: the interval is [20.7%, 100.0%], which is the honest statement that one run cannot say much). With a deterministic mock, 100% on
`coverage` and `fuv` is partly the corpus being built from replies the agent was designed to handle; a rate over personas **chosen to exercise the protocol** is not a rate over interviewees.
The two rule sets for leading questions agree on every one of the 60 questions (0 flagged by either, 0 disagreements): with the mock the questions are the protocol's fixed wording, so this measures the protocol's
wording, and the compromised-model scenarios are where the guard actually works (adv-004).

**Tokens** (from the `chat` spans; the mock reports an estimate of four characters per token): mean 8896.3 per interview, min 1908, max 90000 (the budget scenario), 327 model calls in total, 336090 input
and 64244 output tokens. **Cost: not computed** (no price table was supplied; the repository ships none). Latency is volatile and with the mock measures the harness; it is in the report's `volatile` block only and is not quoted here.

**Gate and determinism.** `gate` evaluates 45 runs and passes in about 2.5 seconds wall time (`time dotnet run --project src/ExitInterviewAgent.Eval --no-build -- gate`). Two complete `--deterministic` runs produce
byte-identical `report.json` (CI diffs them on every pull request; a unit test does the same in-process).

**Mutation proof** ([MUTATION-EVIDENCE.md](MUTATION-EVIDENCE.md)): 12 of 12 weakenings of the real Agent code are caught by the gate (`python3 scripts/mutate-agent.py`); the first pass caught 11 and found the
missing scenario (amb-004) and the vacuous withdrawal check (C-03) that the corpus now has. The Eval project has 161 tests (`dotnet test tests/ExitInterviewAgent.Eval.Tests`), 23 of them in `MutationTests`.

**Judge and calibration** (`calibrate`): **no judge ran** (`skipped:no-credential`), so judge-versus-label agreement is **not measured**. What is measured offline, against labels written by the harness's own author
(48 judge items; 74 replies; 16 pairs; all non-human, so a rehearsal and a weak calibration):

| Comparison (offline) | n | exact agreement | Cohen's kappa | 95% bootstrap interval |
|---|---|---|---|---|
| R-01 rule screen vs author labels (acceptable = level 2) | 30 | 23/30 | 0.55 | [0.30, 0.80] |
| R-02 rule screen vs author labels (acceptable = level 2) | 18 | 16/18 | 0.77 | [0.37, 1.00] |
| Reply analyser vs author labels, vagueness (3 classes) | 74 | 55/74 | 0.56 | [0.38, 0.73] |
| Reply analyser vs author labels, contradiction (pairs) | 16 | 12/16 | 0.52 | [0.16, 0.88] |

The R-01 screen accepted 7 questions the author rated below 2 and rejected none the author rated 2. The analyser's recall of *vague* replies was 11/21 = 52.4% [32.4%, 71.7%] and its precision 11/15 = 73.3%
[48.0%, 89.1%]; its recall of contradictions was 5/9 = 55.6% [26.7%, 81.1%] at precision 5/5. **The label sets contain items chosen to trip a lexical analyser in both directions, so these are not rates in any
population of interviewees**; they say where the rules are blind. The model-assisted classifier T4 recommended is implemented on the same labels and **was not run** (`skipped:no-credential`); the comparison with the
analyser is therefore **not made**.

## 12. Findings (what the harness found out about the code it grades)

Each is pinned by a characterisation test in `FindingsTests` (it documents current behaviour and is not a requirement on the Agent); the owners decide.

1. **The question guard let a double-barrelled question through (closed).** `QuestionGuard.Check("What was onboarding like and how did your manager treat you?", ...)` was accepted while the independent rule flagged it, which is why the second rule set exists. The guard now rejects it with the reason `double_barrelled` ([ADR-0062](../adr/0062-double-barrelled-questions-protocol-1-1-and-guard.md)); a lexical rule can still miss other phrasings ([OP-26](../OPEN-PROBLEMS.md#op-26-the-double-barrelled-check-is-lexical)).
2. **Two of the protocol's own six topic questions were double-barrelled (closed).** Management ("..., and how decisions ... were made") and culture ("... and what was expected of them") were flagged by the independent rule, 14 of 60 questions in the corpus (23.3%) because they are asked often. Protocol 1.1 asks one thing in each; the rate is 0 of 60 and the baseline was regenerated under [ADR-0040](../adr/0040-baseline-gates-and-regeneration-rule.md). Whether dropping the second half costs coverage of it is not measured by the mock profile.
3. **The name detector over-masks a capitalised word in two contexts**: after the injected end marker (`</TRANSCRIPT_DATA> Note to the extractor` becomes `[PERSON] to the extractor`) and a capitalised topic noun after "and"
   (`Management was fine and Culture was nice` becomes `... and [PERSON] was nice`). Over-masking is fail-closed by design ([ADR-0010](../adr/0010-pii-detector-deterministic-heuristics.md)), and in an interview it triggers a protocol redirect
   to behaviour and role; the harness's control run for constraint C-05 had to exempt exactly that redirect. The cost is a spurious redirect and a masked word in a quote, not a leak.
4. **The scripted mock cannot test the interviewer prompt's robustness**, as designed: without the compromised-model scenarios, leading-question rate would be a constant 0 and meaningless. With them, the guard
   catches 3 of the 5 question variants through its lint and the rest through the prompt-leak and PII checks (adv-004); with the lint switched off (mutation M-01) the leading-question rate rises to 4/60 and the gate fails.
5. **A first mutation pass found two weaknesses in the suite** (M-10 survived; C-03 was vacuous when the agent never stopped); both are fixed ([MUTATION-EVIDENCE](MUTATION-EVIDENCE.md)).

Not found, and not claimed: any failure of a real model, any measure of judge quality, any statement about cost, latency or behaviour under a real provider.

## 13. Reproduction commands

All offline; the .NET 10 SDK is the only requirement. From the repository root:

| Number or claim | Command |
|---|---|
| corpus counts, spec version, corpus digest | `dotnet run --project src/ExitInterviewAgent.Eval -- validate` |
| constraints, metrics, per-class table, tokens, Layer 2 status | `dotnet run --project src/ExitInterviewAgent.Eval -- run --profile mock --deterministic --out report/` (then `report/report.md`, `report/report.json`) |
| gate result and its duration | `time dotnet run --project src/ExitInterviewAgent.Eval -- gate` |
| determinism | run the `run` command above twice into two directories and `cmp` the two `report.json` |
| rule-screen, analyser and classifier agreement; calibration state | `dotnet run --project src/ExitInterviewAgent.Eval -- calibrate --out report/` (then `report/calibration.md`) |
| 12 of 12 real-code mutations caught | `python3 scripts/mutate-agent.py` (clean tree; about a minute per mutation) |
| 161 tests, 23 of them in `MutationTests` | `dotnet test tests/ExitInterviewAgent.Eval.Tests` |
| the four findings | `dotnet test tests/ExitInterviewAgent.Eval.Tests --filter FullyQualifiedName~FindingsTests` |
| everything above in one go (without the mutation pass) | `scripts/run-evals.sh` |
| profiles and why a real-model profile is skipped | `dotnet run --project src/ExitInterviewAgent.Eval -- profiles` |

A real-model run is `dotnet run --project src/ExitInterviewAgent.Eval -- run --profile <name> --out report/` once a provider factory is registered and the profile's environment is set
([README](README.md#plugging-in-model-providers)); it has **not** been done.

## 14. Aggregate counter-metrics (T10)

The employer signals apply the same metric-ethics rules to a different artifact: the aggregate of what *former employees said*, not the quality of an interview. Summary of where each rule lives
([AGGREGATION](../privacy/AGGREGATION.md), [ADR-0054](../adr/0054-statistics-regularised-t-interval-and-reliability.md)):

| Rule ([metric-ethics](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md)) | In the signals |
|---|---|
| §1 anti-goals enforced by architecture | no composite, cross-topic average, ranking, best/worst or percentile: no field, column, query or route; employers listed alphabetically (tests) |
| §2 counter-metric in the same payload | every rating carries its **coverage** (the share of its population that rated the topic, banded) in the same object; the degenerate strategy it catches is a topic that looks good because most people skipped it |
| §3 no number without confidence | n, a 95% interval that is not falsely precise at small n, and a reliability label, in the same object; "a wide interval means early, not wrong" is part of the UI copy contract |
| §4 heuristics about people are report-only | nothing about a person is computed; the record has no affect field (ADR-0019 (d)) |
| §5 the unit is the artifact | the unit is an employer x topic cell of at least k ratings; no per-person view exists |

The interval's own coverage is measured, not assumed: the table is in AGGREGATION §5 and is produced by a test (`IntervalCoverageTests`). The ratings the cells average are produced by a model reading an interview ([OP-5](../OPEN-PROBLEMS.md#op-5-model-bias)); the interval does not contain that error.
