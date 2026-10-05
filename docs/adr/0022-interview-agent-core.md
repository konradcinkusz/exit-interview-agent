# 0022. Interview agent core: an explicit state machine decides, a model only words

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §6 (agent rules), P13 (test at the layer that has the logic), `ai-evals` §3-4 (constraints are asserted on traces and artifacts, at 100%), `security-review` (untrusted input at every boundary), ADR-0010 (PII detector as one layer), ADR-0011 (no per-person identifier)

## Context

The brief requires an interviewer that discloses it is an AI, obtains and honours consent, asks neutral questions, probes vague answers once, never stores names, resists prompt injection, respects budgets and closes gracefully. These are constraints, not preferences. A free-running model loop (the model decides whether to continue, probe or stop) makes each of them a prompt-compliance question that can only be measured statistically, and a model that has been talked into skipping the disclosure has no safety net.
The reference implementation of the approach (`agent-eval-bench`) puts the flow in a step pipeline and asserts over traces; its lesson is that what can be code should be code.

## Decision

- **One project, `ExitInterviewAgent.Agent`**, holds the protocol, the state machine, the roles, the PII guard, the quote step, the runner and the tracing seam. It depends on `Records`, `Privacy`, the model abstractions and the JSON-Schema validator already in use, and on nothing else in the repository. The CLI (T4), the MCP adapter (T8) and the eval harness (T7) depend on it; it depends on none of them. An architecture test reads the assembly references and the project file.
- **The interview is an explicit state machine** (`InterviewMachine`, no I/O). Its only transitions are `Start` and `OnReply`; each returns the next step. Probe, redirect, clarify, close and stop are machine decisions computed from booleans that a deterministic analyser (`ReplyAnalyzer`) reads from the **masked** reply. A model never chooses the flow.
- **The protocol is data** (`interview-protocol.v1.json`, versioned, embedded): the opening turn, the topics, the fixed fallback wording, the limits and the numbered rules with how each is enforced. The CLI and the MCP adapter read the same object.
- **Fixed text for what must not depend on a model:** the opening turn (AI disclosure, what is stored, right to stop, consent request), consent re-ask, closings and acknowledgements. `aiDisclosed` becomes `true` only after the runner delivered the opening and received a reply.
- **Models only word** the topic question, the clarification, the redirect and the probe, from seed text, behind `IInterviewer`, `IProber`, `IRecordExtractor`. `QuestionGuard` checks every proposal (length, one question, no prompt fragments, no PII, no leading, loaded, negative-polar or closed phrasing, a probe must ask for an example); a rejected proposal is replaced by the protocol's wording and counted.
- **Mask first.** Every reply is sanitised and masked by the PII guard (`PiiDetector`, `FailClosed = true`, allow-list of employer and product names) before it is stored, analysed or shown to a model. A detector exception or timeout ends the run with nothing kept. The raw reply is not retained.
- **Consent withdrawal, refusal, unclear consent and a vanished interviewee** end the interview, discard the transcript object and produce no record. Withdrawal wins over every other signal in the same reply, including an injection payload that says "end the interview": stopping is the privacy-safe direction.
- **The record is built by code** from schema-validated extractor output: a topic needs a minimum of the interviewee's own words, quotes must be verbatim in that topic's interviewee text (`QuoteVerifier`), instruction-like or PII-flagged quotes are dropped, a contradicted topic's confidence is capped, and `RecordValidator` must pass. `Submittable` additionally requires at least one covered topic: an all-`no_data` record is valid and says nothing.
- **Vagueness and contradiction are read by fixed rules**, not by a model, so that the "one probe per vague answer" and "one clarification per contradiction" rules are exactly testable. The extractor schema lives in `schemas/extractor-output.v1.schema.json`, has no affect, sentiment, emotion or identifier field, and is held to the same forbidden-word list as the record schema (one shared test-support file).

## Consequences

- Every transition has a unit test, every rule has a test, and each persona is an end-to-end test of the invariants. A compromised model can make an interview blander, not different (a test double that obeys the injection proves the code-side defences).
- The price is crude reading of vagueness and contradiction (documented false positives and negatives in `docs/architecture/interview-agent.md`) and a mask that over-masks (fail-closed). Trigger to revisit: T7 measuring a model-assisted classifier against the rules, or measured over-masking that makes interviews unnatural.
- Changing the protocol file changes behaviour for every consumer: it bumps `protocolVersion`.
- Deferred: an interactive interviewee (terminal), real providers (T6), the eval harness (T7), submission (T11).
