# 0048. One-time secrets stay in page memory; nothing sensitive is cached; CSRF on state-changing routes

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: threat model T-09, T-12, T-15, `security-review` §5 (a secret is not a GUID; where secrets must not go),
  `frontend-bff` §3 (cookies), `identity-and-accounts`.

## Context

Two bearer secrets pass through the browser: a **submission ticket** (minted by the signed-in account, [ADR-0030](0030-submission-tickets-for-the-cli.md))
and a **receipt code** (typed by the user, [ADR-0029](0029-receipt-deletion-semantics.md)). Each is a credential for one action. Where a
secret can end up in a browser is a long list: web storage, cookies, the URL and history, the HTTP cache and back/forward cache, form autofill,
the clipboard, logs and analytics.

## Decision

- **A ticket lives in React state of one page and nowhere else.** Not in `localStorage`, `sessionStorage`, IndexedDB, a cookie or a URL; no analytics
  exist. It is cleared by: expiry (a one-second timer against the server's `expiresAt`), the "Clear it now" button, leaving the page (unmount),
  `pagehide`, and `pageshow` with `persisted` (back/forward cache restore). A reload starts from the idle state, so it cannot show the ticket
  again. The mint answer is `Cache-Control: no-store` so the HTTP cache cannot hold it. The copy button writes to the clipboard on an explicit click;
  the clipboard is the browser's and the page says so.
- **A receipt code is typed into an input that has no `name`** (a native form submission could not put it in a query string), opts out of autofill
  (`autocomplete="off"`, `autocorrect`, `spellcheck=false`), is **emptied the moment the request is made**, and is sent only in the `X-Receipt-Code`
  header. The page never renders it back. The form also carries `method="post"` as defence in depth.
- **Password forms carry `method="post"`** (login, register, delete account). Found while testing: a submit before hydration is a native GET
  that put the email and password in the URL; with `method="post"` the worst case is a refused POST.
- **`Cache-Control: no-store` on every page and API answer** except `/healthz` and `/api/config`, set by the edge gate on the response, plus
  explicitly by the routes that carry a ticket, receipt outcome or account data (the BFF's upstream helper sets it on every proxied answer and
  does not pass the backend's own `cache-control` through).
- **CSRF, two layers.** (1) Session cookies are `SameSite=Strict` and HttpOnly (existing; asserted). (2) The edge gate refuses, before any handler
  runs and before the session is read, a state-changing request (`POST`, `PUT`, `PATCH`, `DELETE`) to `/api/*` whose `Sec-Fetch-Site` is not
  `same-origin`/`none` or whose `Origin` host differs from the request host (`X-Forwarded-Host` first, for a platform proxy): `403 {"error":"cross_origin_request"}`.
  A request with neither header (curl, server to server) is allowed: it cannot borrow a browser's cookies. The check covers the anonymous
  receipt route as well, so a hostile page cannot spend a victim's per-client rate limit.
- No ticket, receipt code, token or password is logged: the BFF routes read them, forward them and drop them; the one `console.error` in the proxy
  names a base URL and a status only.

## Consequences

- The ticket cannot be recovered after a reload: by design (the page says so); the user makes another (3 live, 10 an hour per account).
- Residual: a malicious browser extension or an XSS defeats page memory too; the CSP ([ADR-0047](0047-nonce-csp-and-style-policy.md)) is the control for the second.
  The clipboard keeps the ticket until the user copies something else.
- `Sec-Fetch-Site` is absent in old browsers; then only `Origin` and SameSite protect. Acceptable: `SameSite=Strict` is the primary control.
- Trigger to revisit: a framework-level CSRF token if a non-cookie auth method is ever added.
