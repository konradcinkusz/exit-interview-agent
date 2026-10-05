# 0025. Trace schema: spans carry metadata only, enforced by construction and by a canary test

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §6 (no PII or interview content in logs, traces or audit events), P15 (observability), `ai-evals` §4 (assert over traces using the OpenTelemetry GenAI conventions), threat model T-15

## Context

The eval harness asserts over traces, so the agent must emit a stable vocabulary. The privacy design forbids interview content in telemetry. A convention ("do not put text in spans") is a request; the failure mode (a debugging tag that carries a reply) is exactly the one a code review misses.

## Decision

- One `ActivitySource`, `ExitInterviewAgent.Agent`, with spans for session, turn, probe, PII guard, model call (`chat`), extraction, quote verification and validation, and a small set of events. Names are constants in one file; [`docs/eval/TRACE-SCHEMA.md`](../eval/TRACE-SCHEMA.md) documents every one and a test fails when the two disagree. GenAI semantic-convention names (`gen_ai.operation.name`, `gen_ai.agent.name`, `gen_ai.request.model`, `gen_ai.provider.name`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`) are used where they apply; the conventions are in development, so the names used are pinned in the document. The rest use the `interview.` prefix.
- **Metadata only, by construction:** `SpanTags` offers typed writers for integers, booleans, enum names and lists of enum names or controlled codes, and **has no `string` overload**; a code is accepted only if it is at most 48 characters of `[A-Za-z0-9_.:-]`. Provider exceptions are rethrown as type names only; extractor and validator errors are codes.
- **Enforced by tests:** a shape test over a trace of every persona (every tag value is an int, a bool or a controlled code); a **canary test** that runs every persona with a marker appended to every interviewee reply, asserts the marker is in the stored transcript (so the test has power), and asserts it and every planted name or address are absent from every span name, tag, event, status, baggage item and log line; failure-path variants (a model that throws a message containing the marker, malformed extractor output containing it); and a test that the scanner itself fails on a deliberate leak.
- **Test isolation:** the capture helper opens its own root activity and keeps only spans in its trace, because the listener is process-wide and xUnit runs classes in parallel.
- The harness subscribes in-process with an `ActivityListener`, the way `agent-eval-bench` does; export and sampling remain the kernel's concern.

## Consequences

- T6 providers and T7 build on a fixed vocabulary; adding a name is a documented, tested change.
- Some useful debugging detail is deliberately unavailable (what the interviewee said, what the model replied); diagnosis relies on counts, decisions and codes, and on rerunning a persona with the same seed.
- Reserved attribute names (`interview.probes`, `.clarifications`, `.redirects`, `.questions.rejected`, `.reason`) are declared and not yet emitted; the counts are in `RunDiagnostics`.
- Not covered: how a real provider's own instrumentation is configured. A provider project must not enable any option that records message content, and T6 must extend the canary test to its client.
