# 0029. Receipt deletion: header, uniform answer, checksum, latency floor, limits

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: threat model T-11 and T-15, `security-review` §5 (a GUID is not a secret), `service-api-patterns` §1
  (anonymous traffic: no queue, a global bound under the per-client window, one resolver for the client).

## Context

A receipt code is a bearer secret with no account link: whoever holds it can delete the record, and nobody can list or recover
codes. The endpoint is anonymous, so it is the enumeration and timing surface (T-11). The task text names `DELETE /api/v1/receipts/{code}`;
the threat model (T-09, T-15) says codes and tickets must never be in URLs.

## Decision

- **The code travels in a header**, `X-Receipt-Code`, on `DELETE /api/v1/receipts` (no path parameter). A URL appears in access logs,
  proxy logs, the HTTP server span (`url.path`) and browser history; a header does not by default. This **deviates from the task's
  path form on purpose**; a code in the path or query string is not read (the tests assert it).
- **Code format:** 32 random bytes from the OS CSPRNG plus a 2-byte checksum, URL-safe base64, 46 characters (256 bits of secret, well
  above the 128-bit floor). The server stores only `SHA-256(code)`; SHA-256 is adequate because the input is high-entropy.
- **Answer semantics:**
  - a **malformed** code (wrong length, alphabet or checksum) is `400` `INVALID_RECEIPT_CODE`. The checksum is computable by anyone from
    the code, so this reveals nothing about stored codes, and it turns a typo into a visible error instead of a false "deleted";
  - **every well-formed code** (live, already deleted, never issued) gets the same `204 No Content`. A `204` therefore means "if a record
    with this code existed, it is deleted now" and does **not** confirm that one existed. This is a deliberate cost: a user who holds a
    wrong-but-well-formed code gets no signal. The checksum narrows that to codes that are not typos.
- **Constant-time:** the stored and presented hashes are compared with `CryptographicOperations.FixedTimeEquals`; lookup is by hash
  through a unique index, so the comparison input is not attacker-steerable. Every well-formed request is held to a **minimum duration**
  (`Submission:Receipts:ResponseFloorMilliseconds`, default 150 ms) so a hit (an extra delete) and a miss are not told apart by latency
  below that floor. The floor is not a proof of constant time: a database stall above it still shows.
- **Limits:** a strict per-client window (default 6 per minute) and a **global** budget (60 per minute) beneath it, no queueing, the kernel's
  429 body shape. The client key comes from one resolver: the socket address, or a configured forwarded header
  (`Submission:ClientIpHeader`, for example `Fly-Client-IP`) **only when configuration names one**. The key is a limiter partition and is never stored or logged.
- **Deleting does not touch the ledger** (no link exists), so the account stays closed to that employer until the ledger entry is purged (ADR-0028).

## Consequences

- The web UI (T9) and CLI (T11) must send the header, show the code once with the bearer-secret warning, and word a `204` as
  "if this code was valid, the record is deleted".
- A global budget means a flood can lock legitimate deletions out for a minute; chosen over letting a flood enumerate or exhaust the service.
- An in-process limiter does not share state across replicas (threat model T-18); re-examine before any deployment.
