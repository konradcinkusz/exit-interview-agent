# 0067. The Signals pages render what the API returned and derive nothing

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `metric-ethics` §1 (anti-goals enforced by absence), §3 (no number without its confidence), §5; `frontend-bff` §1 (the browser talks to its own origin);
  P11 (anti-corruption at the edge); [AGGREGATION §8](../privacy/AGGREGATION.md#8-api-contract-and-ui-copy-contract); threat model T-01, T-06.

## Context

The Signals API ([ADR-0053](0053-disclosure-control-clean-partitions-and-k-per-cell.md), [ADR-0056](0056-signals-api-caching-rate-limits-and-demo-data.md)) publishes aggregates that are safe *as sent*: a number below k is never in a
response, and neither is a score, a rank or a comparison. A view can still undo that. It can sort a list, colour a mean, add two means "for convenience", round an interval to look tighter, or show how many topics lacked data. Every one of those is
a few lines of client code, and none needs the API's consent. The rule "never derive" has to be something the code cannot easily violate, not a code-review habit.

## Decision

1. **Two pages, one fetch each, through the BFF.** `/signals` (a page of the alphabetical list) and `/signals/[employerRef]` are server-rendered shells (no data, so `no-store` is free) around client components that call
   `/api/proxy/v1/signals/...`. The BFF holds the token ([ADR-0050](0050-two-factor-sign-in-and-account-flows-through-the-bff.md)); the page never sees it. Server components calling the backend directly were rejected: they would
   duplicate the session refresh logic that the catch-all route already has.
2. **A strict reader between the response and the view** (`lib/signals.ts`). It validates the shape and the closed vocabularies, and **drops by construction** what the reader must not see: a topic with `insufficient_data`
   keeps no numbers (the type has none), a `suppressed` cut keeps no cells, a `none` or `suppressed` band keeps no statistics. A response that is not exactly the contract is not rendered at all ("could not be loaded"). A view cannot
   show a withheld number because its input type cannot hold one.
3. **Presentational components with no arithmetic** (`app/signals/views.tsx`): no sum, mean, rounding policy, sort, comparison or percentage. The one calculation is the position of a value on the fixed 1-5 axis of the range bar.
   Numbers are formatted to two decimals (the API's own precision) and nothing else. The list is rendered in the order the API sent; the six topics likewise.
4. **The interval and n are one string.** `"{mean} ({level}% interval {lower}-{upper}), {n} ratings"` is a catalog template filled in one place (`StatLine`); there is no way to render a mean without its interval and n.
   Reliability and coverage are shown wherever a figure is.
5. **The range bar** is decorative (`aria-hidden`), on a fixed 1-5 axis with one neutral hue, and encodes the interval and the mean. It does not encode n (a test renders n = 12 and n = 5000 and requires identical SVG).
   The distribution is a table of the API's counts, not bars: bars would invite reading shares, and shares would be a derived number.
6. **No chart library.** The one drawing is inline SVG; adding a dependency for a rectangle and two lines would be a larger supply-chain surface than the drawing is worth.
7. **Copy.** Every string is in `m.signals` (typed catalog). The copy contract of AGGREGATION §8 is applied verbatim and asserted (unit and browser tests). Where a sentence is per-figure in the contract
   ("A wide interval means early, not wrong."), it is shown for each topic that has a figure, not once at the top.
8. **What is deliberately absent**: controls (no button, select, input or form on the list or the employer page), the words rank, score, average, best, worst, percentile, trend, compare, any arrow glyph, any colour class that implies good or bad.
   The tests assert the absence (see [ADR-0071](0071-signals-browser-suite-absence-tests-and-mutation-proof.md)).
9. **Security.** API strings are React text children only; no `dangerouslySetInnerHTML`; the only URL built from an API string is `/signals/<ref>`, after the reference matches the API's pattern and through `encodeURIComponent`
   (a string that does not match is shown as text, not as a link); the employer reference is never in the page title, and the page stores nothing (no storage, no analytics).

## Consequences

- A reader of the code can check the anti-goals by looking at the types: there is no field for a rank or a withheld number to travel in.
- A new field in the API is not shown until the reader and a view are changed on purpose (and a test of absence is considered). That is the intended friction.
- The reader refuses an unknown vocabulary value rather than printing it: an API that adds a status needs a web change in step, caught by the stub-contract note.
- Deviations from the brief of this task, none from the contract: the navigation entry is the existing gated-link kind (shown to everyone, sends a signed-out visitor to sign in), because the header does not know
  the session ([UI-UX](../ux/UI-UX.md#navigation)); a `none` band inside a published cut says "No ratings." (a zero is public by design, AGGREGATION §3), a band that arrives `suppressed` says it is hidden.
- Trigger to revisit: opening the endpoints to anonymous readers (a new threat-model entry and its own limits); a request for any view that relates two employers.
