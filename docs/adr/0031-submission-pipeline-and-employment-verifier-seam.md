# 0031. The submission pipeline, the PII re-scan, and the employment-verifier seam

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: brief §2 (verification interface plus mock), §6 (untrusted input, fail closed), P8 (optional integrations degrade and are
  visible), P11 (one application service behind three entry points), threat model T-04, T-10, T-15.

## Context

Three entry points submit records (the web endpoint now, the MCP tool in T8, the ticketed CLI endpoint). A record from a client is untrusted input
even when a client says it is masked (brief §6).

## Decision

- **One `SubmissionService`**: input is the raw bytes and a `sub` (or a ticket that resolves to one); output is a receipt code (once) or a stable
  rejection (`code`, validation `errors` of code and schema path, PII `kinds`). Endpoints and the MCP tool only translate it.
- **Order:** size (hard cap while reading the body, then again in the service) -> `RecordValidator` (schema, depth, duplicate keys, `piiMasked`) ->
  **server-side PII re-scan** -> AI disclosure -> verification -> ledger/id checks -> persist. The library's own AI-disclosure check is switched off in
  this service so that the disclosure rejection comes after the re-scan, as specified; the code is the library's `AI_NOT_DISCLOSED`.
- **Re-scan scope:** every quote, plus every other string field that can carry free text (`employerRef`, `protocolVersion`, `language`). The interview id is not
  scanned: the schema pins it to 32 hex characters, which cannot hold personal data, and a random hex string sometimes looks like a numeric identifier to
  the detector (observed while writing the tests). **Any exception or timeout in the scanner is a rejection** (`PII_CHECK_FAILED`), enforced in the service as well as
  in the scanner. The response names kinds only. The detector runs with its default (not fail-closed) mode, an allow-list and the fail-closed switch are configuration; the
  detector's limits are those of [pii-detector.md](../privacy/pii-detector.md), so "passed the re-scan" is not "contains no personal data".
- **Employment verification** is `IEmploymentVerifier` with a **mock** that answers `verified`, `unverified` or `unavailable` from configuration. It receives the account and
  the employer claim, never the record. The result is stored as a coarse **`Verification` column on the store row** (`Verified`, `Unverified`, `Unchecked`), not in the record
  schema. A verifier that throws, hangs (2 s default) or is `unavailable` yields `Unchecked`: **submission degrades, it does not fail** (P8). `Submission:Verification:RejectUnverified`
  is an opt-in stricter policy (`EMPLOYMENT_NOT_VERIFIED`); off by default because with no real verifier nothing could be accepted. `/health` lists the integration as **not
  configured** with the mock's mode, because a mock verifies nothing (OP-1).
- **The client-chosen interview id is a key**, so a clash is refused (`INTERVIEW_ID_TAKEN`, 409) before any write, and a race is settled by the primary key.
- **Telemetry:** the only emitted signal is a counter of outcomes whose label is `accepted` or a rejection code (fixed vocabulary). No employer, account, ticket, receipt or quote is a log
  argument, span attribute or metric label (the canary test, T-15). Rejections are not logged at all.
- **Temporary MCP probe:** in Development only, `POST /mcp/_submit` lets an MCP principal reach `SubmissionService` so the two-scheme wiring is tested end to end until T8 mounts the tool.

## Consequences

- A real verifier will link an account to an employer inside the verifier, the link the rest of the design avoids (OP-1); that is a future ADR.
- Pre-existing detector limits (names without titles, mixed languages) mean the re-scan lowers, and does not remove, the chance that personal data is stored (T-02).
- A legitimate employer slug with a long digit run can be rejected as a phone-like number; an allow-list does not cover that case (the allow-list is for person-name detection). Trigger: the employer registry (OP-2) issues the ids.
