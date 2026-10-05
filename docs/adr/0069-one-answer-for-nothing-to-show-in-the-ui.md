# 0069. One answer for "nothing to show" in the web UI, and the reference is validated before any request

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `security-review` (one answer for "unknown" and "not allowed"); [ADR-0056](0056-signals-api-caching-rate-limits-and-demo-data.md) §4; AGGREGATION R10; threat model T-01.

## Context

The API answers an unknown employer and one with too few records with a byte-identical 404, so the response is not an oracle for small groups. A UI can rebuild the oracle in one sentence: "unknown employer" for one and "not enough
responses" for the other, or a different layout, a different timing, or a request that is made for one and not for the other.

## Decision

1. **One state, `not_found`**, for `404 SIGNALS_EMPLOYER_NOT_FOUND`, for `400 SIGNALS_INVALID_EMPLOYER_REF` and for a reference the page itself rejects. The page says "There is nothing to show for this employer." and one detail sentence that names both
   possible reasons and says the page cannot tell which. It never says "unknown", "not found" or "does not exist" (asserted).
2. **The reference is checked against the API's pattern before any request** (`lib/signals-ref.ts`: lower-case words joined by single hyphens, 3 to 64 characters), in the page and again in the BFF route, which answers the API's own `400` problem body without calling the backend.
   A string that is not a reference therefore never reaches the network from this page, and the URL path is only ever built from a validated reference through `encodeURIComponent`.
3. **A malformed reference looks like the others** to the reader (the same page), while the BFF's `400` is available to a script that calls it directly, exactly as the API's would be.
4. **The employer list's links** are built only for strings that match the pattern; anything else from the API is shown as text.

## Consequences

- The page makes no request for a malformed reference, so request count differs from the other two cases; this is observable only by someone who already controls the input and learns nothing about any employer.
- Both "not shown" cases are cached by the browser for the batch like any 404 ([ADR-0068](0068-signals-through-the-bff-caching-validators-and-429.md)).
