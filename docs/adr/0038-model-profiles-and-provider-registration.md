# 0038. Model profiles and the provider registration point

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §4 and §7 (conformance suite: same interview, different models, comparable report), `ai-evals` §6, P8 (optional dependencies degrade and are visible)

## Context

The conformance report must run one scenario corpus against different models. T6 adds real providers (Anthropic key, OpenAI-compatible, Ollama) behind `IChatClient` in a separate project, in parallel with this work, and T7 must not create providers or touch the CLI.

## Decision

- A **profile** is a named factory of `IChatClient` (`ModelProfile`). `mock` (the scripted model) is built in and is the default and the CI profile.
- Real-model profiles are **declared in configuration**, `evals/profiles.yaml`: a name, a provider name, `model_env` (the environment variable holding the model id to test), an optional `endpoint_env`, and `requires_env` (the variables that must be set). **No model identifier and no secret is committed**; the id to test is pinned by whoever runs the nightly and is recorded in the report together with the id the service says actually answered.
- **The registration point is `ProviderFactories.Register(provider, Func<ProviderSettings, IChatClient>)`.** The harness resolves a profile's provider name through it and never constructs a provider itself (architecture test: the Eval assembly references no provider package). T6's project registers `anthropic`, `openai-compatible` and `ollama`; the tool's `Program.cs` calls that registration (one line, commented in place). `ProviderSettings` carries the model id, the endpoint and the resolved environment values; none is ever logged or reported.
- **Two kinds of skip, reported separately and never as a pass:** `skipped:no-credential` (a required environment variable is unset; the report names the variable, never a value) and `skipped:no-provider` (no factory is registered for the provider name). `skipped:unimplemented` is a scenario marked `skip`. A profile that cannot run is listed in the report with its reason.
- The same mechanism configures the **judge** (`judge:` in the same file): one pinned model, separate from the models under test.

## Consequences

- If T6 has merged, wiring is one registration call in `Program.cs` plus the environment; if it has not, nothing in this repository changes when it does.
- Real-model profiles are exploratory and nightly: they never block a PR (METHODOLOGY §7), because a sampling model gives a distribution, not a fact, and the interval over `n = 5` runs per scenario is the honest summary (not implemented here; the mock is deterministic with `n = 1`).
- Open: the environment variable names are this repository's proposal; T6 may rename them in `evals/profiles.yaml`.
