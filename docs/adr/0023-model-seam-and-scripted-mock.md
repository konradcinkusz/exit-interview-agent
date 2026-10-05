# 0023. Model seam: `Microsoft.Extensions.AI.Abstractions`, and a scripted mock that is not a baseline

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §4 (model access behind `IChatClient`; verify the package and versions on NuGet and justify), `ai-evals` (an eval needs a deterministic, offline seam), P13

## Context

Model access must sit behind `Microsoft.Extensions.AI`'s `IChatClient` (brief §4). The brief also asks for a deterministic mock for offline demos and CI, and defers real providers (Anthropic key, OpenAI-compatible, Ollama) to T6.

## Decision

- **Package: `Microsoft.Extensions.AI.Abstractions` 10.10.1**, referenced only by the Agent project. Checked on 2026-10-05: 10.10.1 is the latest stable version in the NuGet flat-container index (the same index lists `Microsoft.Extensions.AI` at 10.10.0 as latest), the licence expression is MIT, and the `net10.0` dependency group is empty (so it adds no transitive package). It supplies `IChatClient`, `ChatMessage`, `ChatOptions`, `ChatResponse`, `UsageDetails` and `DelegatingChatClient`, which is all the roles and the metering wrapper need.
  `Microsoft.Extensions.Logging.Abstractions` 10.0.12 is added for `ILogger` (versions only in `Directory.Packages.props`).
- **Not adopted here: the `Microsoft.Extensions.AI` package** (middleware such as function invocation, caching and its own telemetry). The agent needs none of it, it owns its tracing so that the metadata-only rule is enforced by construction ([ADR-0025](0025-trace-schema-metadata-only.md)), and which middleware a provider project wants is T6's decision. Alternatives considered: a project-specific `IModel` interface (reinvents the abstraction and cuts the agent off from every provider package that already implements `IChatClient`); an orchestration framework (a loop owned by a framework contradicts [ADR-0022](0022-interview-agent-core.md)).
- **`MeteredChatClient`** wraps any `IChatClient`: one `chat` span per call with the GenAI usage attributes, the budget meter, no message text in the span, and provider exceptions rethrown as `ModelCallFailedException` carrying the exception **type** only (provider messages can echo the prompt). Streaming is refused so that every call is metered.
- **`ScriptedChatClient`** is the deterministic offline model: it reads the role from the first line of the system prompt, returns the protocol's seed wording for the interviewer and prober, and extracts with sentence splitting, a concreteness preference and word-count ratings. No network, no credentials, no randomness.
- **It is a test and demo seam, not a quality baseline**, and every surface says so (class comment, `interview-agent.md`, the CLI banner, README). It cannot be talked into anything because it understands nothing, so a clean run with it proves the plumbing and nothing about a real model's neutrality, injection resistance or extraction quality. No model-quality number may be derived from it.

## Consequences

- T6 adds providers as separate `IChatClient` implementations without touching the Agent project; the same agent, personas and invariants then run against them (T7 measures).
- The Agent project makes no network call and references no HTTP assembly (architecture test): a provider project that does is T6's.
- Trigger to revisit: a provider package that needs `Microsoft.Extensions.AI` middleware, or a breaking change in the abstractions.
