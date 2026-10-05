# 0014. Account deletion semantics, and no PII in logs or traces

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `IDENTITY-AND-ACCOUNTS.md` §8, `SECURITY-REVIEW.md` (logging), P15 (no scopes exported);
  brief §3.1 (an account has no link to an employer), §6 (no user id in the record; no PII or interview content in logs,
  traces or authservice audit events).

## Context

Accounts live in authservice. Records live in `interview-service` and, by design (brief §6), carry **no account reference**:
the only per-account data this system will hold is the submission ledger, a keyed HMAC of (account `sub`, employer id) with
no content and no record id, purged after a configurable window (T5). Deletion by receipt code removes a record without
linking it to anyone.

## Decision

**Deleting an account deletes the login and nothing else, and the product says so.**

| What | Effect of account deletion | Why |
|---|---|---|
| The account at authservice | Soft-deleted at once (`DELETE /api/v1/auth/account` requires the literal `DELETE` and, for password accounts, the password); every refresh token and MCP authorization is revoked; permanent deletion after authservice's retention period (30 days: `ApplicationUser.DefaultRetentionDays`, same value at `v0.3.4` and at commit `cccf978`) | identity is authservice's |
| Submitted records | **Untouched. They cannot be found.** | no record carries an account reference, so there is nothing to delete by account |
| The way to remove a record | Its **receipt code**, held only by the submitter; lose it and the record cannot be removed by anyone | deletion by receipt is the only unlinked path |
| Ledger entries | Not deleted on account deletion; **purged by the retention window** | the entry is a keyed hash of `sub`; locating it requires the key and the `sub`, and deleting it early would let a new account for the same person resubmit to the same employer inside the window |
| Access tokens already issued | Keep validating until `exp` (web: authservice's access lifetime; MCP: 15 minutes) | JWTs are not recallable (authservice D4); observed after deletion |

Consequences stated plainly: after deleting an account, a person who submits from a *new* account is a different `sub`, so the
one-per-employer rule does not span the two accounts. That is accepted: it follows from "an account is an account only", and the
alternative (keeping a durable per-person identifier) is exactly the linkage the design refuses.

**User-facing copy** (`web/app/lib/copy.ts`, shown on the account page before the user commits and again after deletion,
asserted by a unit test and three Playwright specs): the deletion removes the login; it *does not delete anything submitted*
because submissions are not linked to the account; removal is by receipt code only, so keep it; the ledger marker expires by itself.
The copy never promises that "all your data is deleted", which the system could not keep.

**The BFF** exposes `DELETE /api/auth/account`, which forwards the password and the confirmation to authservice, stores and logs
neither, maps authservice's errors to stable codes, and clears the session cookies on success.

**No PII in logs, traces or audit events.**
1. *Data minimisation first.* authservice puts `email` (and a name) in every token. After validation `interview-service`
   keeps `sub`, `client_id`, `scope` and protocol claims on both schemes (ADR-0012); an email never reaches a handler, a log
   statement or a span tag. A unit test asserts both schemes apply it; a mutation (keeping all claims) fails the probe test.
2. *Scrubbing second.* `ILoggerFactory` is wrapped (`Infrastructure/Logging`): every message, structured argument and exception
   text has email addresses replaced with `[email-redacted]` before any provider (console, OTLP) sees it. Tests: arguments and
   exceptions; a request whose URL carries an address (the framework's own request log); a token with an email claim.
   Scopes are not scrubbed; OTLP already excludes them (`IncludeScopes = false`).
3. *Not covered, and why:* free text a person types is not logged by anything in this repository (it never receives
   interview content: brief §5A). The scrubber matches addresses, not names or other PII: it is a net, not the policy.

## Consequences

- **authservice's own audit rows include the actor's email** (for example `AccountSoftDeleted` records `actorEmail`). That is outside
  this repository and cannot be changed here; it conflicts with brief §6 for the identity data it holds. Proposal for authservice, in
  the T2 report: a setting that omits email from audit metadata (hash or user id only).
- Residual: an operator with access to authservice's database sees accounts and emails; that is identity data, not record data, and
  is the reason records never reference accounts.
- The ledger window and its default are decided in T5; this ADR only fixes that account deletion does not touch it.
