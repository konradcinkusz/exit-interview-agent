# 0037. The evaluation harness: one project, scenarios as data, an in-process runner, universal constraints

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): P13 (test at the layer that has the logic), P15 (the trace is the contract), `ai-evals` §2 to §4, `metric-ethics` §1 and §5, `konradcinkusz/agent-eval-bench` (ported, not copied)

## Context

The interview agent is probabilistic wherever a model sits behind it, so "did it get worse" cannot be answered by unit tests alone. The brief (§7) asks for a harness that scores the quality of the *interview* from OpenTelemetry traces, reusing the approach of `agent-eval-bench`: spec first, scenarios as YAML validated by JSON Schema, an in-process runner, one captured trace per scenario, deterministic Layer 1 assertions, a calibrated Layer 2 judge, CI gates. T4 left a stable seam for this: `PersonaSession.RunAsync(persona, seed, IChatClient?, decorate, logger)`, the `ExitInterviewAgent.Agent` activity source with a metadata-only vocabulary, and `InterviewInvariants`.

## Decision

- **One project, `src/ExitInterviewAgent.Eval`, library and console tool** (`exit-interview-eval`; tests in `tests/ExitInterviewAgent.Eval.Tests`). It references `Agent`, `Personas`, `Records` and `Privacy` and nothing else (architecture test): no employer entity, no Signals module, no CLI, no provider, no `HttpClient`. Anti-goals of METHODOLOGY §1 are therefore absences, not prose.
- **Spec first.** `docs/eval/SPEC.md` (version 1.0.0) precedes the corpus: twelve hard constraints (C-01..C-12), ten behaviours (B-01..B-10), a **normative operation table** instead of a naming convention (the agent has no write-classified operation, and says so), the metrics with counter-metrics and Wilson intervals. A validator checks the spec's citations in both directions on every build.
- **Scenarios are data**: `evals/scenarios/<class>/<id>.yaml`, validated by `evals/schema/scenario.schema.json` (strict, `additionalProperties: false`) and by corpus rules the schema cannot express (no empty class, ids agree with files, the two-assertion rule, resolvable citations, every persona reply hand-labelled, well-formed faults). Six classes: happy, ambiguity, hostile, adversarial, degradation, consent. The "named base world plus a per-scenario delta" of `ai-evals` §3 is the persona catalogue and the protocol that ship in the box plus a scenario's seeds, faults and canary; no new persona was needed.
- **YAML** is read with `YamlDotNet` 18.1.0 (MIT; version in `Directory.Packages.props`), converted to JSON by YAML core-schema rules and validated with the JSON-Schema library the repository already uses (ADR-0008). Aliases and tags are refused.
- **In-process runner, one trace per run.** `ScenarioRunner` calls `PersonaSession`; a per-run `ActivityListener` keeps only activities that share the run's own root trace id, so parallel tests cannot contaminate each other. Results are serialised for the graders. A run is a pure function of (scenario, seed, profile): seed-derived persona, simulated clock and interview id; faults counted by call ordinal.
- **The twelve constraints are evaluated on every run of every scenario**, whatever its gate (ported rule: a constraint violated in a happy-path scenario is still a violation). Scenario-specific expectations are separate assertions (`L1.X.*`). Failure messages carry counts and ids, never interview text (tested).
- **Compromised-model scenarios.** The mock cannot be fooled, so injection resistance of the *model* is unmeasurable offline. Faults are injected at the `IChatClient` seam (a decorator below the agent's metered client): timeout, 5xx, empty answer, malformed extractor JSON, missing or inflated usage, and a **compromised** model that does what an injection asked (leading, leaking or name-echoing questions; an extractor that adds fields, invents quotes or carries PII). They test that the code-side protections hold when the model has failed, which is what the mock *can* show.
- **No composite score, no ranking, no per-person view.** The report schema has no total; each metric row carries its counter-metric; where intervals overlap the report says "not distinguishable" (tested).

## Consequences

- CI runs the whole harness offline in seconds (about 3 s of tests, about 1 s for the 44 runs); no model, no network.
- A scenario is cheap to add (one YAML file, one citation in the spec) and expensive to weaken (the validator, the digest and the baseline all notice).
- What is **not** demonstrated: anything about a real model. That is the job of the profiles ([ADR-0038](0038-model-profiles-and-provider-registration.md)) and of a keyed nightly, which has not run.
- Ported from `agent-eval-bench` and changed: the domain and its tools (here the agent has no write tool in interview mode); the "confirmation before write" constraint becomes the operation table plus C-03 (no record after withdrawal); Layer 1 grades a trace *and* a record and a transcript, because the record is the product here; the corpus digest also covers the label sets and the persona and protocol data; faults and a compromised-model simulation replace tool-result fixtures. No code was copied.
