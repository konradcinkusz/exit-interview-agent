# 0019. Brief amendments from the T3 legal/privacy review

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: P14 (documentation records reasoning and stays true), `repo-baseline`,
  `PROJECT-BRIEF.md` §9.7 (no invented claims about the law or provider terms), [ADR-0018](0018-records-are-treated-as-personal-data.md).

## Context

The T3 documentation review ([CONSIDERATIONS §7](../legal/CONSIDERATIONS.md#7-proposed-changes-to-the-brief-and-readme-wording-adopted-by-adr-0019),
[threat model](../security/THREAT-MODEL.md), [privacy design](../privacy/DESIGN.md)) proposed six changes to the binding
brief. The brief is edited only via ADR (its own header). The orchestrator, on the owner's delegated authority
(brief §9.1), adopted proposals (a)-(f). Nothing here is legal advice, and none of it is implemented: the
threat-model rows move from *Proposal* to **Decided (Tn)**, never to *Implemented*.

## Decision

- **(a) Provider wording (brief §2).** The subscription-token bullet states what Anthropic's own pages say (CONSIDERATIONS §1
  rows 1, 1b, read 2026-10-05): third parties may not offer Claude.ai login or route requests through Free/Pro/Max
  credentials. GitHub Copilot as a backend is described as "could not be verified" (row 5), never "prohibited". The decision
  stands: neither is supported.
- **(b) Coarse timestamps (brief §6).** The record carries at most an ISO-week bucket; the submission ledger stores a day
  or no timestamp. Row timing must not be a join key between ledger, ticket redemption and record (T-08(c), T-09).
- **(c) K per displayed cell (brief §6).** K applies to every cell shown, publication is batched, and there are no
  cross-product breakdowns, so small cells cannot be recovered by differencing (T-01).
- **(d) AI disclosure and no affect inference (brief §6).** The interviewer says at the start that it is an AI, recorded as
  `aiDisclosed` in the record metadata; the extractor schema has no emotion, sentiment or affect field (CONSIDERATIONS §5:
  Art. 50 transparency, workplace emotion recognition; metric-ethics §4).
- **(e) Retention (brief §6).** Records have an operator-configurable maximum age; a record older than it is purged. **Default
  24 months**, chosen as a placeholder long enough for aggregates to be meaningful and short enough to bound exposure;
  it is **an assumption, not a legal determination**, and counsel may change it. The ledger window (brief §6) is
  independent and must be at least as long as the one-per-employer rule needs.
- **(f) Vocabulary.** Records are personal data (ADR-0018) and are never called "anonymous" or "anonymised" in README, docs,
  UI or the brief. Aggregates may be called "aggregated". A sweep of the repository found no remaining violation.
- **Backlog additions (brief §10).** T12 adds CSP, X-Frame-Options, Referrer-Policy and Permissions-Policy to
  `web/app/next.config.ts` (T-06) and re-verifies the unverified sources of CONSIDERATIONS §6 when egress allows; T5 includes a
  canary-in-logs test (T-15); T2 includes negative cross-scheme tests (T-17).

## Consequences

Contracts gain constraints before they exist: T1 (week-bucket field, `aiDisclosed`, no affect field, retention
setting), T5 (day-or-none ledger timestamp, purge job), T10 (per-cell K, batching), T4/T7 (disclosure scenario). Costs:
coarser timing hurts debugging and ordering; purge by age loses old signals. Trigger for revisiting: counsel's opinion
on retention or on ADR-0018, or a threat-model change to T-01/T-08.
