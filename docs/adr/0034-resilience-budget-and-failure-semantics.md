# 0034. Resilience, the hard budget, and what a provider failure does to an interview

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): P8 (degrade, visibly), [ADR-0022](0022-interview-agent-core.md) / [ADR-0023](0023-model-seam-and-scripted-mock.md) (budgets and the metered client), [ADR-0025](0025-trace-schema-metadata-only.md) (no content in exceptions), `security-review` §7 (errors), brief §6/§9 (no hardcoded unverifiable claims)

## Context

Real providers time out, rate-limit and fail, and their exceptions can echo the prompt. T4 left two things open: the agent swallowed every model failure and carried on with the protocol's own wording (right for a flaky provider, wrong for a wrong API key, where it would run a whole interview and then fail to extract), and its budgets were graceful only.

## Decision

- **One transport policy below the SDKs** (`ResilientHttpHandler`, in the `HttpClient` each SDK is given): a timeout per attempt (default 120 s); up to 3 retries (configurable 0-10) on 408, 429, 5xx (529 included), per-attempt timeouts and connection errors, never on other 4xx; **equal-jitter exponential backoff** (half of `min(cap, base * 2^n)` fixed, half random; base 1 s, cap 30 s); `Retry-After` honoured, and a value larger than the cap **ends the retries** rather than stalling an interview (Anthropic documents that a spend-cap 429 has no `retry-after` and keeps failing, platform.claude.com/docs/en/api/errors, read 2026-10-05); the request body is buffered and each attempt is a fresh request; cancellation is checked before each attempt and during every wait. The clock is a `TimeProvider`, so tests drive backoff exactly on a fake clock (delays asserted to the tick).
- **One credential header, no other**: the handler removes every credential-bearing header (`Authorization`, `x-api-key`, `api-key`, ...) and puts back only the one the provider's scheme allows. This is what neutralises the SDK forwarding `ANTHROPIC_AUTH_TOKEN` ([ADR-0032](0032-provider-packages-and-adapters.md)).
- **Failures become `ProviderException`**, built from a kind (authentication, rate limited, server error, bad request, timeout, network, invalid response, budget exceeded, unavailable), an HTTP status and an attempt count. The response body is **never read** on an error; headers, URLs, keys and prompts cannot reach a message. Anything not an HTTP failure (a parse error inside an SDK) is reduced to its type name.
- **A whole-call deadline** (default: every permitted attempt and wait fits; overridable), separate from the per-attempt timeout.
- **Hard per-interview budget** (`InterviewBudget`), checked *before* each call: tokens (twice the protocol's graceful `MaxEstimatedTokens`, because extraction resends the whole transcript and is not counted by the graceful meter), calls (the protocol's `MaxModelCalls` + 4 for the extractor's attempts and a margin), and cost (optional). `--max-tokens` lowers the graceful token budget through `InterviewProtocol.WithLimits` and the hard ceiling follows. "Hard" means no further call starts; the call in flight completes, so a ceiling can be overshot by one call; failed attempts a provider may bill are not visible and are not counted.
- **No hardcoded prices.** Cost is `tokens x` the user's `--price-in` / `--price-out` (per million tokens), or it is reported as "not computed". A cost ceiling without prices is a configuration error. (A price list cannot be verified from here and goes stale.)
- **Usage accounting** comes from the provider's response (tokens in and out, latency per call and in total); when a provider reports none the call is estimated (four characters per token, as T4) and flagged `estimated`.
- **Failure semantics for the interview** (a small change in the Agent project): `IModelFailure` (a controlled code and `IsFatal`) lets a provider failure describe itself without content; `MeteredChatClient` carries the code (never the message) in `ModelCallFailedException`, adds `error.type` to the `chat` span, and the runner **rethrows fatal failures** instead of degrading. Fatal: bad credentials, a rejected request (wrong model or URL, a refused redirect), a spent budget, and **three consecutive failed calls** (`Unavailable`). Non-fatal (degrade to protocol wording, as T4): a rate limit, server error, timeout or network failure after the retries, a single unusable response. The CLI then says the interview stopped, that nothing was kept, and what to check.
- Streaming is off for every provider (every call is one metered call).

## Consequences

- A wrong key or model name stops the interview at the first model call instead of after twenty minutes of fallback questions.
- A provider that fails intermittently still yields an interview (with the protocol's own wording on the failed turns); a provider that is down stops after three calls.
- The defaults (120 s, 3 retries, 1-30 s backoff, three consecutive failures, the 2x/+4 hard ceiling) are starting values and are **not measured** against any real provider.
- Not covered: provider-side rate-limit headers beyond `Retry-After`, circuit breaking across interviews, a graceful close with a partial record when the hard budget is hit (the interview is abandoned instead).
