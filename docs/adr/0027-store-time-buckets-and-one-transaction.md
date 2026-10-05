# 0027. Store-side time buckets (ISO week) and one transaction per submission

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: brief §6 (coarse timestamps, retention), [ADR-0019](0019-brief-amendments-from-the-t3-legal-privacy-review.md)
  (b) and (e), [ADR-0011](0011-no-per-person-identifier-in-the-record.md), P3, `service-api-patterns` §7 (background services wait for the schema).

## Context

The record carries no timestamp at all (ADR-0011). The *store* still needs one thing: when a row may be purged. Any stored time is
a candidate join key between tables and between the store and an observer of live traffic (threat model T-08(c), T-09): a record
row stamped to the second can be matched to a ticket redemption or a ledger entry stamped to the second. Brief §6 allows the ledger
"at most a day-level timestamp (or none)"; ADR-0019 (b) adds that row timing must not be a join key.

The task also asks for the whole submission to be **one transaction** (ledger entry, record, receipt, and, for a ticket, the ticket
delete). That is in tension with the privacy design's earlier proposal (§5.4) of separate transactions from non-adjacent commit points.

## Decision

1. **Granularity: the Monday of the ISO week, UTC, for both `Records.CreatedWeek` and `SubmissionLedger.CreatedWeek`** (`DateOnly`;
   no time component in the type). Not a day: a day bucket gives an observer 7x finer resolution for no operational gain, because
   neither purge needs better than weeks (records live 24 months, the ledger window is 365 days). Not a month: a month is coarser
   than the record's own ISO-week bucket in ADR-0019 (b) and would make the purge up to a month late, and a deployment with few
   submissions is already easy to correlate; a week is the coarsest bucket that keeps the purge close to its configured age and it
   matches the one bucket the brief names. The bucket is a property of the **store**: it is not in the record, and the API never
   returns it.
2. **Purge uses the END of the bucket.** A row is removable when `bucket + 7 days <= today - age`; it is therefore never purged
   before its age is reached and is kept at most one week longer. The purge is tested with a fake clock, including the boundary.
3. **The only precise stored timestamp is a ticket's expiry**, rounded up to a 5-minute step (ADR-0030), because a ticket must expire
   to be a ticket and its row is deleted at redemption.
4. **One transaction.** Ledger check, ledger entry, record, receipt and (for a ticket) the ticket delete commit together or not at all.
   An orphan ledger entry would lock an account out of an employer with nothing stored; an orphan record without a ledger entry would
   defeat one-per-employer. Atomicity is chosen over the alternative of separate commits.

## Consequences

- **Residual, stated in the threat model (T-08, T-09):** rows written in one transaction share the database's hidden transaction id
  (PostgreSQL `xmin`) and sit near each other physically (`ctid`, heap order). Someone with direct access to the database files or
  system columns, or to a physical backup, can in principle pair a ledger entry with a record from them even though no column links
  them and every key is random. Separate transactions would not remove this on a quiet system (adjacent transaction ids), and real
  separation needs a queue with batching and delay, or a separate database, which is not built. Random (non-sequential) keys
  remove only the *key order* channel.
- The in-process (InMemory) provider has no transactions or unique indexes; there the write section is serialised in-process and the
  checks run before any write (`StoreGate`). It is a development and test path, not a production claim.
- Trigger for revisiting: a deployment with enough volume to batch, or a decision to put the ledger in its own database/schema with its own role.
