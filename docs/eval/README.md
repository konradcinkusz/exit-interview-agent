# Evaluation harness: a tour

Status vocabulary: [ADR-0017](../adr/0017-documentation-layout-and-claim-status.md). Decisions: [ADR-0037](../adr/0037-eval-harness-architecture.md) to
[0041](../adr/0041-mutation-proof-and-independent-rules.md). The contract is [SPEC.md](SPEC.md); the method and the numbers are
[METHODOLOGY.md](METHODOLOGY.md); the trace vocabulary is [TRACE-SCHEMA.md](TRACE-SCHEMA.md). Everything here runs **offline**: no model, no network,
no credential. **What the mock profile does and does not demonstrate** is stated in [SPEC §1](SPEC.md#1-what-is-evaluated-and-what-is-not) and at the top of
every report: a green mock run shows that the code-side protections hold and that the harness can see them fail; it says nothing about a real model.

## Run it

```bash
scripts/run-evals.sh                       # validate, gate, report (report/), calibration (report/)
dotnet run --project src/ExitInterviewAgent.Eval -- validate     # schema + corpus rules + spec citations
dotnet run --project src/ExitInterviewAgent.Eval -- gate         # the CI gate: constraints 100%, behaviours vs evals/baseline.json
dotnet run --project src/ExitInterviewAgent.Eval -- run --profile mock --out report/   # report.json and report.md
dotnet run --project src/ExitInterviewAgent.Eval -- calibrate    # rule screens and the reply analyser vs the hand labels; judge and model classifier skip without a credential
dotnet run --project src/ExitInterviewAgent.Eval -- profiles     # which model profiles can run here, and why not
```

Add `--deterministic` to `run` to omit the volatile latency block (two such runs are byte-identical; CI diffs them). `--prices file.yaml` computes cost from a
price table you supply (the repository ships none; without one, tokens are reported and cost is "not computed"). `--no-judge` skips Layer 2 explicitly.

## What is where

| Path | What |
|---|---|
| `docs/eval/SPEC.md` | the behaviour contract: constraints C-01..C-12, behaviours B-01..B-10, the operation table, metrics and counter-metrics, the gates |
| `evals/scenarios/<class>/*.yaml` | the scenarios, as data (happy, ambiguity, hostile, adversarial, degradation, consent). A scenario names a persona and writes only its delta: seeds, faults at the model seam, a canary, a `probe_reply`, what it expects (including at least one absence for adversarial and consent scenarios) |
| `evals/schema/scenario.schema.json` | the strict schema (validated on every build) |
| `evals/labels/vagueness.yaml`, `evals/labels/judge.yaml` | hand labels with written rationales (author-labelled, non-human; see below) |
| `evals/rubrics/judge.yaml`, `judge-prompt.md` | the judge's criteria with an anchor per level, the calibration thresholds, and the prompt (both SHA-256 pinned in every report) |
| `evals/baseline.json` | the recorded state behaviours are compared with (pinned to spec version, corpus digest, harness version, profile) |
| `evals/profiles.yaml` | the real-model profiles (declared; each skips unless configured) |
| `src/ExitInterviewAgent.Eval` | the library and console tool: `Scenarios/` (model, loader, corpus rules), `Execution/` (runner, trace capture, fault injection, profiles), `Layer1/` (graders and the independent rule sets), `Layer2/` (judge, calibration, classifier experiment), `Reporting/` (metrics, gate, baseline, conformance report), `Cli/` |
| `tests/ExitInterviewAgent.Eval.Tests` | 150 tests, including the broken-variant mutation tests |
| `docs/eval/MUTATION-EVIDENCE.md` | the real-code mutation pass: what was weakened, what the gate said |
| `scripts/run-evals.sh`, `scripts/mutate-agent.py`, `scripts/check-baseline-justification.sh` | the commands above, the mutation pass, and the regeneration rule |

## How a scenario becomes a verdict

1. `ScenarioLoader` reads the YAML, converts it to JSON by YAML's core-schema rules, validates it against the schema, and maps it to the typed model.
2. `ScenarioRunner` runs the persona against the agent through `PersonaSession` with the chosen profile's `IChatClient` (wrapped by the fault decorator if the
   scenario declares faults), under an `ActivityListener` that keeps one trace and one log capture per run. Deterministic with the seed.
3. `Layer1Grader` evaluates the twelve constraints on **every** run, the scenario's own expectations (`L1.X.*`), and measures the behaviours (k of n). A message
   never contains interview text.
4. `Gate` compares with the baseline; `ConformanceReport` writes JSON and Markdown: per constraint, per metric (with the counter-metric, `n` and the Wilson
   interval), per scenario class, one column per profile, no composite score and no ranking.

## Add a scenario

1. Pick the class. Write `why` first; if you cannot say what breaks when it stops passing, it is not a scenario.
2. Copy the nearest file under `evals/scenarios/<class>/`, name it `<prefix>-<nnn>-<slug>.yaml` (the prefix and class must agree).
3. Cite the spec ids it proves in `spec:` **and** add its short id (`adv-009`) to the matching rows of SPEC.md; `validate` checks both directions.
4. `validate`, then `gate`. A new scenario is "not in the baseline", so the gate fails until you regenerate it with a justification (below).

## Regenerate the baseline (the rule)

```bash
dotnet run --project src/ExitInterviewAgent.Eval -- baseline --justification "why the measured behaviour changed"
```

It is refused without `--justification`, and it refuses to record a run with a harness error or a constraint violation. **The pull request description must
repeat the printed line, `Baseline justification: ...`**; CI (`scripts/check-baseline-justification.sh`) fails when `evals/baseline.json` changes and the line is
missing or different. Tolerances are the one part edited by hand (`tolerances` in the baseline file; `default` is 0 for the deterministic mock).

## Plugging in model providers

The harness never constructs a provider. `evals/profiles.yaml` declares profiles by provider name and by environment variable (the model id and the key are
never committed). A provider project registers one factory per name:

```csharp
// ExitInterviewAgent.Eval/Program.cs, before EvalCli.RunAsync (T6 adds this line; nothing else changes):
ProviderFactories.Register("anthropic", settings => /* IChatClient from settings.Model, settings.Env["ANTHROPIC_API_KEY"] */);
ProviderFactories.Register("openai-compatible", settings => /* ... settings.Endpoint ... */);
ProviderFactories.Register("ollama", settings => /* ... */);
```

Without the factory a profile reports `skipped:no-provider`; without its environment, `skipped:no-credential` (naming the variables, never their values). Neither is
ever a pass. Run it with `run --profile anthropic --out report/` and the report has one column per profile; real-model runs are exploratory and nightly and
never block a pull request. The Layer 2 judge and the model-assisted classifier use the `judge:` entry in the same file.

## What is weak, said plainly

- **The mock cannot be fooled.** The *compromised-model* scenarios simulate a model that was, to test that the code-side protections hold when it has failed; they
  do not measure how a real model behaves.
- **The hand labels are author-labelled and non-human.** The AI session that wrote the harness wrote the rubric and the labels, so agreement with them is partly
  agreement with the author's own reading. They are a rehearsal of the calibration protocol, they count for nothing towards the judge gate (tested), and a
  human relabelling is the way to change that ([OP-11](../OPEN-PROBLEMS.md#op-11-judge-calibration-labels)).
- **Layer 2 has not scored anything.** No judge credential exists in this environment; every report says `skipped:no-credential`.
- **Intervals are over scenarios and seeds**, not over model sampling, because the mock is deterministic.
