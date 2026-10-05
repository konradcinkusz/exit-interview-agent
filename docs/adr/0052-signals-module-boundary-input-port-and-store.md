# 0052. Signals module: boundary, input port, and a store that holds only what was published

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: [ADR-0002](0002-service-layout.md) (a module with an enforced boundary, extractable later), P2 (the kernel stays plumbing),
  P3 (a bounded context owns its data), P11 (anti-corruption at the edge), `metric-ethics` §1 (anti-goals enforced by architecture), brief §4 and §6,
  [ADR-0018](0018-records-are-treated-as-personal-data.md) (records are personal data).

## Context

ADR-0002 says `signals-service` starts as a project inside `interview-service`, with its own schema and `DbContext` and no shared domain types, and must be
extractable later. Brief §6 and [ADR-0007](0007-record-context-bands.md) say what the aggregates may read: employer, topic, rating, bands, verification level.
The task left one mechanism open: a projector reading `Records` through a narrow contract, or the module mapping the store itself. Three options were weighed
against the coupling they create and the privacy exposure they leave at rest.

| Option | Coupling | At rest | Verdict |
|---|---|---|---|
| A. The module reads the `Records` table itself and maps `Json` | the module knows the interview schema's table and its column types; one `DbContext` model in two places; extraction means rewriting the reader | nothing new, but the module's code is one query away from `Json` (quotes) | rejected: no boundary at all, only a folder |
| B. A projector copies per-record projections (employer, topic, rating, confidence, bands, verification, week) into a module table, updated on each submission or on a schedule | a narrow contract in, but a second store that deletion, retention and the ledger window must all reach | **a second record-level store, holding sub-k data at rest**, joined to nothing but one row per interview | rejected: every future leak path (backup, replica, a debug query) would need the controls of the record store; deletion by receipt would have to propagate |
| C. A pull port, a full recompute per batch, and a store that holds only the published snapshot | one small interface; the adapter is the only code that reads `Records.Json` | **no record-level data at rest in the module, ever**; cells below k exist only in the memory of one publication run | **chosen** |

## Decision

1. **The input port is `IObservationSource`**, in the module (`ExitInterviewAgent.Signals`). It streams `Observation`s: employer reference, tenure band, optional
   seniority and function band, verification state, and a list of (topic, rating or null). The wire vocabulary is the record schema's (ADR-0007), written out in
   `Vocabulary`. Nothing else crosses: **no interview id, no quote, no confidence, no metadata, no week bucket.** Confidence and week were in the task's list of
   what the store "only needs"; nothing in the rules reads them (batching uses the clock, not the row's age), so they are not carried. A use for either needs its own ADR.
   Contract: observations of one employer are contiguous; the feed is a full snapshot of what is stored now (a deleted record is simply absent next time); the
   source streams.
2. **The anti-corruption layer is `RecordStoreObservationSource`**, in `interview-service` (the only reader of `Records.Json` for the module). It maps the stored canonical
   JSON by the published schema's wire names, not through the record library's C# types, so a rename on the submission side shows up as a failing adapter test, not
   as a silently different vocabulary. Anything that is not a schema-v1 record the module understands is skipped, never repaired. Quotes, the interview id, confidence and
   metadata are read past and dropped inside the adapter, in the process that already holds the record store.
3. **The module owns a `SignalsDbContext` in the `signals` schema with its own migration history** (`signals.__EFMigrationsHistory`) and two tables: `Snapshots`
   (one row per publication) and `EmployerSnapshots` (employer reference and the already-controlled view as JSON). It holds **published aggregates only**. There is
   no numeric column a query could sort by (the view is one JSON column), no `Json` of a record, no quote, no interview id: a schema-invariant test pins the column
   sets in the model and, against PostgreSQL, in the migrated database.
4. **No shared domain types.** The module references the kernel and nothing else of the solution (architecture test over its referenced assemblies); the service
   names Signals types in exactly the adapter, the registration extension and the endpoint slice (architecture test over type signatures). The kernel gained two optional
   parameters on `AddDatabaseContext` (an Npgsql options hook for the history table, and a switch so a second context does not list `database` twice in `/health`); it
   stays plumbing (320 of 800 lines).
5. **Extraction is a transport change.** To move the module out: implement `IObservationSource` over a streaming HTTP or gRPC call (or an event feed that the module
   folds) from the interview side, give the module its own database (its data is derived and rebuildable from the feed, so it moves by republishing, not by copying), and move the
   two endpoints with it behind the same authorization policy. Nothing in the rules, statistics, publisher or reader changes. The adapter is the one class that is deleted.
6. **One publisher instance.** In-process locking serialises publications in a process and a unique fingerprint settles a race between instances, but an instance that
   removes "orphan" rows while another is mid-run could delete the other's invisible rows; run one (threat model T-18 already lists the in-process mechanisms as single-instance).

## Consequences

- The module has no incremental state, so a deletion, a retention purge or a change of rules needs no propagation: the next batch is rebuilt from what is stored. The
  price is a full scan of `Records` per batch (O(records), once a day by default). **Trigger for revisiting:** a measured scan time that no longer fits a quiet window.
  The next design is option B and brings a record-level store with it, so it needs its own ADR, threat-model entry and deletion-propagation test.
- The adapter parses `Records.Json`, including quotes, in the interview-service process. That is the same trust zone that stored them, and nothing the adapter returns
  carries them; it is not a new exposure, but it is not zero. A column holding only the signal fields on `Records` would remove the parse; it would add a
  field to the store (T5's table), which this task does not touch.
- The `signals` schema lives in `interviewdb`. P3 says a database per service; while the module is inside the service, a schema per module is the equivalent. Extraction moves it.
- Postgres orders `EmployerRef` by its collation; the port only asks for contiguity, so any order is fine.
