# 0028. Submission ledger: keyed HMAC, rotation, and the window default

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: brief §6 (ledger), P5 (secrets come from the environment, never a default), `security-review` §5,
  threat model T-08, T-13; [ADR-0019](0019-brief-amendments-from-the-t3-legal-privacy-review.md) (e).

## Context

One submission per employer per account must hold without storing "account X submitted record R". The employer-id space is small,
so an unkeyed hash of (`sub`, employer) would be reversible by enumeration; a key makes the table opaque to anyone who has the
database but not the key. `sub` is the same user id on web and MCP tokens (ADR-0012), so one tag serves every submission path.

## Decision

- **Tag** = lowercase hex `HMAC-SHA-256(key, "exit-interview-agent/ledger/v1" ‖ len(sub) ‖ sub ‖ len(employerRef) ‖ employerRef)`, lengths as
  4-byte big-endian, so no two (`sub`, employer) pairs concatenate to the same bytes. The employer is not stored in the clear.
- **Row** = random uuid key, key id, tag, ISO-week bucket (ADR-0027). **No record id, no interview id, no receipt reference, no
  foreign key** (enforced by `SchemaInvariantTests`, also against the migrated PostgreSQL schema).
- **Uniqueness is the database's**: a unique index on the tag decides a race; the application's pre-check is a fast path. Tested with 24
  parallel requests on PostgreSQL: exactly one success.
- **Rotatable key set** from configuration: `Ledger:ActiveKeyId` and `Ledger:Keys:<n>:{Id,Secret}` (base64, at least 32 bytes; the secret comes
  from the environment or a secret store). New entries use the active key; **every listed key is tried on lookup**, so adding a key
  and switching the active id does not reset duplicate suppression. An old key is removed from configuration after the ledger window;
  entries under an unknown key id are then inert until the purge removes them.
- **No default key.** Outside Development the service **refuses to start** without a valid key set. In Development an ephemeral key is
  generated at startup; `/health` and the startup banner say so (`ledger-key: DEGRADED ... EPHEMERAL ...`); the key is never logged.
  Entries written under an ephemeral key do not survive a restart (documented dev-only behaviour).
- **Window default: 365 days** (`Submission:Retention:LedgerWindowDays`). Reasoning: the ledger is the one table that records
  "this account wrote about that employer", so every day it is kept widens the surface (T-08); but the window is also how long
  duplicate suppression lasts. The records it guards live 24 months (ADR-0019 (e)). Choosing the record age (730 days) would
  give perfect suppression for a record's whole life at twice the exposure; choosing a few weeks would let the same account stack
  records almost at will. 365 days covers the realistic case (a person revising or re-trying an exit interview about one job) while
  halving the exposure against 730. The number is an **assumption, not a measurement**, and is configuration.

## Consequences

- **After the window the same account can submit again for the same employer**, and the earlier record (if not yet purged) stays in the
  aggregate: the one-per-employer rule is time-limited, and this must not be described as a permanent guarantee (threat model T-10).
  Deleting a record by receipt does not clear the ledger entry (no link exists to find it): that account cannot resubmit for that
  employer until the entry is purged.
- An operator with database **and** key can compute tags for every account and employer and confirm "S submitted about E" (T-08, accepted).
  Keeping the key outside the database backup domain is an operations rule, not something code can enforce.
- Rotation is operator work and is not automated; there is no key-derivation or HSM integration.
- Trigger for revisiting: counsel's view on retention, or a volume at which a shorter window is workable.
