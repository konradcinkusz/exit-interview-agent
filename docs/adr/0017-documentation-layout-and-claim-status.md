# 0017. Documentation layout and the vocabulary for claim status

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: P14 (documentation records reasoning and stays true), `research-documentation`
  (every number traceable; separate claim from evidence), `PROJECT-BRIEF.md` §9.7 (no invented claims about the market,
  the law or provider terms).

## Context

Several sessions write code in parallel while the privacy, security, legal and evaluation documents describe a design
that is mostly not implemented yet. A document that describes planned behaviour in the present tense is a stale README
in advance (P14's corollary). Legal and provider-terms statements are the highest-risk claims: they are easy to
write from memory and costly if wrong. In the authoring environment of this decision, the egress policy blocked the
statute and regulator hosts (EUR-Lex, EDPB, ICO, UODO), so a part of the legal material could not be read at all.

## Decision

- **Layout.** `docs/privacy/DESIGN.md`, `docs/security/THREAT-MODEL.md`, `docs/legal/CONSIDERATIONS.md`,
  `docs/OPEN-PROBLEMS.md`, `docs/eval/METHODOLOGY.md`. Each links the others and the brief; `docs/` gains no other
  top-level folders for these topics.
- **Status vocabulary for design claims:** *Implemented* (on `main`, with a link to the code), *Planned (Tn)* (specified
  by the brief, owner task named), *Proposal* (this documentation's recommendation, not yet accepted by the owning
  task), *Assumption* (stated, not verified). Never present a *Planned* behaviour as present tense without the tag.
- **Status vocabulary for external statements (provider terms, statutes, regulator guidance):** *Verified* (the primary
  document was fetched and the operative text read; the date and URL are in the row), *Reported by secondary source*,
  *Unverified*. A row is never promoted without reading the primary text, and no position is attributed to a
  provider or regulator that was not read.
- **Legal documents carry the heading "considerations, not legal advice"** and never state a conclusion that only a
  lawyer can reach.
- Documents that are updated when the code lands (threat-model statuses, README Status) are updated in the pull
  request that changes the code.

## Consequences

Reviewers can check any sentence against one of seven statuses. Legal rows stay *Unverified* until someone with
network access to the primary sources reads them; that is a visible gap, not a silent one. The cost is some
repetition of tags, accepted deliberately.
