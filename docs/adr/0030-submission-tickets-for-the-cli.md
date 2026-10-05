# 0030. Submission tickets: expiry rounding, caps, atomic redemption, and what narrows the correlation point

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: brief §4 (binding decision on CLI login), threat model T-09 and T-13, `security-review` §5.

## Context

The CLI cannot hold an OAuth client secret (brief §4). The web panel mints a ticket; the CLI redeems it with a record. The brief
fixes the shape (random, short-lived, single-use, not employer-bound, row deleted at redemption) and names the redemption instant as
a correlation point to be documented and narrowed.

## Decision

- **Mint:** `POST /api/v1/tickets`, policy `account`, returns `{ticket, expiresAt}` once. 256 random bits, URL-safe base64 (43 characters).
  The store keeps `SHA-256(ticket)`, the account `sub`, and the expiry: the only table that holds a `sub`. TTL default 15 minutes.
- **Narrowing, what was done:**
  1. **Expiry is rounded up to a 5-minute step**, so the row does not hold the mint instant to the second (the TTL is therefore 15 to 20 minutes).
  2. **The ticket is not employer-bound and carries no record reference**, so the table never says which employer or record.
  3. **No other timestamp:** the record and ledger rows carry only a week bucket (ADR-0027).
  4. **Header only** (`X-Submission-Ticket`), never a URL; no ticket, `sub` or record in any log, span or metric (canary test).
  5. **Deleted at redemption, in the same transaction** as the ledger entry and the record; expired rows are swept every few minutes.
  6. **At most 3 live tickets per account** and **10 mints per account per hour** (per account, not per address), so the table
     stays small and holds few `sub`-to-time pairs.
- **Redeem:** `POST /api/v1/submissions/ticketed`, anonymous, ticket in a header, per-client and global limits (as ADR-0029), body capped at the record limit.
  Order: look up the ticket read-only; validate the record; PII re-scan; verify; then, in one transaction, run every check that can
  reject (id clash, ledger) **before** consuming the ticket, delete the ticket with a single atomic statement whose affected-row count
  decides the winner, and write ledger, record and receipt. A rejected submission therefore leaves the ticket usable; a race gives exactly one winner
  (24 parallel redemptions tested on PostgreSQL). Unknown, expired, used, malformed and missing tickets are one answer: `401 TICKET_INVALID`.
- **Not done, on purpose:** random commit delay, queued/batched commits and separate transactions. They do not hide the instant from an observer of live
  traffic, and on a quiet system do not hide it from the database either (ADR-0027). Adding them would cost latency and complexity for a protection the
  honest threat model cannot claim.

## Consequences

- **The redemption instant remains a correlation point** (T-09, accepted by the brief): one request carries a ticket (resolvable to `sub`) and a record. An observer
  of live traffic at the edge, or with access to the database's transaction ids and a snapshot containing the dead ticket tuple, can pair them. Standard HTTP-server
  spans also record method, route, status, User-Agent and timing; none of it is content, all of it is timing.
- A user who loses a ticket mints another; a ticket stolen within its lifetime submits as that account (once).
- Trigger for revisiting: public-client support in authservice (OP-7) makes tickets unnecessary.
