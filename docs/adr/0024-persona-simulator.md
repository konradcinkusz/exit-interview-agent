# 0024. Persona simulator: a separate project, data files validated by a schema

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §7 (personas), `ai-evals` §3 (scenarios are data), `demo-data-and-seeding` (synthetic, deterministic, rebuilt every run), brief §2 (no real people)

## Context

The eval harness (T7) reuses the personas as scenario input, and the agent's own tests need them. Personas are interviewee simulators: talkative, terse, hostile, vague, names a manager, prompt-injection attempt, withdraws consent, contradictory. The question was where they live and in what shape.

## Decision

- **A separate project, `ExitInterviewAgent.Personas`**, not a namespace of the Agent. The Agent is shipped logic; personas are test data and simulation. A separate project keeps the Agent from depending on them (an architecture test asserts it), lets T7 reference personas without the CLI, and gives the data a place to grow. It depends on `Agent` only for the `IInterviewee` seam and the turn kinds, and on `Records` for the context bands.
- **Personas are data**: JSON files embedded in the assembly, validated against `schemas/persona.v1.schema.json` (closed objects, enumerated behaviours, outcomes and bands) at load; an invalid file fails every test. JSON, not YAML, because the repository already validates JSON against JSON Schema with one package and no new dependency.
- **A persona is a set of response slots with alternatives** (consent, one per topic, probes, clarifications, redirects, fallback), an `expected` block (outcome, end reason, submittable, minimum probes, redirects, clarifications) and optional `plantedLiterals` and `injectionTargets`. Behaviour is data, not code: "withdraws consent" is a persona whose growth answer is a withdrawal.
- **Seeded and platform-stable.** The reply to the n-th turn of a kind is a pure function of persona, seed, kind, topic and n: SplitMix64 over the seed xor an FNV-1a hash of persona and slot, with repeated asks rotating through the alternatives. Fixed algorithms, not `System.Random`, so a seed means the same thing on every runtime. A golden test pins it and the values were cross-checked with an independent implementation.
- **The simulator never reads the interviewer's words**, only the turn kind and topic, so nothing the interviewer says can steer it.
- **Synthetic only.** The employer is `widgetron-ltd`, obviously fictional; names and addresses are invented and use reserved example domains; the catalog test rejects an address that is neither planted nor under `.example`.
- **Stable API for T7**: `PersonaCatalog`, `PersonaDefinition`, `PersonaInterviewee`, `PersonaSession.RunAsync(persona, seed, model?, decorate?, logger?)`. Changing a persona's text is a behaviour change for every consumer and is reviewed as one.

## Consequences

- T7 can add scenarios by adding files, and can run the same personas against real models through `PersonaSession`.
- Personas' text is chosen against the current detector and analyser (for example it avoids capitalised mid-sentence words that the fail-closed detector would mask); that coupling is a maintenance cost, and a persona that stops behaving as expected fails its `expected` block loudly.
- Not covered: personas in other languages, free-form generative personas (a model-played interviewee) and persona difficulty calibration.
