# 0055. Publication: batched snapshots, coarse time, atomic swap, and honest deletion

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: [ADR-0019](0019-brief-amendments-from-the-t3-legal-privacy-review.md) (c) (batched publication), [ADR-0027](0027-store-time-buckets-and-one-transaction.md) (coarse time is
  not a join key), P4 and `service-api-patterns` §7 (migrations first), P8 (degrade, visibly), `metrics-exposition` §1 (every label is a cardinality decision), threat model T-01, T-08.

## Context

A live aggregate that moves from n = 5 to n = 6 tells an observer about the sixth record, and the moment it moves is a join key to whoever submitted then. Deleting a record
(receipt code) must not create a second moment. The module must also survive a restart, run exactly once per period, never grow without bound, and say when it is stale.

## Decision

1. **The unit is the batch period** (`Signals:PublishInterval`, whole hours from 1 to 168, default 24). A snapshot is built once per period from everything stored, never on a
   submission, a deletion or a request. No code path from a request reaches the publisher (the only caller outside the hosted loop is the Development-only demo seeder).
2. **Coarse time.** The snapshot records the **start of its period** (aligned to the interval since the Unix epoch, UTC; midnight for a day), never the moment the run finished. The API returns
   that value as `generatedAt` and `Last-Modified`. Nothing else in the module stores a time.
3. **Idempotent and restart-safe.** The fingerprint is `period | interval | rules version | k`. A snapshot with the current fingerprint is never rebuilt, so a second tick, a restart or a
   second replica does nothing; only the database remembers a publication. `RunDueAsync` is the whole unit of work, and the hosted loop only looks at the clock every `CheckInterval` (default 5 min).
4. **A change of k or of the rule version republishes at once.** A stricter k must not wait for midnight. This is an operator action, not something a submission can trigger, and it does show
   records that arrived since the last batch to anyone reading afterwards; stated here, not hidden.
5. **Atomic swap without a transaction.** Employer rows are written first, invisible; the `Snapshots` row is inserted last and is the publication (readers follow the highest sequence number);
   older rows are then deleted. A run that dies half way leaves invisible rows that the next run removes; a failed run keeps serving the previous snapshot. (The InMemory provider has no transactions, so the
   protocol does not rely on one.) A unique index on the fingerprint settles a race between instances.
6. **Bounded memory.** The builder folds one employer at a time from a streaming feed (a few hundred integers per employer, nothing per record) and writes in batches of 100 rows. A feed that is not grouped by employer
   fails the run rather than publish a half-counted employer.
7. **Deletion semantics, stated honestly:** a record deleted by receipt code, or purged by retention, **stays in the published numbers until the next batch**. The snapshot header says so
   (`deletionsAppearAtNextPublication`), the OpenAPI text says so, and the UI copy contract requires it. After the next batch the cell is recomputed from what remains and withdrawn if it falls below k.
8. **Operations.** Logs: one line per publication with the number of employers published, the rule version and k; a failure logs the exception *type* only. Metrics: `signals.publications` (label `outcome`:
   published, skipped, failed), `signals.publication.duration`, `signals.snapshot.employers`; nothing carries an employer, a topic, a band or a count of suppressed cells (how many cells were withheld is itself a
   statement about small groups, so it is not emitted). `/health` lists the `signals` integration (not configured when `Signals:Enabled` is false) and a `signals` check: unhealthy until the schema exists, degraded
   when the last run failed or the snapshot being served is more than two periods old, never a hard failure for a stale aggregate. Observations outside the vocabulary are counted and skipped, never described.

## Consequences

- A person who deletes their record sees the aggregate keep it for up to one period; the receipt flow's confirmation must say so (T9 copy). Shortening the interval shortens this and widens the timing signal; the floor is one hour.
- Between two batches a reader cannot tell whether anything happened; between two batches *a day apart* an adversary who added one record of their own learns, at best, about k - 1 other people ([ADR-0053](0053-disclosure-control-clean-partitions-and-k-per-cell.md)).
- Single publisher instance ([ADR-0052](0052-signals-module-boundary-input-port-and-store.md) item 6).
