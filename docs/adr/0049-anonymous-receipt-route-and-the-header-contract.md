# 0049. Receipt deletion goes through its own anonymous BFF route, forwarding exactly one header

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `frontend-bff` §5 (catch-all proxy; deviated from, see the register), [ADR-0029](0029-receipt-deletion-semantics.md),
  threat model T-11 and T-15, `security-review` §5.

## Context

`DELETE /api/v1/receipts` is **anonymous** on the interview-service and takes the code in the `X-Receipt-Code` header (ADR-0029). The BFF's catch-all
proxy (`/api/proxy/[...path]`) requires a signed-in session and injects a bearer; it is the wrong tool: a person who deleted their account (or never
had one on this browser) must still be able to delete a submission, and the request must never carry an account.

## Decision

- A dedicated route, `DELETE /api/receipts` (`web/app/app/api/receipts/route.ts`), **public** in the edge gate. It reads **no cookie**, sends **no
  bearer**, sends **no query string**, and forwards **exactly one header, `X-Receipt-Code`**, to **exactly one backend route, `DELETE /api/v1/receipts`**,
  through the same candidate ladder as the proxy (`lib/upstream.ts`, which forwards only the headers its caller passes: nothing is copied implicitly
  from the browser request). The backend's answer is passed through: `204` for every well-formed code, `400 INVALID_RECEIPT_CODE`
  (problem+json `{type,title,status,code}`), `429` (`{error:"rate_limited",retryAfter}` and `Retry-After`).
- A missing or over-long (> 128 characters) code is refused with the same `400 INVALID_RECEIPT_CODE` shape without asking the backend; a code in the
  URL or query is not read (a test asserts the backend never sees it).
- The catch-all proxy forwards neither `X-Receipt-Code` nor `X-Submission-Ticket`: it builds its upstream headers from `authorization`, `accept` and
  `content-type` only.
- The page words a `204` as "**If** a submission with this receipt code existed, it is deleted now" and says the answer is the same for every correctly
  typed code. It never says "deleted successfully". A `400` says nothing was deleted; a `429` says to wait and gives the service's retry hint, bounded to an hour.

## Consequences

- **Finding, not fixed here (recorded as OP-15):** the backend's per-client window (default 6 a minute) keys on the socket address, or on a forwarded header only when
  `Submission:ClientIpHeader` is configured (ADR-0029). Behind the BFF every anonymous deletion arrives from the web server's address, so for web users that window is
  effectively one shared budget. This task does not forward the visitor's address (that would put an IP into another service for a limiter key; a privacy trade-off for the operator to
  decide in a deployment ADR), and does not change the backend. Locally and in tests it is not visible.
- The route is a second place to keep in step with the backend contract; the e2e stub mirrors it and carries a contract note.
- Trigger to revisit: any other anonymous endpoint reached from the browser (this is the pattern for it, with its own header allow-list).
