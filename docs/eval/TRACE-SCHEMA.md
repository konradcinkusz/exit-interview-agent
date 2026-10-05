# Interview trace schema

Status: **Implemented** (T4 agent layer, T6 provider layer); consumed by T7 (eval harness). Companion documents:
[interview agent](../architecture/interview-agent.md), [eval methodology](METHODOLOGY.md),
[privacy design](../privacy/DESIGN.md) (no PII or interview content in logs or traces, brief §6).

The interview agent emits OpenTelemetry spans from one `ActivitySource`, **`ExitInterviewAgent.Agent`** (version `1.0`).
The eval harness subscribes to that source in-process (an `ActivityListener`, no collector, no exporter), the way
`agent-eval-bench` captures its traces, and asserts over span names, attributes and events, never over text. The
vocabulary lives in one file, `src/ExitInterviewAgent.Agent/Tracing/InterviewTelemetry.cs`; **renaming a name there is a
breaking change for the harness** and is reviewed as one. A test fails when this document and that file disagree.

## The rule: metadata only

A span carries counts, flags, enum names and short controlled codes. It never carries interview text, a quote, a name,
an address, a prompt, a model reply or an exception message. This is enforced three ways:

1. **By construction.** The only way to write a tag is `SpanTags` (`Set(int)`, `Set(bool)`, `Set<TEnum>`, `Set<TEnum>(list)`,
   `SetCodes`); it has **no `string` overload**. A code passes through `SafeCode`: at most 48 characters of
   `[A-Za-z0-9_.:-]`, otherwise it is replaced by `invalid_code`.
2. **By shape test.** `TraceTests.Every_string_in_a_trace_is_...` runs every persona and asserts every tag value is an
   int, bool or a controlled code, and that events carry only ints, bools and enum names.
3. **By canary test.** `TraceTests.A_canary_in_the_interviewee_text_...` runs every persona with a marker string appended to
   every interviewee reply, asserts the marker really is in the stored transcript (so the test has power), and asserts it
   appears in **no** span name, tag key or value, event name or tag, status description, baggage item, or log line. Planted
   names and addresses from the persona definitions are scanned for in the same way. Separate tests do the same
   when the model call fails with a message that contains the marker, and when the extractor returns malformed output
   that contains it (model-call failures are rethrown as `ModelCallFailedException`, which carries the exception *type*
   only; extractor errors are the codes `extractor.not_json` and `extractor.schema_violation`).

The same discipline applies to logs: the runner logs through `ILogger` with ids, counts and enum names only, and the canary test
captures every formatted line.

## Spans

One trace per interview. Parent and child relations are the ordinary ones: `interview.session` is the root of the run;
each `interview.turn` is its child; the `chat`, `interview.pii_guard` and `interview.probe` spans of a turn are children of that turn;
the three closing stages are children of the session.

| Span | When | Key attributes |
|---|---|---|
| `interview.session` | the whole run | `gen_ai.operation.name` = `invoke_agent`, `gen_ai.agent.name`, `interview.protocol.version`, and at the end `interview.outcome`, `interview.end_reason`, `interview.turns`, `interview.model_calls`, `interview.tokens.estimated`, `interview.topics.covered`, `interview.ai_disclosed`, `interview.submittable` |
| `interview.turn` | one interviewer turn and the reply to it | `interview.turn.index`, `interview.turn.kind`, `interview.topic`, `interview.phase`, `interview.decision` (the kind of the *next* step), the reply measures and the `interview.signal.*` flags |
| `interview.probe` | the prober role words a concrete-example follow-up | `interview.role` |
| `interview.pii_guard` | one reply masked on arrival | `interview.pii.findings`, `interview.pii.kinds`, `interview.pii.fail_closed` |
| `chat` (display name `chat <role>`) | one model call through the metered client | `gen_ai.operation.name` = `chat`, `interview.role`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens` |
| `interview.extraction` | one extractor attempt (at most two) | `interview.extraction.attempts`, `interview.extraction.schema_valid`, `interview.validation.error_codes` |
| `interview.quote_verification` | the quote step | `interview.quotes.checked`, `interview.quotes.dropped`, `interview.topics.covered` |
| `interview.validation` | `RecordValidator` on the canonical record | `interview.record.valid`, `interview.validation.error_codes` |

A withdrawn, declined or abandoned interview has no `interview.extraction`, `interview.quote_verification` or
`interview.validation` span: that absence is the trace-level proof that no record was built.

## Events

| Event | On | Meaning | Event attributes |
|---|---|---|---|
| `interview.disclosure.delivered` | session | the opening turn (AI disclosure, storage, right to stop, consent request) was delivered and answered; `aiDisclosed` is set only after this | none |
| `interview.consent.granted` | session | the dialogue closed with consent intact | none |
| `interview.consent.withdrawn` | session | the interview stopped without a record (withdrawal, refusal, unclear consent, or the interviewee left); `interview.end_reason` says which | none |
| `interview.transcript.discarded` | session | the transcript was discarded | none |
| `interview.injection.suspected` | turn | the reply read like an instruction to a model; observability only, no decision changes | none |
| `interview.question.rejected` | turn | the question guard replaced a model-worded question by the protocol wording | `reason` (int, index into the guard's reason list) |
| `interview.budget.exhausted` | session | the model-call or token budget was reached; the interview closes gracefully | `interview.model_calls` |
| `interview.names.masked` | turn | a reply named a person and was masked | `interview.pii.findings` |
| `interview.quotes.dropped` | quote verification | quotes were dropped (unverifiable, wrong topic, PII, instruction-like) | `interview.quotes.dropped` |

## Attributes

OpenTelemetry GenAI semantic-convention names are used where they apply (the conventions are still in development; the
names below are the ones in the 1.37 series and are pinned here). The rest use the `interview.` prefix.

| Attribute | Type | Meaning |
|---|---|---|
| `gen_ai.operation.name` | string code | `invoke_agent` on the session, `chat` on a model call |
| `gen_ai.agent.name` | string code | the agent's name, `exit-interview-agent` |
| `gen_ai.request.model` | string code | the model id the client reports (the mock reports `scripted-mock`), passed through `SafeCode` |
| `gen_ai.provider.name` | string code | the provider name the client reports (`mock` for the mock; `anthropic`, `openai_compatible`, `ollama` for the providers) |
| `gen_ai.usage.input_tokens` | int | provider-reported, or a four-characters-per-token estimate |
| `gen_ai.usage.output_tokens` | int | as above |
| `interview.protocol.version` | string code | for example `1.0` |
| `interview.role` | enum | `interviewer`, `prober` or `extractor` |
| `interview.turn.index` | int | the interviewer-turn counter |
| `interview.turn.kind` | enum | `opening`, `consent_reask`, `topic`, `probe`, `clarification`, `redirect`, `close`, `stop` |
| `interview.topic` | enum | the topic name, as in the record |
| `interview.phase` | enum | the machine's phase when the turn was issued |
| `interview.outcome` | string code | `completed`, `withdrawn`, `consent_not_given`, `abandoned`, `pii_guard_failed`, `extraction_failed` |
| `interview.end_reason` | string code | for a completed interview `all_topics_covered`, `unresponsive`, `hostile` or `budget_exhausted`; otherwise `consent_withdrawn`, `consent_declined`, `consent_unclear`, `disconnected`, `pii_guard_failed`, `extraction_invalid` |
| `interview.decision` | enum | the kind of the step the machine chose after this turn |
| `interview.turns` | int | transcript turns at the end |
| `interview.model_calls` | int | calls through the metered client |
| `interview.tokens.estimated` | int | tokens counted against the budget |
| `interview.reply.chars` | int | length of the raw reply in characters (a measure, not content) |
| `interview.reply.words` | int | word count of the masked reply |
| `interview.signal.vague` | bool | the reply is a generality without a concrete cue (a probe candidate) |
| `interview.signal.terse` | bool | at most three words |
| `interview.signal.hostile` | bool | a hostile cue was found |
| `interview.signal.contradiction` | bool | the reply reverses the direction of the earlier answers on the same topic |
| `interview.signal.withdrawal` | bool | the reply withdraws consent |
| `interview.signal.names_person` | bool | the PII guard found a person name |
| `interview.signal.injection_suspected` | bool | the reply reads like an instruction to a model |
| `interview.pii.findings` | int | number of PII findings in the reply |
| `interview.pii.kinds` | enum list | the kinds found, never the text |
| `interview.pii.fail_closed` | bool | true when the detector failed (the interview stops, nothing is kept) |
| `interview.probes` | int | reserved for a run-level count (the diagnostics carry it today) |
| `interview.clarifications` | int | reserved, as above |
| `interview.redirects` | int | reserved, as above |
| `interview.questions.rejected` | int | reserved, as above |
| `interview.reason` | string code | reserved |
| `interview.topics.covered` | int | topics with at least one verified quote |
| `interview.extraction.schema_valid` | bool | the extractor output conformed to `schemas/extractor-output.v1.schema.json` |
| `interview.extraction.attempts` | int | 1 or 2 |
| `interview.quotes.checked` | int | quotes the extractor proposed |
| `interview.quotes.dropped` | int | quotes dropped by the quote step |
| `interview.record.valid` | bool | `RecordValidator` accepted the canonical record |
| `interview.validation.error_codes` | string list | validator or extractor error codes (`SCHEMA_VIOLATION`, `extractor.not_json`, ...), never messages |
| `interview.ai_disclosed` | bool | the record's `aiDisclosed` |
| `interview.submittable` | bool | the record is valid and carries at least one covered topic (submission itself is a later task) |

## What a harness can assert from a trace alone

- **No record without consent:** no `interview.extraction` span exists when `interview.outcome` is `withdrawn`, `consent_not_given` or `abandoned`.
- **Disclosure first:** `interview.disclosure.delivered` precedes every `interview.turn` with `interview.turn.kind` = `topic`.
- **Probe discipline:** per topic, at most `maxProbesPerTopic` turns of kind `probe`, and each follows a turn with `interview.signal.vague` = true.
- **Names:** every turn with `interview.signal.names_person` = true is followed by a `redirect` decision (once per topic).
- **Injection:** turns with `interview.signal.injection_suspected` = true do not change the sequence of `interview.turn.kind` values the machine would produce without them.
- **Cost and latency:** `gen_ai.usage.*` and span durations per role, per interview.

Transcript fidelity, leading-question rate and PII leakage are *artifact* measures (the record and the transcript), not trace
measures; `InterviewInvariants` (in the Agent project) computes the artifact checks and the harness reuses it.

## Provider layer (T6)

Model providers add a second, lower layer from their own source, **`ExitInterviewAgent.Providers`** (version `1.0`), and a meter of the
same name. It obeys the same rule (metadata only) and adds one thing the agent layer cannot know: what happened on the wire. The
vocabulary lives in `src/ExitInterviewAgent.Providers/ProviderTelemetry.cs`; a test fails when this section and that file disagree, and
another when a provider run emits a name that is not listed here. Design: [ADR-0035](../adr/0035-provider-telemetry-and-export.md),
[providers](../architecture/providers.md).

**No content capture, and no switch for it.** Nothing in the Providers source can record a prompt or a completion: no option, no
environment variable (the standard GenAI content-capture switch is ignored), no `gen_ai.input.messages`-style attribute. The only
string a tag can take is a sanitised code (`[A-Za-z0-9_.:-]`, at most 48 characters; anything else becomes `_`). The canary test runs every
provider client with the key, a header, a base-URL path, an echoing error body and the interviewee's marker planted, scanning every
activity of every source in the process, every metric label, every log line and every exception.

| Span | When | Key attributes |
|---|---|---|
| `provider.call` (kind client) | one model call through a provider client, retries included; a child of the agent's `chat <role>` span | `gen_ai.operation.name` = `chat`, `gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.response.finish_reasons`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `exit_interview.provider.attempts`, `exit_interview.provider.usage_estimated`, and on failure `error.type` and `http.response.status_code` |

| Event | On | Meaning | Event attributes |
|---|---|---|---|
| `provider.retry` | `provider.call` | one HTTP attempt failed transiently and will be retried after a backoff | `exit_interview.provider.retry.attempt` (int), `http.response.status_code` (int, 0 when there was no response) |

| Attribute | Type | Meaning |
|---|---|---|
| `gen_ai.response.model` | string code | the model id the provider reports, sanitised |
| `gen_ai.response.finish_reasons` | string list | one of `stop`, `length`, `tool_calls`, `content_filter`, `other`, `none` |
| `gen_ai.token.type` | string code | metric label: `input` or `output` |
| `error.type` | string code | on a failed call: `provider.auth_failed`, `provider.rate_limited`, `provider.server_error`, `provider.bad_request`, `provider.timeout`, `provider.network`, `provider.invalid_response`, `provider.budget_exceeded`, `provider.unavailable`, or `cancelled`. The agent's `chat` span carries the same code in `error.type` when a provider failure was reported to it |
| `http.response.status_code` | int | the last HTTP status seen for the call |
| `exit_interview.provider.attempts` | int | HTTP attempts made (1 means no retry) |
| `exit_interview.provider.usage_estimated` | bool | the provider reported no token counts, so the figures are four characters per token |
| `exit_interview.provider.retry.attempt` | int | the attempt number that failed |
| `exit_interview.provider.retry.reason` | string code | metric label: the HTTP status, or `timeout` / `network` |
| `exit_interview.provider.budget.limit` | string code | metric label: `tokens`, `calls` or `cost` |

`gen_ai.provider.name` takes `anthropic`, `openai_compatible` or `ollama` (the conventions allow custom values where no well-known one
fits). No server address or URL is recorded: where a user's gateway lives is theirs.

**Metrics** (meter `ExitInterviewAgent.Providers`). Labels are low-cardinality by construction: a closed set of keys (operation, provider,
model, token type, error type, retry reason, budget limit) and short controlled values; a test checks every measurement.

| Instrument | Unit | Meaning | Labels |
|---|---|---|---|
| `gen_ai.client.operation.duration` | s | duration of one model call, retries included | `gen_ai.operation.name`, `gen_ai.provider.name`, `gen_ai.request.model`, `error.type` (failures only) |
| `gen_ai.client.token.usage` | {token} | tokens used per call, provider-reported or estimated | the above plus `gen_ai.token.type` |
| `exit_interview.provider.retries` | {retry} | retried HTTP attempts | `gen_ai.provider.name`, `exit_interview.provider.retry.reason` |
| `exit_interview.provider.budget_exceeded` | {event} | calls refused because the hard budget was reached | `exit_interview.provider.budget.limit` |

**Reading usage from a trace.** The agent's `chat` span and the `provider.call` span both carry token counts (the second is the provider's own
accounting, the first is what the budget meter counted). A harness sums **one** of them per interview, never both. Latency from the
`provider.call` span includes retries and waits; per-attempt latency is not recorded.

**Export** is the CLI's concern and is off unless asked ([ADR-0035](../adr/0035-provider-telemetry-and-export.md)): `OTEL_TRACES_EXPORTER` and
`OTEL_METRICS_EXPORTER` (`otlp`, `console`, `none`), or an OTLP endpoint in `OTEL_EXPORTER_OTLP_ENDPOINT`; `OTEL_SDK_DISABLED=true` turns it off.
Only the two sources above and the one meter are registered.

## Not yet covered

- The reserved attributes (`interview.probes`, `interview.clarifications`, `interview.redirects`, `interview.questions.rejected`,
  `interview.reason`) are declared so the names are stable, and are not emitted yet; the counts are in `RunDiagnostics`.
- Sampling, export and the collector are the kernel's concern (P15) and are not configured by the Agent project.
