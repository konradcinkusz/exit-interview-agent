# Open problems

What this project does **not** solve, why each matters, what it does about it now, and what would close it. A listed problem is
an acknowledged decision; an unlisted one is drift ([`security-review` §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SECURITY-REVIEW.md)).
Status vocabulary: [ADR-0017](adr/0017-documentation-layout-and-claim-status.md). Related: [threat model](security/THREAT-MODEL.md),
[privacy design](privacy/DESIGN.md), [legal considerations](legal/CONSIDERATIONS.md).

| # | Problem | Severity |
|---|---|---|
| OP-1 | Real employment verification | High |
| OP-2 | Employer registry and identity | High |
| OP-3 | Band granularity vs small groups | High |
| OP-4 | Sample bias toward technical users | Medium |
| OP-5 | Model bias | Medium |
| OP-6 | Multilingual interviews (Polish and English first) | Medium |
| OP-7 | CLI login without public-client support in authservice | Medium |
| OP-8 | MCP sampling support | Low |
| OP-9 | Only Claude is supported through MCP | Low |
| OP-10 | Per-host client registration | Low |
| OP-11 | Judge calibration labels | Medium |
| OP-12 | Receipt codes: access without a list of records | Medium |
| OP-13 | Storage-level correlation between the ledger and the records | Medium |
| OP-14 | One submission per employer is time-limited by the ledger window | Medium |
| OP-15 | Anonymous receipt deletion behind the BFF shares one rate-limit key | see section |
| OP-16 | Manual accessibility pass | see section |
| OP-17 | Two-factor sign-in has been tested only against the stub | see section |
| OP-18 | Mode A: host fidelity, the opening text, and the unverified Claude run | High |
| OP-19 | K is a convention, and small batches expose small differences | High |
| OP-20 | Homogeneous cells are shown | Medium |
| OP-21 | What is withheld is itself a signal | Low |
| OP-22 | Clean partitions withhold more than a textbook rule would | Medium |
| OP-23 | CLI secrets are protected by discipline, not by the platform | Medium |
| OP-24 | A submission can end with an unknown outcome and a lost receipt | Medium |
| OP-25 | CLI submission has not run over real TLS, on Windows/macOS, or against a deployment | Medium |
| OP-26 | Readers may still compare employers by eye, and nobody has tested comprehension | Medium |
| OP-27 | A browser may keep Signals answers after sign-out, until the batch ends | Low |
| OP-28 | The Signals pages have only met the stub | Medium |

## OP-1. Real employment verification

- **Why it matters.** Without proof that the author worked at the employer, the system cannot tell a real ex-employee from a script,
  a competitor or the employer itself ([threat model T-10](security/THREAT-MODEL.md)). Every aggregate is "claimed by accounts".
- **What we do now.** `IEmploymentVerifier` is an interface with a mock that answers what configuration says (brief §2; implemented in T5, [ADR-0031](adr/0031-submission-pipeline-and-employment-verifier-seam.md));
  records are stored with a coarse `Verified`/`Unverified`/`Unchecked` level that nothing reads yet; an unavailable verifier degrades to `Unchecked`; one submission per (account, employer) via the
  ledger; rate and size limits; uncertainty on every number; outputs must say "claimed", not "verified".
- **What would close it.** A verification method that does not reintroduce a person↔employer link in our database: for example,
  a third-party attestation that issues an unlinkable, employer-scoped credential (blind-signature style) or a work-email challenge whose
  result is not stored. Each has its own privacy cost and legal review; none is chosen. Whichever is chosen is an ADR and a threat-model revision.

## OP-2. Employer registry and identity

- **Why it matters.** "Employer" is a field of the record (brief §3.1). Free-text names split one employer into many (defeating K) or let
  a user create a one-person "employer" to expose someone; legal entities, brands and subsidiaries differ.
- **What we do now.** Nothing in code: the model is Planned (T5). *Proposal:* a closed, operator-curated employer id list with canonical
  names; unknown employers are not accepted in v1.
- **What would close it.** A curated registry with a documented policy for merges, minimum size and removal (an employer that asks to be
  removed), plus a size floor below which an employer's signals are never shown ([OP-3](#op-3-tenure-and-role-band-granularity-vs-small-groups)).

## OP-3. Tenure and role band granularity vs small groups

- **Why it matters.** Finer bands make a record more informative and a small group easier to identify ([T-01](security/THREAT-MODEL.md)); K = 5
  per employer says nothing about K per band.
- **What we do now.** The brief fixes n ≥ K (default 5). **Implemented (T10, [AGGREGATION](privacy/AGGREGATION.md), [ADR-0053](adr/0053-disclosure-control-clean-partitions-and-k-per-cell.md)):**
  K per displayed cell, single-band cuts only, each a clean partition or withheld whole, batched publication, coarse bands. The band sets are ADR-0007's; no employer-size floor exists, so an employer with a
  handful of records shows nothing only because no cell reaches K.
- **What would close it.** Band sets chosen against a modelled smallest-realistic-group, a documented size floor for employers, and (if the
  project later wants stronger guarantees) a formal approach such as noise addition, evaluated rather than assumed. Evidence needed:
  simulated re-identification tests on synthetic populations (eval-adjacent work for T7/T10).

## OP-4. Sample bias toward technical users

- **Why it matters.** Users who run an MCP host or a CLI with an API key are mostly technical; aggregates will over-represent software
  employers and under-represent people for whom this tool is out of reach. The signals are not a representative view of any employer.
- **What we do now.** The web path (mode C) runs no model and the web app is aimed at account management; interviews need a model access
  route, so the bias is structural. Documentation says aggregates reflect *users of this tool*.
- **What would close it.** A hosted, operator-paid interview path (explicitly outside the current brief: "the project hosts no model") and a
  published description of who the respondents are; until then, no claim of representativeness.

## OP-5. Model bias

- **Why it matters.** The interviewer, extractor and judge are language models: they may probe some groups of people or some topics differently,
  mis-rate accented or non-native writing, or mask names unevenly across languages and cultures (a PII guard tuned on English names misses others).
- **What we do now.** Nothing measured yet. The eval harness ([METHODOLOGY](eval/METHODOLOGY.md)) defines metrics per persona class; PII leakage
  is measured with a planted-token set (T7).
- **What would close it.** Persona sets that vary language, register and name origin on the same underlying story, with leakage and fidelity
  reported per variant and compared; a stated limit when a language has no coverage. Requires writing the persona corpus (T7) and judging by humans.

## OP-6. Multilingual interviews (Polish and English first)

- **Why it matters.** The target users include Polish speakers; PII detection, quote fidelity, masking and judge rubrics are language-dependent,
  and a record's quotes are verbatim in the original language.
- **What we do now.** Docs are English (brief §9.9). The record schema, personas and PII detector are Planned (T1/T7); no language handling exists.
- **What would close it.** A declared language per interview, language-specific PII rules and tests (declension makes name masking harder in
  Polish), personas in Polish and English, and a conformance report per language. Until then the supported-language statement is "English
  tested, Polish untested".

## OP-7. CLI login without public-client support in authservice

- **Why it matters.** authservice registers only confidential clients (secret ≥ 32 bytes, no `none`, no device grant, no DCR; authservice ADR-0005),
  and a distributed CLI cannot hold a secret. The ticket workaround adds the redemption correlation point ([T-09](security/THREAT-MODEL.md)).
- **What we do now.** Submission tickets minted by the web panel ([privacy design §5.4](privacy/DESIGN.md#54-submission-tickets-for-the-cli-planned-t5t9t11)); no change to authservice (brief §4).
- **What would close it.** Public-client + PKCE (or the device-authorization grant) in authservice, which means amending its ADR-0005 by its owner;
  then the CLI can authenticate directly, and the ticket row (the correlation point) disappears. The CLI would still send a record under the user's `sub`
  to compute the ledger entry, so *some* identity must reach the ingest at submission, at the moment of submission.

## OP-8. MCP sampling support

- **Why it matters.** Sampling would let our server ask the user's host model to run parts of the protocol, but client support is uneven.
- **What we do now.** Not used, referenced or possible: the server is stateless and a test scans the assembly ([ADR-0042](adr/0042-mcp-sdk-and-streamable-http-stateless.md)). Anthropic's connector documentation,
  read 2026-10-05 ([Build an MCP server for Claude](https://claude.com/docs/connectors/building/index)), says Claude "doesn't yet support" resource subscriptions, sampling and advanced or draft capabilities, which is why the design is right for now.
  Elicitation is not listed as supported either way; also unused. Other hosts: not checked.
- **What would close it.** Read the host's published documentation for the supported MCP features, record the result and date in an ADR, and
  add it only if it brings a privacy or quality benefit.

## OP-9. Only Claude is supported through MCP

- **Why it matters.** The privacy of mode A depends on one host's terms and retention (T-07); users of other assistants are excluded.
- **What we do now.** Claude only, redirect URI `https://claude.ai/api/mcp/auth_callback` (brief §3.2).
- **What would close it.** Per-host verification of the MCP authorization flow and data terms, then an ADR per host.

## OP-10. Per-host client registration

- **Why it matters.** authservice registers MCP clients statically from configuration (its ADR-0005: no dynamic registration); each host needs its own
  client id, secret, redirect URIs, scopes and resource, held by the operator.
- **What we do now.** Wired for Claude (T2, [ADR-0012](adr/0012-two-jwt-schemes-and-the-mcp-resource-server.md)): the AppHost configures one client, the service validates its tokens and
  serves RFC 9728 metadata; the MCP transport is Implemented (T8, [mcp.md](architecture/mcp.md)). The operator runbook is [`guides/connect-claude.md`](guides/connect-claude.md): it marks every step not verified live. Needs two public https URLs locally ([`scripts/README.md`](../scripts/README.md)).
- **What would close it.** A documented operator runbook per host and a startup check that fails loudly on a missing client; dynamic registration
  would need authservice to change its stance.

## OP-11. Judge calibration labels

- **Why it matters.** An LLM judge that was never compared with people is a measuring stick nobody has checked
  ([METHODOLOGY §7](eval/METHODOLOGY.md); the reference repository's own labels were AI-written and are described there as a rehearsal).
- **What we do now.** **Implemented (T7, [ADR-0039](adr/0039-layer-2-judge-and-calibration-policy.md)).** The judge is built, pinned and hashed, and its scores gate nothing. 48 judge items and 74 replies / 16 pairs are hand-labelled **by the AI session that wrote the harness**: an author-labelled rehearsal that counts for nothing towards the gate (the gate also requires `labeller_kind: human` under the owner's handle). No judge credential has been available, so the judge has scored nothing (`skipped:no-credential`); what is computed offline is the rule screens' and the reply analyser's agreement with the same labels (`eval calibrate`).
- **What would close it.** ≥ 40 human labels (the starting thresholds, taken from the reference implementation: ≥ 40 labels, ≥ 8 items, κ ≥ 0.6) under a named human handle in `evals/labels/judge.yaml` with `labeller_kind: human`, `calibration.owner_handle` set in `evals/rubrics/judge.yaml`, and a keyed run that computes κ.

## OP-12. Receipt codes: access without a list of records

- **Why it matters.** A person cannot list their own records (there is no link), so "access" rights depend on a code the person must have kept
  ([legal §2](legal/CONSIDERATIONS.md)). A lost code means the record cannot be deleted by its author.
- **What we do now.** The server returns the code once (Implemented, T5); the warning and any receipt file are the client's job (Implemented: the web page, T9; the CLI shows the code once and `--save-receipt` writes an optional private file, T11, [ADR-0061](adr/0061-cli-receipt-handling.md)). A `204` from the
  deletion endpoint does not confirm a record existed ([ADR-0029](adr/0029-receipt-deletion-semantics.md)), so a person who kept the wrong code is not told.
- **What would close it.** An *optional*, client-side-only receipt store (the CLI writes encrypted receipts to local disk; the web app offers a
  downloadable receipt file) so the server never learns the link, and a lawyer's view on whether that satisfies the rights it is meant to serve.

## OP-13. Storage-level correlation between the ledger and the records

- **Why it matters.** No column links a ledger entry to a record, every key is random and the only stored time is a week bucket, but rows written in one transaction share a
  PostgreSQL transaction id and sit next to each other in the heap. Anyone with direct file or system-column access, or a physical backup, can pair them
  ([ADR-0027](adr/0027-store-time-buckets-and-one-transaction.md); [threat model T-08](security/THREAT-MODEL.md)).
- **What we do now.** Nothing beyond random keys and coarse buckets; it is documented as a residual risk. Separate transactions would not help on a quiet system.
- **What would close it.** The ledger in its own database with its own role, written through a queue with batching and delay, so that its commits are not adjacent to any record's;
  at the cost of the atomic guarantee that currently prevents orphan states. Not chosen; needs an ADR and an operations story.

## OP-14. One submission per employer is time-limited by the ledger window

- **Why it matters.** The ledger is purged after a window (default 365 days) to limit the "this account wrote about that employer" surface, which also ends duplicate suppression: after it
  the same account can add a second record about the same employer while the first (kept up to 24 months) is still in the aggregates
  ([ADR-0028](adr/0028-submission-ledger-hmac-rotation-and-window.md); [threat model T-10](security/THREAT-MODEL.md)).
- **What we do now.** A configurable window and an honest statement. The default is an assumption, not a measurement.
- **What would close it.** Either a window at least as long as the record age (more exposure), or an aggregate that counts accounts rather than records (needs a link the design refuses).

## OP-15. Anonymous receipt deletion behind the BFF shares one rate-limit key

- **Why it matters.** The interview-service limits receipt deletion per client address (default 6 a minute) and keys on the socket address unless `Submission:ClientIpHeader`
  names a forwarded header ([ADR-0029](adr/0029-receipt-deletion-semantics.md)). Every web request reaches it from the web server, so for people using the portal the
  per-client window behaves as one budget for everyone, and one person's retries can lock others out for a minute. Found by T9 ([ADR-0049](adr/0049-anonymous-receipt-route-and-the-header-contract.md)); not visible locally.
- **What we do now.** Nothing: the BFF does not forward the visitor's address, because that puts an IP into a second service for a limiter key, which is a privacy choice for the operator.
- **What would close it.** A deployment ADR that decides between forwarding a client-address header (and configuring `Submission:ClientIpHeader` to read only that header from the BFF's network)
  and accepting the shared budget with a larger global allowance; plus a load test of the chosen setting.

## OP-16. Manual accessibility pass

- **Why it matters.** The browser suite runs axe-core (WCAG 2.0/2.1 A and AA rules) on every page and state, which finds a subset of problems. It cannot judge reading order, whether
  the copy is understandable, focus order across a whole flow, or what a screen reader announces ([ADR-0051](adr/0051-message-catalog-accessibility-gate-and-stub-contract.md)).
- **What we do now.** The automated floor, a keyboard-only login test, a 320 px overflow test, visible focus, a skip link, `role="alert"` for errors.
- **What would close it.** A person running the flows with a screen reader and keyboard only, at 200% zoom and in forced-colours mode, recorded in the repository.

## OP-17. Two-factor sign-in has been tested only against the stub

- **Why it matters.** The BFF's second step relies on authservice's `2fa/login` contract as read from its source (ADR-0050), including telling a wrong code, a dead challenge and a lockout apart by the
  text of a `401`. The browser suite runs against a stub that mirrors that source; no session here could run the real image.
- **What we do now.** Unit tests pin the three texts; the stub carries a contract note.
- **What would close it.** The full-stack journey against the AppHost with a two-factor account enrolled through authservice (a later e2e layer).

## OP-18. Mode A: host fidelity, the opening text, and the unverified Claude run

- **Why it matters.** In mode A the host model, not this project, conducts the interview. The server checks the record (schema, PII re-scan, AI-disclosure flag, size, one per employer) but cannot see whether consent was
  obtained or withdrawn, whether the AI disclosure was said, whether questions were neutral, or whether quotes are verbatim ([mcp.md](architecture/mcp.md)). The protocol's opening says "the full conversation is not stored", which is true of this
  service and not of the user's AI provider; the prompt adds a fixed note ([ADR-0045](adr/0045-mode-a-host-fidelity-and-opening-note.md)) whose wording has had no legal review. Nobody has run a real Claude client against this server,
  so the connector flow, the prompt's discoverability to users and the host's adherence to the instructions are all **unverified**.
- **What we do now.** Server-side validation, the confirm-before-submit instruction, a pinned and reviewed contract, an operator runbook that says what was and was not verified, and the README/connect copy that says mode A is the weakest of the three modes.
- **What would close it.** (1) A live run against Claude with the real authservice image and two tunnels, recorded with date and versions. (2) The T7 harness running the personas against mode A hosts and reporting the same metrics as mode B.
  (3) A lawyer's reading of the opening plus note. (4) A server-observable signal that is not the transcript (for example the interview's own turn and duration bands, already in the record) compared with what hosts report.

## OP-19. K is a convention, and small batches expose small differences

- **Why it matters.** K = 5 is the brief's number, not a measured privacy level. Three limits of any k-threshold remain after T10: (1) an adversary who adds one record of their own to an employer with k - 1 others sees
  the cell appear, and "everything minus mine" is exactly those k - 1 people; with *m* accounts it is k - m, and the ledger limits one submission per employer per account, not the number of accounts ([OP-1](#op-1-real-employment-verification),
  [T-10](security/THREAT-MODEL.md)); (2) an adversary who knows who else submitted can eliminate; (3) a batch in which only a few known people submitted exposes their joint contribution to the cells they touch.
- **What we do now.** The clean-partition rule bounds (1) at k - 1 for one account and no lower, proved exhaustively on small partitions and by property tests, with the boundary stated as a test
  (`The_known_boundary_...`); batches default to a day and cannot be shorter than an hour; k is configurable and cannot be below 3; whole cuts are withheld rather than partly shown.
- **What would close it.** Verification that makes an account cost something (OP-1); a minimum number of *changes* per batch before a cell is republished (this conflicts with deleting a record "at the next batch", so it needs
  a decision about erasure); noise addition evaluated against simulated re-identification, if the project later wants a formal guarantee instead of a convention; an employer-size floor from a registry ([OP-2](#op-2-employer-registry-and-identity)).

## OP-20. Homogeneous cells are shown

- **Why it matters.** A cell where everyone gave the same rating is displayed (with a wide interval, never a point): a person known to be in the cell has a known rating. That is the homogeneity limit of k-anonymity (the l-diversity gap).
- **What we do now.** Nothing suppresses it, on purpose: suppressing unanimous cells would show only polarised employers, and the pattern of suppression would itself tell (OP-21). The interval is wide at small n; the copy contract says what the
  numbers describe.
- **What would close it.** A diversity rule evaluated for its cost in coverage, or showing only cells whose spread is above a floor, with the bias that introduces written down.

## OP-21. What is withheld is itself a signal

- **Why it matters.** A withheld cut says some band in it has between 1 and k - 1 ratings, or that a group left out of the band does; an `insufficient_data` topic says fewer than k people rated it. Which band, and how many, are not said.
- **What we do now.** Statuses are a fixed vocabulary; the response always has six topics and three cuts per displayable topic; no count of withheld cells is returned, logged or emitted as a metric; an employer below k and an unknown one get
  byte-identical answers.
- **What would close it.** Publishing every cut in a fixed shape regardless of what it hides (not possible without noise), or recoding bands so withholding is rarer ([OP-22](#op-22-clean-partitions-withhold-more-than-a-textbook-rule-would)).

## OP-22. Clean partitions withhold more than a textbook rule would

- **Why it matters.** A clean partition is withheld whole when any band holds 1 to k - 1 ratings, so at a mid-sized employer a single small band (a tenure band of three people) removes that topic's whole tenure cut. Distribution and
  verification breakdowns appear only from about 3k ratings. The cuts will often be empty for small employers, which weakens what the product says about *why* a topic is rated as it is.
- **What we do now.** The textbook rule would show more and is unsafe across two snapshots ([ADR-0053](adr/0053-disclosure-control-clean-partitions-and-k-per-cell.md)); the loss is chosen, documented and visible in the demo data.
- **What would close it.** Recoding (merging adjacent bands such as `lt_6m` and `6m_1y` when one is small), which changes the wire vocabulary and needs the versioning process of ADR-0009; evidence needed: how many cuts are withheld on a population shaped like the
  expected users. **Trigger:** a measured share of withheld cuts that makes them useless.

## OP-23. CLI secrets are protected by discipline, not by the platform

- **Why it matters.** The ticket (single use, minutes) and the receipt code (the only deletion key) pass through a user-space program. The CLI keeps them out of arguments, files, logs and error text (canary-tested) and in a type that does not print,
  but a .NET string cannot be wiped, an exported environment variable is readable by the same user through the process table, a crash dump can hold a copy, the terminal's scrollback keeps the receipt code that is shown once, and the hidden prompt and the
  `0600` file mode were exercised only on Linux ([ADR-0059](adr/0059-cli-secrets-handling.md), [ADR-0061](adr/0061-cli-receipt-handling.md)).
- **What we do now.** Say so (the messages, the ADRs); prefer the prompt and stdin over the environment in the docs; the receipt file is optional, private on Unix and never overwritten.
- **What would close it.** An OS keychain-backed receipt store (optional, local only), a Windows/macOS run of the hidden prompt and file-permission tests in CI, and, for the ticket, nothing short of public-client support in authservice (OP-7), which makes tickets unnecessary.

## OP-24. A submission can end with an unknown outcome and a lost receipt

- **Why it matters.** If the connection breaks or times out after the request left, the server may have stored the record and issued a receipt the person never saw. The CLI cannot retry safely (the ticket may be spent; a repeat would be `INTERVIEW_ID_TAKEN`), and
  the server by design cannot re-issue or look up a receipt ([ADR-0029](adr/0029-receipt-deletion-semantics.md)). The record then exists and its author cannot delete it ([OP-12](#op-12-receipt-codes-access-without-a-list-of-records)).
- **What we do now.** One attempt only, after the connection is open; the message says the outcome is unknown and what a repeat would do ([ADR-0058](adr/0058-cli-http-client-hygiene.md)). The window is small (one request of at most 160 KiB, 30 s).
- **What would close it.** A client-chosen idempotency token that makes the server return the same receipt on a repeat. That needs a server change (T5) and a decision about what it may store, since it would be a second secret tied to a record; not attempted here.

## OP-25. CLI submission has not run over real TLS, on Windows or macOS, or against a deployment

- **Why it matters.** Every CLI test runs over loopback `http` (a real socket) or an in-process handler against the real service. The `https` requirement, certificate validation, and proxy behaviour rest on the address check and on leaving the platform's TLS defaults untouched (asserted),
  not on a handshake test. Nothing is deployed.
- **What we do now.** State it ([cli-submission](architecture/cli-submission.md#tests-and-what-they-cannot-show)); the in-process end-to-end test pins the contract with the real service.
- **What would close it.** A test with a throwaway TLS certificate (accepted as an untrusted-certificate failure, and trusted in a second run), the CLI binaries exercised on the other two platforms, and a first smoke run against a staging deployment once there is one.


## OP-26. Readers may still compare employers by eye, and nobody has tested comprehension

- **Why it matters.** The pages show no rank, score or comparison and say "two figures whose intervals overlap are not different", but a reader can open two employers and compare two means. The interval is shown and worded ("a wide interval means early, not wrong"), yet
  there has been **no usability test**: nobody has checked that a reader understands what an interval, reliability or coverage means, or that a `low` coverage figure speaks for few respondents. An unread caveat does not protect anyone ([metric-ethics](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md) §3).
- **What we do now.** The numbers never appear without interval, n, reliability and coverage; there is no view that puts two employers or bands side by side; the contract copy is applied verbatim; the list says it does not rank.
- **What would close it.** A small comprehension test with people who are not the authors (do they read a 5-rating figure as a verdict? do they compare two employers?), and changes to the copy and layout from what it shows. **Trigger:** before any real data is shown.

## OP-27. A browser may keep Signals answers after sign-out, until the batch ends

- **Why it matters.** The two Signals reads carry `private, max-age` to the next batch so that a visit does not cost a request ([ADR-0068](adr/0068-signals-through-the-bff-caching-validators-and-429.md)). A browser may therefore still hold an answer after the account signs out, and serve it to whoever uses that profile.
- **What we do now.** Only aggregates identical for every account are stored (nothing per account, nothing below k); `private` and `Vary: Cookie` keep shared caches out; pages are `no-store`; the lifetime is clamped to 7 days and is the time to the next batch.
- **What would close it.** `no-store` on these reads (every visit then costs a request against the per-account budget), or a cache partition keyed to the session. **Trigger:** the endpoints being opened to anonymous readers, or aggregates becoming sensitive per account.

## OP-28. The Signals pages have only met the stub

- **Why it matters.** The browser suite runs against a stub that mirrors the Signals contract and carries a note saying it must change with the contract; a machine does not enforce the note. The pages were not run against the real interview-service (with `Signals:Demo:Mode=Seed`) in the task that built them,
  so a drift between the stub and the service (a header, a status, an empty list) would show only there. The accessibility check is axe (a subset), with no screen-reader pass ([OP-16](#op-16-manual-accessibility-pass) covers the new pages too).
- **What we do now.** The stub is built from the contract file and the endpoint source; the web reader refuses any response that is not exactly the contract, so drift fails closed ("could not be loaded"), never as a wrong number.
- **What would close it.** A full-stack journey against the AppHost with the demo seed (sign in, list, an employer, a 304 on revisit), run in CI or by hand and recorded; ideally a contract test that replays the real service's responses through the web reader.

## OP-29. Fail-closed over-masking of capitalised topic words is bounded only by a list

- **Why it matters.** In fail-closed mode every unknown capitalised word in mid-sentence is masked, because that is how a stray name is caught. Interview topics are capitalised in lists and headings ("Autonomy and Mentoring"); the ones on the stop list are left alone ([ADR-0063](adr/0063-pii-detector-rule-cost-and-over-masking.md)), the rest are masked as `[PERSON]`, which damages a quote but leaks nothing.
- **What we do now.** A stop list of common topic nouns in English and Polish; a conjunction is not treated specially (the "Mark and Pay" shape masks the name and leaves the topic). Measured on the over-masking corpus, which was written for the reported cases and is therefore not independent evidence.
- **What would close it.** A larger corpus of real-shaped interview text with topic headings, and a measurement of how often a topic list is masked in practice; then either a longer list or a rule on list shape (capitalised words joined by "and", "i", commas) with its recall cost measured. **Trigger:** a reviewer or the person's own preview finds topic words masked often enough to hurt the quotes.

## OP-30. Obfuscated-email spellings beyond the bracketed forms are not detected

- **Why it matters.** Only `name[at]host[dot]tld`, the round and curly `at` markers, and a plain `.` before the top-level part are recognised. `name{at}host{dot}tld`, "name at host dot com" written in words, and spellings in other languages are not ([ADR-0063](adr/0063-pii-detector-rule-cost-and-over-masking.md) kept the rule's coverage exactly as it was).
- **What we do now.** Listed as a known limit in the detector documentation; the interview agent is instructed not to ask for contact data and the person reviews the record before it is sent.
- **What would close it.** New spellings added to the marker-anchored rule together with corpus lines (`Corpus/email-obfuscated.txt`) and a cost measurement; words-for-symbols forms need a precision measurement first, since "at" and "dot" are ordinary words.
