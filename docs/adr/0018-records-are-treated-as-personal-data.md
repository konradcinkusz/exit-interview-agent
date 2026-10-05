# 0018. Records are treated as personal data, not as anonymous

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `PROJECT-BRIEF.md` §6 (privacy design), `metric-ethics` §5 (unit of evaluation),
  `security-review` §3 (an acknowledged residual is a decision).

## Context

The brief removes the user id from the record and keeps a separate, keyed ledger. That makes linking a record to an
account hard. It does not make the record anonymous: it carries verbatim free-text quotes, an employer, tenure and
role bands and a pseudonymous interview id, and a former colleague can recognise a person from a distinctive
episode or phrasing. Whether such a record is legally anonymous is a legal question this repository cannot answer
(see [CONSIDERATIONS](../legal/CONSIDERATIONS.md)); a design that *assumes* anonymity fails badly if the assumption
is wrong.

## Decision

Design, document and test every component **as if records were personal data held by a controller** (at best
pseudonymous):

- no component, document or UI string says the records are "anonymous"; the permitted word is "unlinked from your account";
- every control that would be required for personal data is in scope regardless of the legal outcome: deletion by
  receipt code, purpose limitation (no ranking, no per-person view), retention limits, no content in logs, a lawful-basis
  and DPIA question list before any real data (CONSIDERATIONS §2, §4);
- the privacy design and threat model name re-identification through small groups and quote style as primary threats;
- the "no real interviews" non-goal is therefore also a legal posture, not only a product decision.

## Consequences

Some controls cost UX (a lost receipt code cannot be recovered; account deletion cannot reach records). Marketing
claims are narrower. If counsel later concludes records are anonymous, controls can be relaxed deliberately; the
reverse (discovering they were personal data after launch) would be far more expensive. Trigger for revisiting: a
written legal opinion on this exact schema.
