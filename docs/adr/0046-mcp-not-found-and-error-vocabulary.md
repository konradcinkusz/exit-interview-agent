# 0046. MCP errors: stable codes, protocol errors only for protocol problems

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `SERVICE-API-PATTERNS.md` §3 (validation), `SECURITY-REVIEW.md` §7; ADR-0029 (stable codes); threat model T-15.

## Context

A host model can only fix what it can read. The SDK turns an exception inside a tool into a generic "An error occurred invoking '…'" (and logs the exception), which tells the model nothing.

## Decision

- Everything the model can act on is a **tool result**, not an exception: validation and submission rejections carry the same codes and paths as the web API (`NOT_JSON`, `DUPLICATE_KEY`, `UNKNOWN_FIELD`, `PAYLOAD_TOO_LARGE`, `PII_DETECTED` with kinds, `AI_NOT_DISCLOSED`, `ALREADY_SUBMITTED`, `INTERVIEW_ID_TAKEN`, `EMPLOYMENT_NOT_VERIFIED`, …), plus `INSUFFICIENT_SCOPE` and `UNAUTHENTICATED` from the tool layer.
- Unknown methods, tools, prompts and resources are answered by the SDK's normal protocol errors, after the closed vocabulary has replaced the client's string (ADR-0043).
- HTTP-level refusals (401, 403, 400, 413, 429) are problem details or the standard challenges from T2, never echoing request values.
- Rate limiting is the existing per-account `api` policy (120 a minute) on the whole mount; a model that loops on validation is slowed, not blocked, and receives 429.

## Consequences

- A host model can loop on `validate_interview_record` up to the rate limit; the prompt caps it at three rounds, which the server cannot enforce.
- Any new tool must add its code vocabulary here and an entry to `McpToolScopes`.
