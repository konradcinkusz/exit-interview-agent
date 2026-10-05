# 0068. Signals through the BFF: private caching to the next batch, validators, and no retry loops

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (deviates from, with a register row): [ADR-0048](0048-one-time-secrets-no-store-and-csrf.md) "`no-store` on every page and API answer except two probes"; `frontend-bff` §5 (pass through status, body and relevant headers);
  `service-api-patterns` §1; [ADR-0056](0056-signals-api-caching-rate-limits-and-demo-data.md).

## Context

The service sends `Cache-Control: private, max-age=<to the next batch>`, a weak ETag, `Last-Modified`, `Vary: Authorization` and answers `304` to `If-None-Match` (ADR-0056). The BFF used to make every answer `no-store` and copy only
four headers, so the browser could never revalidate: every visit would cost a full response, and a scraper-shaped page that re-fetched on each navigation would spend the per-account rate budget (30 a minute) for nothing. Separately,
`callBackend` treated every 3xx as "a redirect between services" and answered 502, which would have turned each `304` into an error.

## Decision

1. **The two Signals reads are cacheable, nothing else is.** `routeFor` marks exactly `GET v1/signals/employers` and `GET v1/signals/employers/{ref}` as `signalsRead`. For those, and only for statuses `200`, `304` and `404`,
   the BFF passes `ETag` and `Last-Modified` and sets `Cache-Control: private, max-age=N` **only if the service sent exactly that shape** (N clamped to 7 days, the longest publication interval). Anything else (`public`, a missing header,
   an unknown shape) becomes `no-store`. **The BFF never makes a response `public` and never invents a lifetime.** `Vary: Cookie` is added: at this origin the session is the cookie (the bearer is injected here), so a stored answer must not be
   reused across sessions by a shared cache.
2. **The 404 is cacheable for the same time**: it is the uniform "not shown" answer, byte-identical for an unknown employer and one below k (R10), and it cannot change before the next batch either.
3. **`If-None-Match` is forwarded** for these reads only (the route copies it; nothing else from the browser is forwarded, as before). `304` is not a redirect: it is passed on with no body, with its validators and lifetime (it renews the browser's copy).
4. **Everything else stays `no-store`**: other API paths, `429` (with `Retry-After` passed on), every error, every page, every answer that carries rotated session cookies (forced `no-store`), and every answer the edge gate itself gives (redirect, 401).
   The edge gate (`proxy.ts`) no longer overwrites the header for the two reads **only when** the request passed the gate; the route sets its own, and every error path of the route now sets `no-store` itself.
5. **The pages' HTML stays `no-store`.** It carries no data. The data is fetched by the page with the browser's default cache mode, so the browser's own HTTP cache does the work: within the batch, no request (tested: a second visit costs zero
   requests to the backend); after it, a conditional request that costs a `304`.
6. **429**: the page shows the wait the service asked for (`Retry-After`, seconds, clamped to an hour, 30 s if absent or unreadable), keeps "Try again" disabled until then, and **starts no request by itself**. There is no polling, no timer that fetches, no retry loop.
   Tested against the stub, with a mutant that retries by itself.
7. **After sign-out** a browser may still hold a stored answer until its `max-age` ends. The answers are aggregates identical for every account, readable only by whoever uses that browser profile; `Vary: Cookie` keeps shared caches honest.
   Recorded as [OP-27](../OPEN-PROBLEMS.md) rather than hidden.

## Consequences

- One rule is now "no-store except the probes and the two Signals reads"; the register row says so. The test suite pins both sides: the reads carry validators and `private, max-age`, every other path (including a POST to the same path, and the pages) stays `no-store`.
- A bug fixed on the way: a backend `304` would have been a `502`. Found by the new unit test before any browser run.
- If the service ever sends a different cache-control shape, the BFF degrades to `no-store`; the page keeps working, it just stops saving requests.
- Trigger to revisit: opening the endpoints to anonymous readers (then `public` caching at a CDN may be wanted, which is a separate decision with its own threat-model entry).
