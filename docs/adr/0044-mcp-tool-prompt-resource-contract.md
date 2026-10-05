# 0044. The MCP contract: two tools, one prompt, three resources, static text

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: threat model T-07 and T-03 (we are an injection source if our descriptions can change), `SERVICE-API-PATTERNS.md` §2 (one submission implementation), P11; brief §5.A, §6.

## Context

Everything the server publishes is read by the host model as trusted text. The server never sees the transcript, so the only things that cross are the record (in) and codes (out).

## Decision

- **Tools**: `validate_interview_record` (read-only, idempotent, not open-world; dry run through `SubmissionService.CheckAsync`: size, schema, PII re-scan, AI disclosure; no verifier call, no ledger read, no write, no metric, so it cannot reveal whether the account already submitted) and `submit_interview_record` (not read-only, not idempotent, not destructive, not open-world; `SubmissionService.SubmitAsync(bytes, sub)` with the `sub` of the MCP principal, never an argument). Both take one argument, `record`, a JSON value passed on **as the exact text received** so duplicate keys and depth are judged by the record library. There is no transcript or free-text argument; a record with an extra field is `UNKNOWN_FIELD`.
- **Results** are JSON with a fixed vocabulary: `status`, `stored`, a stable `code`, `errors[{code,path}]`, `piiKinds`, and, once, `receiptCode` with a fixed notice. A rejection is a tool error (`isError`), a dry-run finding is not. No result contains a byte derived from the submitted record.
- **Prompt** `conduct_exit_interview(language?, employerHint?)`: built from `InterviewProtocol.Current` and fixed text (`InterviewInstructions`). `language` is reduced to a 2–3 letter code or the default; `employerHint` to at most 80 letters, digits and a few separators, placed on one labelled data line. The instructions include a note, absent from the CLI, that the chat is handled by the user's AI provider (ADR-0045).
- **Resources** (read-only, versioned URIs, never edited in place): `exit-interview://schema/record/v1` (the embedded schema), `exit-interview://protocol/v1` (the protocol file), `exit-interview://topics/v1` (topics, status semantics, rating and confidence anchors).
- **Scope**: every tool call also checks its own scope (`McpToolScopes`); a tool without an entry is closed. Today both need `interview:submit`, the same as the HTTP policy, so the check is defence in depth, tested by relaxing the policy.
- **Snapshot**: `mcp-contract.snapshot.json` pins names, titles, descriptions, schemas, annotations, server info and instructions in full, and SHA-256 prefixes of the prompt text and each resource. A change fails the test until a person regenerates it (`UPDATE_MCP_SNAPSHOT=1`) and the diff is reviewed in the PR.

## Consequences

- `record` has no `type: object` in its input schema (the SDK emits an unconstrained schema for a raw JSON value); the description and the schema resource carry the shape. Using a typed object would lose exact-text validation or echo keys in errors. Revisit if hosts mishandle it.
- Rating anchors 2–4 in the topics resource are this project's wording (the schema defines only 1 and 5); changing them is a protocol-behaviour change reviewed through the snapshot.
- Removed: `GET /mcp/_probe` and `POST /mcp/_submit` (T2/T5 development endpoints). The tests that used them go through `tools/list` and the tools.
