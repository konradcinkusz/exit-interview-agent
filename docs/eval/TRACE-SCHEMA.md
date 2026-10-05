# Interview trace schema

Status: **Implemented** (T4); consumed by T6 (providers) and by the T7 eval harness (`src/ExitInterviewAgent.Eval`, [ADR-0037](../adr/0037-eval-harness-architecture.md)): `TraceRecorder` captures one trace per run and the Layer 1 graders read only the names and attributes in this file. The operation table the harness derives constraint C-10 from is [SPEC §2](SPEC.md#2-the-operation-table-normative). Companion documents:
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
| `gen_ai.provider.name` | string code | the provider name the client reports (`mock` for the mock) |
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

## Not yet covered

- Model providers (T6) will add provider-specific attributes through the same `chat` span; they must obey the same rule, and
  must wrap provider exceptions the way `MeteredChatClient` does.
- The reserved attributes (`interview.probes`, `interview.clarifications`, `interview.redirects`, `interview.questions.rejected`,
  `interview.reason`) are declared so the names are stable, and are not emitted yet; the counts are in `RunDiagnostics`.
- Sampling, export and the collector are the kernel's concern (P15) and are not configured by the Agent project.
