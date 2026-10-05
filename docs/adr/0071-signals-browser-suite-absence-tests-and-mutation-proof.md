# 0071. Signals tests: absence is asserted, the stub mirrors the contract, and the guards were shown to fail

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `testing-strategy` (a test that cannot fail is worse than none), `e2e-acceptance-testing`, `metric-ethics` §1; [ADR-0051](0051-message-catalog-accessibility-gate-and-stub-contract.md) (the stub must move with the contract).

## Context

The anti-goals of the Signals view are absences (no ranking, no sort, no score, no number below k). An absence is tested by looking for the thing and failing if found; a green test that never looked proves nothing.

## Decision

1. **Layers.** Vitest for the reader (shape, closed vocabularies, what it drops), the failure mapping, the catalog templates, the pure views rendered to markup (`react-dom/server`, no new dependency) and the BFF passthrough
   (`callBackend` with a mocked `fetch`, the edge gate's caching rule). Playwright against the production artifact and the stub for the journey: pages, paging, states, caching, 429, gating, security, accessibility.
2. **The stub mirrors the Signals contract exactly** (status codes, shapes, headers, `insufficient_data`, a suppressed cut with no cells, a `none` band, the uniform 404, 400, 429 with `Retry-After`, `304`, the empty snapshot) and carries the contract note; it has a
   synthetic employer whose band, topic and group strings are markup, to prove nothing from the API is interpreted.
3. **Absence tests**: no `button`, `select`, `input`, `textarea`, `form`, search role or `aria-sort` on the list or employer page; no test id or text that says rank, score, average, percentile, trend, best, worst, compare, better, worse; no arrow glyph;
   no digit anywhere in an `insufficient_data` topic; no band named in a suppressed cut; no "unknown" in the not-shown page; no inline style.
4. **Mutants run, and killed** (13 in Vitest, 2 at browser level; the list is in the PR and the signals module note): a count printed for an insufficient topic; a sort control; the list re-ordered by the client; the reader keeping `overall` of an insufficient topic;
   the reader keeping a suppressed cut's cells; n dropped from the stat line; `public` let through; a `304` treated as a redirect; `Retry-After` and `ETag` not passed; the edge gate leaving every path to the route; the reference guard loosened; the invalid-reference answer made
   distinguishable; the route not forwarding `If-None-Match`; the page retrying a 429 by itself.
5. **Accessibility**: every new page and state in the axe gate, light and dark (list, employer, nothing to show, 429, empty); keyboard (links only, no traps); a 320 px overflow check on the list and two employers.

## Consequences

- What the suite cannot show: that a screen reader announces the tables and states well, that readers understand intervals (they may still compare employers by eye; [OP-26](../OPEN-PROBLEMS.md)), and anything about the real backend (the stub only; [OP-28](../OPEN-PROBLEMS.md)).
- The browser tests share one stub process; state is per account (a test signs in a fresh account and configures only that account), so they stay parallel-safe.
