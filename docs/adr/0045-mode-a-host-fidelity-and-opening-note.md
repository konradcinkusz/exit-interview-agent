# 0045. Mode A: the host runs the interview; the opening gets a note about the provider

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: brief §6 (AI disclosure, never "anonymous"), `docs/privacy/DESIGN.md` §4, threat model T-07, T-16, T-20; ADR-0018, ADR-0019.

## Context

The protocol's opening (R01) says "the full conversation is not stored". True of the exit-interview service in every mode; **not** a statement about the AI provider in mode A, where the chat happens inside the user's assistant under that provider's terms. Read on its own it could mislead. In mode A, nothing in this server can enforce consent, stopping, neutral questions, verbatim quotes or masking: the host model follows instructions, and the server can only check the record it later receives.

## Decision

- The prompt tells the host to say the protocol opening verbatim (translated faithfully for another language, all three points kept), **followed by a fixed note**: the chat is handled by the user's AI provider under its own terms, and the sentence about the conversation not being stored describes the exit-interview service, which receives only the record. The protocol file is not changed (that would be a protocol version bump for all modes).
- Server-side checks stand in for what cannot be enforced: schema, PII re-scan, `aiDisclosed`, size, one submission per employer. They cannot check that quotes are verbatim, that consent was obtained, or that questions were neutral. That is stated in `docs/architecture/mcp.md`, and it is why the evaluation suite is to run the same personas against hosts (T7); until it has, host fidelity is **unmeasured**.
- The record is shown to the user in full and confirmed before `submit_interview_record`; the receipt code is shown once.
- No reliance on sampling, elicitation or roots (ADR-0042). Whether a user can start the flow from a prompt picker or has to ask Claude to use the connector was **not verified**; the server's `instructions` string points the model at the prompt either way.

## Consequences

- Mode A has the weakest privacy and fidelity guarantees of the three modes; README and connect guide say so.
- The note is part of the prompt-text hash in the snapshot; legal review of its wording is open (`docs/OPEN-PROBLEMS.md`, OP-18).
