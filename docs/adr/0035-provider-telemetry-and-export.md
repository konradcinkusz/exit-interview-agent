# 0035. Provider telemetry and export: GenAI conventions, no content capture, opt-in exporters

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): P15 (observability: OTLP first), [ADR-0025](0025-trace-schema-metadata-only.md) (metadata only), ADR-0014 (no PII in telemetry), `ai-evals` §4 (one trace vocabulary for offline and production), brief §6

## Context

T4 emits `chat` spans from the metered client with the provider and model the client reports. T6 must add the provider layer, register the sources, make export an explicit opt-in, and make it impossible to put prompt or completion text into telemetry.

## Decision

- **Two sources, one meter.** `ExitInterviewAgent.Agent` (T4) and `ExitInterviewAgent.Providers` (version `1.0`), and the meter `ExitInterviewAgent.Providers`. The CLI registers exactly these (a test pins the list); the HTTP stack's and the SDKs' own sources are never exported.
- **A `provider.call` span** (kind client, a child of the agent's `chat <role>` span) per model call, with GenAI semantic-convention attributes pinned to the 1.37 series names: `gen_ai.operation.name` (`chat`), `gen_ai.provider.name` (`anthropic`, `openai_compatible`, `ollama`: custom values are allowed where no well-known one fits), `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.response.finish_reasons` (mapped to a closed set), `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `error.type` (our failure code), `http.response.status_code`, plus `exit_interview.provider.attempts`, `.usage_estimated`, and a `provider.retry` event with ints only. No server address is recorded (a user's gateway host is theirs).
- **Metrics with low-cardinality labels only:** `gen_ai.client.operation.duration` (s) and `gen_ai.client.token.usage` ({token}) as in the conventions, `exit_interview.provider.retries` and `.budget_exceeded`. Label keys are a closed set (operation, provider, model, token type, error type, retry reason, budget limit); values are short controlled codes (the model id is sanitised to `[A-Za-z0-9_.:-]`, 48 characters). A test checks every measurement.
- **Content capture is off and not available.** No option, switch or environment variable exists to record prompts or completions: the Providers source reads no environment variable at all (a source scan), contains no `OpenTelemetryChatClient`, no `EnableSensitiveData`, and no member whose name offers capture (a reflection scan); the only string a tag takes is a sanitised code; and a test sets the standard GenAI content-capture switch and a switch named after the OpenAI SDK's experimental telemetry (the name is from general knowledge and was not verified, so it may do nothing) to `true`, runs interviews through every provider and asserts that nothing leaks and no `gen_ai.input.messages`-style key appears.
- **The canary test is extended to every provider client.** A marker in the interviewee's words (so it travels in prompts), the API key, a response header, a base-URL path and a provider error body that echoes the request are planted; none may appear in any activity of **any source in the process**, any event, tag or metric label, any log line, or any exception (message, type chain, stack, data). The path canary is asserted against the project's own sources only, because the HTTP stack's own spans (which are not exported) carry the request path. Each case first proves it has power (the marker reached the fake provider; the key went out; the span exists).
- **Export is off by default and opt-in through the standard variables** (`TelemetrySetup`): traces when `OTEL_TRACES_EXPORTER` lists `otlp` and/or `console`, or, when it is unset, when `OTEL_EXPORTER_OTLP_ENDPOINT` (or the traces-specific one) is set; metrics likewise with `OTEL_METRICS_EXPORTER`; `OTEL_SDK_DISABLED=true` turns everything off; `OTEL_SERVICE_NAME` is honoured. The OTLP exporter reads protocol and headers from its standard variables. The **console exporter is buffered and printed once when the interview ends** so it does not interleave with the dialogue. When nothing is selected no provider is built at all.
- **Docs and drift test:** `docs/eval/TRACE-SCHEMA.md` gains a "Provider layer" section; a test fails when it and `ProviderTelemetry` disagree, and another when a run emits a name that is not a documented constant.

## Consequences

- T7 can read tokens and latency from either layer (the agent's `chat` span or the provider span): a harness sums **one** of them, never both (the schema says so).
- Packages `OpenTelemetry` and `OpenTelemetry.Exporter.Console` 1.19.1 join the already pinned OTLP exporter, referenced by the CLI only; the Providers and Agent projects reference no SDK.
- Not covered: log export, sampling configuration beyond what the SDK reads from its standard variables, exporting from the interview service (the kernel's concern, P15).
