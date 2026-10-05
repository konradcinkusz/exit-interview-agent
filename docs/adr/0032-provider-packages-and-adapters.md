# 0032. Provider packages: the official Anthropic SDK, Microsoft's OpenAI adapter, a thin Ollama client

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §4 (model access behind `IChatClient`; verify the package choice on NuGet and justify it), P10 (extensibility through interface plus registration), P8 (optional integrations degrade and are visible), [ADR-0023](0023-model-seam-and-scripted-mock.md) (T6 adds providers as separate `IChatClient` implementations without touching the agent)

## Context

T6 needs `IChatClient` implementations for the Anthropic API (user's own key), any OpenAI-compatible endpoint, and Ollama. The brief says to verify packages on NuGet and to fall back to a thin `HttpClient` adapter only where no acceptable package exists. Checked on 2026-10-05 with `curl` against the NuGet flat-container and registration APIs (the numbers below are those responses, not recollection):

| Package | Version | Publisher | Licence | Published | Notes |
|---|---|---|---|---|---|
| `Anthropic` | 12.53.0 | Anthropic | MIT (SPDX expression) | 2026-09-30 | "The official .NET library for the Anthropic API"; repository `anthropics/anthropic-sdk-csharp`; 145 published versions. Depends on `Microsoft.Extensions.AI.Abstractions` (>= 10.5.1), `System.Net.ServerSentEvents`, `System.Text.Json`. Ships `AnthropicClient.AsIChatClient(model, maxTokens)` (read from the package's XML documentation). Anthropic's own SDK page lists the C# SDK as ".NET Standard 2.0+, IChatClient integration" (platform.claude.com/docs/en/api/client-sdks, read 2026-10-05). |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | Microsoft | MIT | 2026-09-25 | "Implementation of generative AI abstractions for OpenAI-compatible endpoints"; depends on `OpenAI` 2.14.0 and the same abstractions version as [ADR-0023](0023-model-seam-and-scripted-mock.md) (10.10.1). |
| `OpenAI` | 2.14.0 (transitive) | OpenAI | MIT | 2026-09-15 | "The official .NET library for the OpenAI service API". Not referenced directly: it comes with the Microsoft adapter. |
| `OllamaSharp` | 5.5.0 | community authors | licence given as a file in the package (upstream is not read here) | n/a | Depends on the full `Microsoft.Extensions.AI` 10.8.0 and `Microsoft.Extensions.AI.Abstractions` 10.8.0. |
| `Microsoft.Extensions.AI.Ollama` | 9.7.0-preview | Microsoft | n/a | 2025-07-08 | **Deprecated** on NuGet ("No further updates, features, or fixes are planned"; recommends `OllamaSharp`). No 10.x release. |

Two behaviours were found by running the SDKs against a fake handler (`/tmp` spike, reproduced by the tests named below), and they shape the design:

1. **The Anthropic SDK reads `ANTHROPIC_AUTH_TOKEN` from the environment and sends it as `Authorization: Bearer` next to the explicit `x-api-key`**, even when a key is given. Setting `AuthToken = ""` still sent an empty `Authorization: Bearer`. A user with a bearer token for some other purpose in that variable would have it sent to whatever base URL is configured. (Test: `Anthropic_sends_the_key_only_in_x_api_key_and_never_forwards_an_environment_bearer_token`.)
2. **SDK exceptions carry the response body** (`Anthropic5xxException`: `Status Code: InternalServerError` followed by the body; `ClientResultException` likewise). Providers sometimes echo the request in an error, so a prompt can reach an exception message. ([ADR-0025](0025-trace-schema-metadata-only.md)'s rule needs a wrapper that never forwards a message.)

## Decision

- **Anthropic: the official `Anthropic` 12.53.0 package** and its `AsIChatClient` adapter. An official, MIT-licensed, actively published SDK that already implements the abstraction exists, so the thin-adapter fallback is not needed. Constructed with an explicit `ApiKey` (so no environment or profile credential is resolved), `MaxRetries = 0`, and our own `HttpClient`.
- **OpenAI-compatible: `Microsoft.Extensions.AI.OpenAI` 10.10.1** (with `OpenAI` 2.14.0), constructed with `Endpoint` = the user's base URL, our `HttpClient` as transport, and `RetryPolicy = new ClientRetryPolicy(0)`. Chosen over a hand-written client because the request/response mapping is non-trivial and Microsoft maintains it; OpenAI-compatible gateways are served by the same code.
- **Ollama: a ~100-line `HttpClient` client for `POST /api/chat`** (non-streaming) in `OllamaChatClient`. Reasons: the Microsoft package is deprecated; `OllamaSharp` is the recommended alternative but brings a second tree (`Microsoft.Extensions.AI` 10.8.0, an older line than the abstractions we pin) for a single endpoint and its licence is a file whose upstream text was not read here; a hand-written client sends exactly the fields we choose (including `num_ctx`, because Ollama's own default context can silently truncate a long transcript) and is trivially covered by the canary and request-shape tests. Cost: we maintain it. **Trigger to revisit:** a need for tools, thinking-mode handling or model management, or a stable Microsoft package, then adopt `OllamaSharp` behind the same class.
- **All SDK retries are off; the transport policy is ours** ([ADR-0034](0034-resilience-budget-and-failure-semantics.md)), and **every SDK exception is replaced by `ProviderException`** (type name and status only).
- Versions live in `Directory.Packages.props` only (`Anthropic`, `Microsoft.Extensions.AI.OpenAI`; `OpenAI` and `System.ClientModel` are transitive). NuGet audit is on (`NuGetAuditMode=all`): restore fails on a known vulnerability, and it did not.
- **New providers** are an `IChatClient` plus one `case` in `ProviderChatClients.Create` and a row in `ProviderCatalog` (P10); the provider's own SDK exceptions must never escape (the wrapper handles that for any inner client).
- The Agent project is untouched except for the failure-code seam ([ADR-0034](0034-resilience-budget-and-failure-semantics.md)): it still references no HTTP assembly and no SDK (architecture tests).

## Consequences

- Three real providers behind one wrapper; `ProviderChatClients.Create(settings)` is the entry point T7 builds a real-model profile with.
- The CLI binary grows (two SDKs). It is still published without trimming (reflection-based JSON, [ADR-0026](0026-cli-project-and-ci-artifacts.md)).
- The SDKs' own telemetry sources are never exported (only the two project sources are registered, [ADR-0035](0035-provider-telemetry-and-export.md)).
- **Not verified here:** any call to a real endpoint (no key, restricted network). The adapters are verified against fakes that reproduce the documented wire shapes; whether a particular OpenAI-compatible gateway accepts `max_completion_tokens` (what the SDK sends) is unknown per gateway.
