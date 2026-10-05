# Model providers (T6)

Status: **Implemented** (adapters verified against fakes; **no live call was made**, see [what was not verified](#what-was-not-verified)).
Decisions: [ADR-0032](../adr/0032-provider-packages-and-adapters.md) (packages), [0033](../adr/0033-provider-configuration-credentials-and-disclosure.md)
(configuration, refusals, disclosure), [0034](../adr/0034-resilience-budget-and-failure-semantics.md) (resilience, budget, failure semantics),
[0035](../adr/0035-provider-telemetry-and-export.md) (telemetry), [0036](../adr/0036-cli-interview-and-providers-commands.md) (CLI).
Companions: [interview agent](interview-agent.md), [trace schema](../eval/TRACE-SCHEMA.md#provider-layer-t6), [legal considerations](../legal/CONSIDERATIONS.md#1-model-provider-terms-what-we-may-and-may-not-support).

**What this does not do: it measures nothing about the quality of any real model.** It makes real models usable. Whether a real model
asks neutral questions, resists injection or extracts faithfully is the evaluation harness's question (T7) and is **not measured**.

## Provider matrix

| `--provider` | Backend | Package | Key | Default endpoint | Notes |
|---|---|---|---|---|---|
| `anthropic` | Anthropic API, your own API key | official `Anthropic` 12.53.0 (`AsIChatClient`) | `ANTHROPIC_API_KEY` (required) | `https://api.anthropic.com` | base URL without `/v1`; header `x-api-key` only |
| `openai-compatible` (alias `openai`) | any OpenAI chat-completions endpoint: a hosted service or a local gateway | `Microsoft.Extensions.AI.OpenAI` 10.10.1 over `OpenAI` 2.14.0 | `OPENAI_API_KEY` (required for `api.openai.com`, optional for a gateway you point at) | `https://api.openai.com/v1` | base URL includes `/v1`; `Authorization: Bearer` only; the SDK sends `max_completion_tokens`, which not every gateway accepts (unverified per gateway) |
| `ollama` | a local (or LAN) Ollama server, native `/api/chat` | hand-written client (ADR-0032) | none (optional bearer key via `--api-key-env` for a proxied server) | `http://localhost:11434` | `--num-ctx` sets the context window: Ollama's own default can silently truncate a long transcript |
| `mock` | the scripted offline model | `ScriptedChatClient` (Agent) | none | none | test seam, **not a quality baseline**; no network |

Not offered: **Claude subscription (Free/Pro/Max) credentials** and **GitHub Copilot** ([why](#what-is-refused-and-why)).

## Configuration reference

Precedence, highest first: **flag, environment variable, config file, default.** Provider and model have no default.

| Setting | Flag | Environment | Config file key | Default |
|---|---|---|---|---|
| provider | `--provider` | `EXIT_INTERVIEW_PROVIDER` | `provider` | none |
| model | `--model` | `EXIT_INTERVIEW_MODEL` | `model` | none (no model id is hardcoded) |
| base URL | `--base-url` | `EXIT_INTERVIEW_BASE_URL`, then `OPENAI_BASE_URL` / `OLLAMA_HOST` for those providers | `baseUrl` | per provider |
| key variable name | `--api-key-env NAME` | `EXIT_INTERVIEW_API_KEY_ENV` | `apiKeyEnv` | `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, none |
| the key itself | **no flag** | the variable named above | **not allowed in the file** | none |
| per-attempt timeout | `--timeout-seconds` | `EXIT_INTERVIEW_TIMEOUT_SECONDS` | `timeoutSeconds` | 120 |
| retries | `--max-retries` | `EXIT_INTERVIEW_MAX_RETRIES` | `maxRetries` | 3 |
| token budget (graceful; hard ceiling is 2x) | `--max-tokens` | `EXIT_INTERVIEW_MAX_TOKENS` | `maxTokens` | the protocol's 60000 |
| prices per million tokens (optional) | `--price-in`, `--price-out` | `EXIT_INTERVIEW_PRICE_INPUT_PER_MTOK`, `_OUTPUT_PER_MTOK` | `prices.inputPerMillionTokens`, `.outputPerMillionTokens`, `.currency` | none: cost is "not computed" |
| cost ceiling (needs prices) | `--max-cost` | `EXIT_INTERVIEW_MAX_COST` | `maxCost` | none |
| Ollama context window | `--num-ctx` | `EXIT_INTERVIEW_NUM_CTX` | `numCtx` | Ollama's own |
| config file | `--config <file>` | `EXIT_INTERVIEW_CONFIG` | n/a | `$XDG_CONFIG_HOME/exit-interview/config.json`, else `~/.config/exit-interview/config.json` (Windows: `%APPDATA%`); directory override `EXIT_INTERVIEW_CONFIG_DIR` |

Telemetry (off unless asked): `OTEL_TRACES_EXPORTER`, `OTEL_METRICS_EXPORTER` (`otlp`, `console`, `none`), `OTEL_EXPORTER_OTLP_ENDPOINT` and the other standard OTLP variables, `OTEL_SERVICE_NAME`, `OTEL_SDK_DISABLED`.

A config file (no secrets in it):

```json
{
  "provider": "anthropic",
  "model": "<a model id from your provider>",
  "apiKeyEnv": "ANTHROPIC_API_KEY",
  "maxTokens": 60000,
  "prices": { "inputPerMillionTokens": 0, "outputPerMillionTokens": 0, "currency": "USD" }
}
```

(The zeros are placeholders: this project hardcodes no price. Put your provider's current prices there, or leave `prices` out.)

## Trust boundary and what the provider sees

```
 you (terminal) --> exit-interview (this machine) --HTTPS--> provider you chose (Anthropic / your endpoint)
                    transcript in memory                      sees: every interviewer question and every answer (masked
                    record built locally                      where the PII guard masks), the protocol prompts, the model name
```

- **The provider sees the whole transcript**, in the extractor call all of it at once. That is inherent to using a hosted model; the CLI says so and asks
  for confirmation before the first external call ([ADR-0033](../adr/0033-provider-configuration-credentials-and-disclosure.md)). Retention, training and
  residency are the provider's terms for your account; this project has not verified them (CONSIDERATIONS rows 2c, 3).
- **The provider does not see**: your API key's value in any prompt, anything from other interviews, a record ID or an account (none exists in the CLI).
- **Replies are masked on arrival** by the PII guard before they enter the transcript that is sent on, so a name the guard recognises does not reach the provider
  in later calls (the guard is heuristic: [pii-detector](../privacy/pii-detector.md)). The text you typed is in the process's memory until the interview ends.
- **A local model on this machine** sends nothing out through this program. A "local" OpenAI-compatible gateway may itself forward to a remote provider; this
  program cannot see that. A remote Ollama (a LAN address) is treated as external.
- **This project's servers see nothing**: there is no submission in this version (T11 adds record-only submission).
- **Nothing is written** unless you ask: `--out` for the record, `--save-transcript` for the transcript. The only persisted state beside those is the optional
  confirmation preference file.

Threat-model rows: T-20 (disclosure to the provider: accepted, disclosed), T-15 (no content in logs, traces, metrics), T-03 (injection: code-side defences are T4's; real
models unmeasured).

## What is refused, and why

- **Claude subscription credentials.** Anthropic's documentation (read 2026-10-05) says third-party developers may not offer Claude.ai login or route requests
  through Free, Pro or Max plan credentials, and names `CLAUDE_CODE_OAUTH_TOKEN` as the subscription OAuth token variable. Naming it (or
  `CLAUDE_CODE_OAUTH_REFRESH_TOKEN`) as a key source, or asking for a `claude-subscription`-style provider, is refused with a pointer to the README. **Limit:** no
  recognisable token format is documented, so a subscription token pasted into `ANTHROPIC_API_KEY` is not recognised and is left to the API to reject. Nothing
  reads or forwards a subscription token; the Anthropic SDK's habit of forwarding `ANTHROPIC_AUTH_TOKEN` is removed at the transport.
- **GitHub Copilot.** Unsupported because its terms for this use **could not be verified** (2026-10-05), not because they were found to prohibit it. No adapter, no
  provider id; a base URL on `githubcopilot.com` is refused (hostname from general knowledge, unverified).

## Behaviour on failure

| Event | What happens |
|---|---|
| 429 / 5xx / timeout / connection error | retried up to the limit with jittered backoff; `Retry-After` honoured up to 30 s, longer ends the retries |
| 401 / 403 / other 4xx / a redirect | not retried; **fatal**: the interview stops at once, nothing kept, exit 5 with a hint |
| failure after the retries on one call | that turn uses the protocol's own wording (T4); three such calls in a row are fatal |
| hard budget reached | no further call starts; fatal; exit 5 |
| Ctrl-C / end of input | the interviewee leaves: transcript discarded, nothing written |

Every failure surfaces as `ProviderException` (kind, status, attempts) whose message contains no prompt, response, header, URL or key; the interview sees only its
controlled code (`IModelFailure`).

## How to add a provider

A provider is an `IChatClient` plus three small edits (P10, ADR-0032):

1. Implement or obtain an `IChatClient` that takes the `HttpClient` from `ProviderHttp.CreateClient(...)` (so the transport policy, the one-credential-header rule and no-redirects apply) and never lets an exception message escape.
2. Add a `ProviderKind` member, a `ProviderCatalog` row and a `case` in `ProviderChatClients.Create` (wrapping it in `ProviderChatClient`); add its label in `ProviderTelemetry.ProviderLabel`.
3. Run the same tests over it: put the kind in `Settings.RealKinds` and teach `FakeBackend` its wire format; the adapter, canary and telemetry theories then cover it. Update this document, the README table and the legal table (a provider whose terms were not read is "Unverified", never "supported by terms").

## For the eval harness (T7)

```csharp
var resolved = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "<model>" }, Environment.GetEnvironmentVariable);
using var client = ProviderChatClients.Create(resolved.Settings);          // IChatClient: metered, budgeted, instrumented
var result = await PersonaSession.RunAsync(persona, seed, model: client);   // same personas, same invariants
var usage = client.Budget.Snapshot();                                       // calls, tokens in/out, latency, cost if prices were given
```

`ProviderSettings` can also be built as a literal. `ProviderRuntime` replaces the transport (a fake handler), the clock and the jitter source. A fatal failure surfaces
as `ModelCallFailedException` (`IsFatal`, `Code`) out of `RunAsync`; a harness should record it as an infrastructure failure of the run, not as an agent result.

## What was not verified

- **No live call to any provider was made** (no key, restricted network). Live smoke tests exist (`Category=Live`: `ANTHROPIC_API_KEY` or `OPENAI_API_KEY`, plus
  `EXIT_INTERVIEW_LIVE_MODEL`); they skip without the variables and never run in CI. Request shapes were checked against fakes that reproduce the SDKs' real wire output
  (captured by running the SDKs against a recording handler), not against the providers' servers.
- Ollama's `/api/chat` shape is from general knowledge of its API, not from a read of Ollama's documentation in this environment.
- The defaults (timeouts, retries, backoff, the hard ceiling) are starting values, not measured.
- Whether a given OpenAI-compatible gateway accepts what the SDK sends is unknown per gateway.
