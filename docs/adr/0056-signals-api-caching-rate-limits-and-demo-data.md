# 0056. Signals API: account policy, caching, rate limits, one answer for "not shown", and demo data

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `service-api-patterns` §1 (rate limiting), §2 (the authorization triad), §4 (clamp every list), P8, `security-review` (deny by default, one answer for "unknown" and "not allowed"),
  `demo-data-and-seeding` §1-§4, threat model T-01, T-18.

## Context

The endpoints expose aggregates to readers. Repeated queries cannot reveal more than the snapshot holds (it changes once per period), but they can enumerate employers and are the
cheapest scraper. A 404 that differs for "unknown" and "too few records" would be an oracle for small groups. Demo data must not become a way to put fabricated reviews in a store people use.

## Decision

1. **Two read endpoints**, `GET /api/v1/signals/employers` (alphabetical, paged, clamped by `ApiExtensions.ClampPage`) and `GET /api/v1/signals/employers/{employerRef}`, on the authenticated group: policy
   `account` (a web token). **Opening them to anonymous readers is a later, separate decision** with its own threat-model entry and its own limits; the endpoint-matrix test pins the anonymous list so a change shows in review.
   No endpoint ranks, searches, filters or sorts; no parameter named like one exists (test).
2. **Rate limit** per account (the same key as the kernel's policies: the `sub` claim, then the remote address): `signals` policy, `Signals:RequestsPerMinute` (default 30), no queue, one budget for both endpoints, the uniform
   429 body. This stops a naive scraper; it does not stop one with many accounts (a snapshot only changes once per period, so extra queries add nothing, and the real control is batching).
3. **Caching consistent with batching:** `Cache-Control: private, max-age=<seconds until the next batch, at least 60>`, a weak `ETag` per snapshot and resource, `Last-Modified` = the batch start, `Vary: Authorization`,
   and `304` on `If-None-Match`. Responses carry only displayable data, so a cache holds nothing that was below k.
4. **One answer for "not shown".** An employer that is unknown and one that has no displayable cell both get the same `404` body (`SIGNALS_EMPLOYER_NOT_FOUND`) and the same headers: neither has a row, by construction, so
   the lookup is identical. A malformed reference is a `400` (`SIGNALS_INVALID_EMPLOYER_REF`). Errors are problem details with a stable `code`, a fixed title and nothing derived from the request.
5. **A fixed shape.** Six topics always, three cuts per displayable topic always; what is withheld says `insufficient_data` or `suppressed`, never a number, so response size depends only on what is displayed.
6. **Telemetry and the URL.** The employer reference is in the request path, so it appears in request logs and in `url.path` of traces, like any URL. It is a public identifier (the same string the list returns), not an
   attribute of a submitter; this service adds no account identifier to a span or log line (scopes are off and the principal drops the email), so a trace says that *a* request read employer X, not who. Recorded in the threat model as a deliberate scope
   decision; if reading an employer must not be traceable at all, the reference moves to a POST body.
7. **Demo data** (`Signals:Demo:Mode` = `Seed` or `Remove`, default `Off`): synthetic records for six invented employers under the reserved `demo-` prefix, generated from a seed (default 42), submitted **through
   `SubmissionService`** as synthetic accounts (validation, PII re-scan, ledger, receipt: the real door). Reset removes the prefix from records, receipts, the ledger entries of those synthetic accounts and the snapshot (it republishes).
   **Development only:** any other mode outside Development stops the service at startup, because synthetic records in a store real people use are indistinguishable from fabricated reviews. While active it is a
   degraded integration on `/health` and in the banner (`signals-demo-data`), on purpose.

## Consequences

- The response shape is public contract (`ExitInterviewAgent.Contracts`); the module's own view types are separate and mapped in one place, so the stored JSON can evolve with the rule version without breaking clients.
- A web view built on this must follow the copy contract in [AGGREGATION §8](../privacy/AGGREGATION.md#8-api-contract-and-ui-copy-contract).
