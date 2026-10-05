# Privacy design

Status vocabulary used in this document (and in the threat model): **Implemented** = on `main`, with a link;
**Planned (Tn)** = specified by the [brief](../architecture/PROJECT-BRIEF.md), owned by task *n*, not yet on `main`;
**Decided (Tn)** = a former proposal adopted into the brief by [ADR-0019](../adr/0019-brief-amendments-from-the-t3-legal-privacy-review.md), owned by task *n*, still not implemented; **Proposal** = this document's recommendation, not yet accepted by the owning task; **Assumption** = stated, not
verified. Nothing below is claimed as implemented unless it says so. As of this writing the only domain-relevant
code on `main` is the scaffold: an account endpoint ([`AccountEndpoints.cs`](../../src/ExitInterviewAgent.InterviewService/Endpoints/AccountEndpoints.cs)),
JWT validation against a JWKS, and an empty `InterviewDbContext`. See [ADR-0017](../adr/0017-documentation-layout-and-claim-status.md).

Companion documents: [threat model](../security/THREAT-MODEL.md), [legal considerations](../legal/CONSIDERATIONS.md),
[open problems](../OPEN-PROBLEMS.md). Binding inputs: [brief](../architecture/PROJECT-BRIEF.md) §2, §4, §6.

## 1. What is being protected, and from whom

A former employee tells an AI interviewer why they left. The value of the product is candour; candour exists only
if the person can trust that what they say cannot be traced back to them by the former employer, by the operator of
the service, or by anyone who later obtains the database. The design therefore tries to make three links hard:

1. **person ↔ record** (who said this?),
2. **person ↔ employer** (does this account belong to employer X? — the brief forbids modelling it, decision 3.1),
3. **record ↔ submission event** (when did this account submit?).

It also tries to keep the interview *transcript* out of our systems entirely (modes A and B).

**Honest framing.** "No user id in the record" is a property of the schema, not proof of anonymity. Verbatim quotes,
tenure/role bands and the employer field can identify a person in a small team, and quotes are free text a person
can recognise by style. [ADR-0018](../adr/0018-records-are-treated-as-personal-data.md) records the stance this
design takes: **records are treated as personal data (at best pseudonymous), never as anonymous**, and every control
below is built on that assumption. Whether they are legally anonymous is a question for counsel
([CONSIDERATIONS §2](../legal/CONSIDERATIONS.md)), not something the architecture can assert.

## 2. The data we hold

| Store | Holds | Never holds | Status |
|---|---|---|---|
| **authservice** (own DB, own key) | account (`sub`, email, credentials), consent rows, its own audit events, OAuth grants for MCP clients | any interview content, employer, record, ledger entry | Implemented as a pinned image ([ADR-0003](../adr/0003-identity-authservice-as-pinned-image.md)); authservice's audit/consent/export behaviour is read from [its docs](https://github.com/konradcinkusz/authservice/blob/main/docs/issue-analysis.md) |
| **Record store** (`interviewdb`) | the versioned record: per-topic rating (1-5 or null), verbatim supporting quotes, confidence, PII-masked flag, `aiDisclosed` flag, pseudonymous interview id, employer id, coarse bands, receipt-code **hash** | user id / `sub`, account email, IP, user agent, receipt code in clear | Planned (T1 schema, T5 store) |
| **Submission ledger** (separate table, ideally separate schema/DbContext) | keyed HMAC of (`sub`, employer id); key version; day-level (or no) creation timestamp | content, record id, interview id, receipt hash, IP | Planned (T5) |
| **Ticket table** | hash of a random ticket, `sub` it was minted for, expiry | employer, record, content | Planned (T5, mint UI T9, redemption by CLI T11) |
| **Signals read model** (`signals` schema) | the published snapshot: per employer × topic, n, mean with interval, reliability, coverage band, and clean single-band cuts, each from at least K ratings | individual records, quotes, interview ids, any cell below K; it keeps no record-level state at all | **Implemented** (T10, [AGGREGATION](AGGREGATION.md), [ADR-0052](../adr/0052-signals-module-boundary-input-port-and-store.md)) |
| **Logs / traces / metrics** | request metadata: route, status, latency, size class | content, quotes, employer, `sub`, receipt codes, tickets, IP beyond what the platform adds | Planned (T5/T6 enforce; kernel telemetry exists, see [`00-ARCHITECTURE.md`](../architecture/00-ARCHITECTURE.md) P15 row) |

Model access is behind `IChatClient` (brief §4); the project hosts no model, so the AI provider is *outside* this
table by design and appears in the who-sees-what table below.

## 3. Data flow

```mermaid
flowchart LR
    subgraph User["User's device and accounts"]
        U([Former employee])
        HOST["MCP host (claude.ai)\nholds the full transcript"]
        CLI["CLI (mode B)\nholds the full transcript locally"]
        BR["Browser (mode C)"]
    end
    subgraph Prov["AI provider (user's own key or account)"]
        LLM["Model API / local model"]
    end
    subgraph Ours["Operator-run services"]
        WEB["web (BFF)\naccount, consents, tickets,\ndeletion, aggregates"]
        AUTH["authservice\nsub, consents, audit"]
        subgraph IS["interview-service"]
            ING["Ingest\nschema, PII check, limits"]
            REC[("Record store\nno user id")]
            LED[("Ledger\nHMAC(sub, employer)")]
            TKT[("Tickets\nhash, sub, expiry")]
            SIG["Signals module\nn >= K, uncertainty"]
        end
    end

    U <-->|"A: interview happens in chat"| HOST
    HOST <-->|"transcript"| LLM
    U <-->|"B: interview in terminal"| CLI
    CLI <-->|"transcript"| LLM
    BR <--> WEB
    WEB <-->|"login, consents"| AUTH
    WEB -->|"mint ticket (needs sub)"| TKT

    HOST -->|"A: record via MCP tool\n(OAuth, sub from token)"| ING
    CLI -->|"B: record + ticket"| ING
    ING -->|"sub or ticket-derived sub"| LED
    ING -->|"record, no sub"| REC
    ING -. "redeem + delete" .-> TKT
    REC --> SIG
    SIG -->|"aggregates only"| WEB
    BR -. "C: receipt code deletes record" .-> WEB
    WEB -->|"delete by receipt hash"| REC
    AUTH -. "JWKS (validation only)" .-> IS
```

Reading the diagram: the dashed `ING → TKT` edge is the ticket redemption correlation point (§6.4); the dotted
`AUTH → IS` edge is key distribution only, never user data. The transcript exists in exactly two places per mode:
the user's device and the AI provider. It never reaches an operator-run service in modes A and B.

## 4. Who sees what, per usage mode

Legend: **T** = full transcript, **R** = the submitted record, **S** = the account `sub`, **E** = employer id,
**L** = ledger entry, **—** = nothing. This is the design's intent; it holds only where the status says so.

| Party | A: MCP (Claude host) | B: CLI (own key / local model) | C: Web |
|---|---|---|---|
| **The user** | T, R | T, R, the ticket | account, own consents; no list of own records (by design, no link) |
| **MCP host** (the AI client the user chose; today claude.ai only, [brief §3.2](../architecture/PROJECT-BRIEF.md)) | **T**, R, and the tool-call arguments, subject to the host's own terms and retention | not involved | not involved |
| **AI provider** | the host's provider sees T under the user's own account terms | **T** under the user's API terms (or nothing, with a local model) | not involved (the web runs no model) |
| **interview-service (ingest)** | R, S (from the OAuth token) at the moment of submission; **never T** | R, S (only via ticket redemption); **never T** | S on authenticated calls; never R except by receipt-hash lookup |
| **Record store after commit** | R (no S) | R (no S) | R (no S) |
| **Ledger** | L = HMAC(S, E) | L | — |
| **authservice** | S, account data, consent/audit events; **no R, no E** | S at ticket-mint time only (it authenticates the web login) | S |
| **Operator (DB access only)** | R, aggregates; ledger rows are opaque without the key | same | accounts via authservice DB |
| **Operator (DB + HMAC key)** | can test "did account S submit about employer E?" (§6.3) | same | same |
| **Operator (DB + key + live traffic/timing)** | additionally can correlate by time (§6.3, §6.4) | same | same |
| **Employer / public** | aggregates only, n ≥ K, with uncertainty; never an individual record | same | same |

Two consequences worth stating plainly:

- In mode A the **server cannot verify that quotes are verbatim**, because it never sees the transcript. In mode B
  the transcript is on a machine we do not control. Both submit *claims*; transcript fidelity is a property the eval
  harness measures on simulated interviews ([methodology](../eval/METHODOLOGY.md)), not something ingest can enforce.
- In mode A the **PII guard must run server-side** on the submitted record, because the host model is not ours.
  Names that the host model let through into quotes are caught (or not) by ingest, not by the interview.

## 5. The mechanisms

### 5.1 Record without a user id (Planned, T1/T5)

The record schema has no `user_id`, `sub`, email or account reference; the interview id is a random pseudonymous
identifier unrelated to any account. Brief §6 lists the topics (onboarding, management, growth, pay vs promises,
culture, reason for leaving). Controls on what *is* in the record:

- **Verbatim quotes only, capped in length and count**, PII-masked before storage. Free text is the main
  re-identification and defamation vector; a length cap is also the cheapest mitigation. *Proposal:* cap quote length
  and count in the schema (T1), and treat the cap as part of the privacy contract, not a UX choice.
  *Decided in T1:* at most 5 quotes per topic, 400 code points each, no control characters
  ([record-schema](../architecture/record-schema.md)).
- **Coarse bands, not exact values** for tenure and role family. *Proposal:* the band set is chosen against the
  smallest realistic group (see §5.5 and [OPEN-PROBLEMS](../OPEN-PROBLEMS.md)).
  *Decided in T1:* tenure (6 bands, required), seniority (4) and function (6), the last two optional
  ([ADR-0007](../adr/0007-record-context-bands.md)); the threshold K must be applied to every published cut.
- **Coarse timestamps.** *Decided (ADR-0019, not implemented):* the record carries no timestamp finer than an ISO-week bucket, and the ledger
  none finer than a day (or none at all), so row timing does not become a join key (§6.3).
  *Implemented in T1, stricter:* the schema carries **no timestamp at all**, and an architecture test
  rejects one ([ADR-0011](../adr/0011-no-per-person-identifier-in-the-record.md)). Storage time is the store's concern (§6.3).
- Every record from a client is untrusted input: schema validation, PII detection, rate and size limits
  (brief §6, T5).

### 5.2 Submission ledger (Implemented, T5)

Purpose: enforce **one submission per employer per account** without storing "account X submitted record R".

- Row = `HMAC-SHA-256(key_vN, sub ‖ employer_id)` plus key version and a coarse creation bucket. No content, no
  record id, no interview id, no receipt hash (brief §6). *Implemented in T5:* the input is length-prefixed, the bucket is the ISO week
  ([ADR-0027](../adr/0027-store-time-buckets-and-one-transaction.md), [ADR-0028](../adr/0028-submission-ledger-hmac-rotation-and-window.md)); the table
  layout and what each table can link are in [submission-flow](../architecture/submission-flow.md).
- The key is **rotatable**: the active key is used for new entries and every key still inside the retention window
  is tried on lookup. Rotation therefore does not reset deduplication as long as old keys are kept for the window.
- Entries are **purged after a configurable window** (brief §6). After the purge the same account can submit again
  for the same employer; that is the price of not keeping a permanent "this account wrote about that employer"
  index, and it is a deliberate trade-off, not a bug.
- **Limits, stated honestly.** The employer-id space is small, so anyone who holds the key can brute-force
  "did account S submit about employer E?" for every E. The HMAC protects the ledger from someone who has the
  database but **not** the key; it does not protect against an operator who has both (brief §6 says so; the threat
  model lists it as a residual risk). Keep the key outside the database's backup domain (platform secret, not a DB
  column) so a database leak alone does not hand over both. *Implemented in T5:* keys come from configuration (`Ledger:Keys`), the service does not start
  without one outside Development, and a Development key is ephemeral and announced. Default window: 365 days (an assumption, ADR-0028).
- Deleting a record by receipt code does **not** remove the ledger entry (the link does not exist, by design). A
  user who deletes cannot resubmit for that employer until the entry is purged. This is documented user-facing
  behaviour, not a defect.

### 5.3 Deletion by receipt code (Implemented in T5 server; T9 web Planned)

On submission the server returns a random receipt code once and stores only its hash.

- The code is a bearer secret. *Implemented in T5 ([ADR-0029](../adr/0029-receipt-deletion-semantics.md)):* 256 random bits plus a checksum, URL-safe, sent in a header (never the URL);
  every well-formed code gets the same `204` whether or not it matched. *Original proposal:* ≥128 bits from a CSPRNG, URL-safe encoding; a fast hash is then
  adequate because the input is high-entropy, and lookup by hash must be constant-time with identical responses and
  comparable timing for "unknown code" and "already deleted" (enumeration, see threat model). Follows the
  recurring rule in [`security-review`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SECURITY-REVIEW.md) §5
  (a GUID is not a secret).
- Presenting the code deletes the record. Because the code is not tied to any account, **anyone holding the code can
  delete the record**, and **a lost code means the user cannot delete it**. Both are inherent to unlinkability.
- Deleting an *account* (authservice soft-delete + reaper, [identity guide](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/IDENTITY-AND-ACCOUNTS.md) §8) cannot delete
  the person's records, because nothing links them. The UI must say so before the user deletes the account and
  tell them to use their receipt codes first. (Tension with GDPR erasure: [CONSIDERATIONS §2](../legal/CONSIDERATIONS.md).)
- Deleting a record can drop a group below K. Aggregates are recomputed from the remaining records and
  withdrawn when n < K at the next batch (Implemented, T10: until then the deleted record is still counted, and the API says so); backups age out on the platform's schedule (state it in the retention table).

### 5.4 Submission tickets for the CLI (Server Implemented in T5; T9 and T11 Planned)

The CLI cannot hold an OAuth client secret, and authservice registers only confidential clients (brief §4), so:

1. The web panel, authenticated through authservice, mints a ticket: random (≥256 bits), short-lived, single-use,
   **not bound to any employer**; the server stores its hash with the `sub` and an expiry.
2. The CLI sends the record plus the ticket.
3. Redemption yields `sub` once; the ledger entry is computed from it, the record is stored without it, and the
   ticket row is deleted.

Residual risk (brief §4): the **redemption instant is a correlation point**. At that moment one request carries a
ticket (resolvable to `sub`) and a record. The mitigations below narrow, but do not close, that window:

- no ticket, `sub`, record or employer in logs/traces (the telemetry rule, §5.7);
- coarse timestamps: the record and ledger rows carry a week bucket only, and the ticket's expiry is rounded up to 5 minutes (*Implemented, T5*). The
  earlier proposal (separate transactions, batching or jitter) was **declined**: ledger entry, record, receipt and the ticket delete commit in **one transaction**,
  because it does not hide the instant from a live observer and leaves orphan states otherwise ([ADR-0027](../adr/0027-store-time-buckets-and-one-transaction.md),
  [ADR-0030](../adr/0030-submission-tickets-for-the-cli.md)); rows of one transaction still share a hidden transaction id (see submission-flow);
- ticket rows deleted at redemption and expired rows swept on a short schedule, so a database snapshot taken later
  contains few `sub`↔time pairs;
- tickets are not employer-bound, so the ticket table never says *which* employer.

An operator who records live request timing at the edge (reverse proxy, platform logs) can still join ticket
redemption to record insertion. That is listed in the [threat model](../security/THREAT-MODEL.md) as a residual
risk, not as solved.

### 5.5 Aggregates: k-threshold, uncertainty, no ranking (Implemented, T10)

The rules, their tests and their limits are in [AGGREGATION.md](AGGREGATION.md); decisions in [ADR-0052](../adr/0052-signals-module-boundary-input-port-and-store.md) to
[0056](../adr/0056-signals-api-caching-rate-limits-and-demo-data.md). In short:

- Published only when n ≥ K **per displayed cell** (default 5, configurable, at least 3). The default is the brief's number, not a privacy guarantee: K = 5 is a convention
  and its adequacy depends on band granularity ([OP-3](../OPEN-PROBLEMS.md#op-3-tenure-and-role-band-granularity-vs-small-groups), [AGGREGATION §7](AGGREGATION.md#7-what-k-does-not-protect-against)).
- **Single-band cuts only, each a clean partition or withheld whole**: a cut is published only when every band is empty or has at least K ratings and so is the group that left the band out. This replaces the
  textbook "hide the smallest shown cell too", which is safe for one snapshot and not for two one record apart ([ADR-0053](../adr/0053-disclosure-control-clean-partitions-and-k-per-cell.md)).
- **Differencing.** The snapshot is rebuilt **once per batch** (default a day), never on a submission; it carries the start of its period, not the moment a run ended. Within one snapshot no group below K is
  recoverable by subtraction; for an adversary whose own record moves between two snapshots, no group of *other* people below K - 1 is (exhaustive and property tests, with mutants).
- **Uncertainty on every number**: n, a 95% interval that is not falsely precise at small n, a reliability label that moves with n, and the coverage of the cell next to the rating
  ([metric-ethics §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md)). With n = 5 the interval is wide and the UI states that plainly.
- **No composite employer ranking and no per-person view**, enforced by absence: no field, column, query or route for a score, an average across topics, a rank or a percentile; employers are listed alphabetically;
  there is no endpoint that returns a record or a quote. The module has its own project, schema and `DbContext` with no shared domain types (brief §4, [ADR-0002](../adr/0002-service-layout.md)).
- Deletions (receipt code, retention purge) reach the aggregates **at the next batch** and the API says so.
- Individual reviews are never published (brief §2). See [CONSIDERATIONS §3](../legal/CONSIDERATIONS.md).

### 5.6 Consents and accounts (T2 Implemented: consent gate, account deletion semantics; T9 Planned: the rest of the portal)

Portal login is an **account only**, with no link to an employer (brief §3.1). Versioned legal consents live in
authservice ([identity guide §9](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/IDENTITY-AND-ACCOUNTS.md)): immutable rows with document, version, timestamp, IP, user agent, locale.
Two design consequences:

- authservice necessarily holds IP and user agent for consent rows; that is identity-side data and is the reason the
  ledger and tickets must never be joined to it in logs.
- Consent **to our terms** is separate from consent to **interview processing**; consent *withdrawal mid-interview*
  stops the interview and discards the transcript on the client (agent rule, brief §6; T4/T7 test it as a persona).
  What is withdrawn after submission is handled by receipt-code deletion.

**Implemented (T2).** The BFF shows a consent step before anything else when authservice says the Terms or Privacy version in force has not
been accepted, and the API proxy refuses calls until then ([ADR-0013](../adr/0013-bff-session-refresh-rotation-and-consent-gate.md)). Account
deletion goes through authservice and removes the login only; the table of what it does and does not remove (records untouched and
unfindable, ledger purged by its window, issued tokens valid until expiry) is in [ADR-0014](../adr/0014-account-deletion-semantics-and-no-pii-in-telemetry.md),
and the portal says so before and after deletion.

### 5.7 Telemetry and audit without content (T5 canary test Implemented; T6 Planned; scaffold and T2 email scrubbing Implemented)

Brief §6: no PII or interview content in logs, traces or authservice audit events. The scaffold already exports
traces by OTLP only when configured and filters probes ([`00-ARCHITECTURE.md`](../architecture/00-ARCHITECTURE.md), P15 row);
that is plumbing, not yet a content guarantee. T2 added data minimisation (the principal drops the `email` claim) and an email-address scrubber on every
log line ([ADR-0014](../adr/0014-account-deletion-semantics-and-no-pii-in-telemetry.md)); authservice's own audit rows still include the actor's email.
The enforceable form for content (T5/T6):

- spans and logs carry route, status, latency, size class, model name, token counts: never prompt text, quotes,
  employer, `sub`, receipt code or ticket;
- a test that submits a record containing a unique canary string and asserts the canary appears in no log line, span
  attribute or audit event (a test that cannot fail is worse than none: it must fail when logging is added). *Implemented in T5*
  (`ContentCanaryTests`, see the threat model T-15): logs, activity tags and events, event-source payloads and metric tags are captured over the
  whole submission, ticket and receipt flow, and body- and header-logging regressions are shown to be caught;
- the eval harness's traces for **simulated** interviews may carry content (they contain no real data); the same
  span schema in production does not. The two must be separated by configuration, not by hope.
- authservice audit events record account actions (role changes, etc.); the interview-service must not call
  authservice to record anything employer- or content-related.

## 6. Retention

Defaults are **proposals** except where the brief fixes them. Every value is configuration; none is a legal
determination ([CONSIDERATIONS](../legal/CONSIDERATIONS.md)).

| Data | Retained until | Mechanism | Status |
|---|---|---|---|
| Transcript (modes A/B) | never held by us. In B: user's machine, until the user deletes it; the CLI keeps no transcript by default (*Proposal*) | n/a | Planned (T4) |
| Transcript at the AI provider | per the provider's terms for the user's own key/account; **outside our control** | n/a | see [CONSIDERATIONS §1](../legal/CONSIDERATIONS.md) |
| Record | until deleted by receipt code, the operator purges (e.g. on employer removal), or the operator-configured maximum age is reached (default 24 months, an assumption: [ADR-0019](../adr/0019-brief-amendments-from-the-t3-legal-privacy-review.md)) | deletion by receipt hash; age-based purge job (week bucket, [ADR-0027](../adr/0027-store-time-buckets-and-one-transaction.md)) | Implemented (T5); operator purge by employer is not built |
| Receipt-code hash | with its record | same | Implemented (T5) |
| Ledger entry | configurable window, default 365 days ([ADR-0028](../adr/0028-submission-ledger-hmac-rotation-and-window.md)), shorter than the record age on purpose | scheduled purge job; old HMAC keys are removed from configuration by the operator after the window | Implemented (T5) |
| Ticket | 15-20 minutes (TTL, expiry rounded up to 5); deleted at redemption | delete at redemption and a 5-minute sweep | Implemented (T5) |
| Account, consents, authservice audit | per authservice: soft delete with a retention window then a reaper | authservice | Implemented in authservice ([guide §8](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/IDENTITY-AND-ACCOUNTS.md)); window values are authservice configuration |
| Aggregates | the snapshot is rebuilt from the remaining records once per batch; a cell is withdrawn when n < K, and a deleted record stays in the published numbers until then | Signals publisher | **Implemented** (T10, [ADR-0055](../adr/0055-publication-batches-snapshot-and-deletion-semantics.md)) |
| Logs and traces | platform-set; contain no content (§5.7) | n/a | Planned |
| Database backups | the platform's backup schedule; deletions reach backups only as they age out | n/a | **Assumption**: no backup policy exists yet (nothing is deployed) |

## 7. GDPR roles, as considerations (not conclusions)

This section only names the questions. It is not legal advice and takes no position the sources do not support;
statute text could not be read in this authoring environment (see the verification table in
[CONSIDERATIONS](../legal/CONSIDERATIONS.md)).

| Actor | Candidate role | Why it is open |
|---|---|---|
| Whoever **operates** an instance (hosts interview-service, web, authservice) | controller of accounts, records, ledger, aggregates | the project ships open-source software and hosts nothing today; roles attach to an operator, not to the repository |
| The **user** who runs the CLI with their own key | a person using a tool on their own account; the provider relationship is theirs | whether a user acting about their own employment is outside "controller" in practice is a counsel question |
| The **AI provider** | its own controller or a processor to the user, depending on its terms and the account type | the project cannot choose this; it can only document it |
| The **MCP host** | as above, and also a recipient of tool-call data | |
| A **named third party** (e.g. a manager named in free text) | data subject whose data may be processed without notice | the reason names are detected and masked (brief §6) |

International transfers occur at the user's choice of provider and key, not at ours; the design cannot prevent
a user from sending their own transcript to a non-EEA provider, and says so to the user (T9 copy, *Proposal*).

## 8. What this design does not do

- It does not prove authenticity: a hostile client can fabricate records, and ledger-based one-per-employer limits
  volume per account, not truth ([threat model](../security/THREAT-MODEL.md)).
- It does not verify employment: `EmploymentVerifier` is an interface and a mock (brief §2); see
  [OPEN-PROBLEMS](../OPEN-PROBLEMS.md).
- It does not defend against an operator who holds the database, the HMAC key and live traffic.
- It does not protect a transcript once it is with the user's AI provider or MCP host.
- It is not deployed, and no real person's data is processed anywhere (brief §2).
