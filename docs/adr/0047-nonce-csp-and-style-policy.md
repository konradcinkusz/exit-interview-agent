# 0047. Nonce-based Content-Security-Policy, and what the style policy is

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: threat model T-06, `security-review` §4 (header set), `frontend-bff` §4. Closes the "no nonces" known limit
  recorded by T2 ([ADR-0013](0013-bff-session-refresh-rotation-and-consent-gate.md) and the architecture "Known limits").

## Context

T2 shipped the five-header set with a CSP that allowed `'unsafe-inline'` for scripts and styles, because Next emits inline bootstrap
scripts and the app had no per-request nonce. `'unsafe-inline'` for scripts removes most of what a CSP is for. The portal now renders
values a user must not leak (a ticket, a receipt outcome, account data), so the policy has to hold. Facts checked in this repository, by
building the production artifact and loading every page with a `securitypolicyviolation` listener (`tests/e2e/specs/security.spec.ts`):

- Next 16 stamps a nonce on the scripts it renders when the **request** reaches the render with a `Content-Security-Policy` header that
  contains `'nonce-…'`; the page must be rendered per request (a statically prerendered page cannot carry a per-request value).
- The app ships its CSS as same-origin files (`/_next/static/…css`). Nothing in it writes a `<style>` element or a `style=` attribute in
  production, so `style-src 'self'` produces no violation on any page.

## Decision

- **The CSP is built per request** in the edge gate (`web/app/proxy.ts`, `lib/security-headers.ts`): a fresh 128-bit nonce, the policy set on
  the response, and the same policy plus an `x-nonce` header forwarded on the request so Next nonces its own scripts. The remaining four
  headers and two cross-origin headers stay static in `next.config.ts` (`securityHeaders()`).
- **Scripts:** `script-src 'self' 'nonce-<n>'`. No `'unsafe-inline'`, no `'unsafe-eval'` in production. **No `'strict-dynamic'`**, on purpose:
  with it the browser trusts any script element that JavaScript creates, which is what an injected snippet does; the browser suite
  injects an inline `<script>` and an inline event handler and asserts both are refused. Without it, same-origin script files stay allowed
  by `'self'` (Next's chunks); that is the price, and it is acceptable because the origin serves no user-controlled script file.
- **Styles:** `style-src 'self'` in production: **no `'unsafe-inline'`, and no nonce either**, because nothing needs one. The codebase rule that
  keeps it true: no `style=` attribute and no inline `<style>` in application code (asserted by a test that scans the rendered HTML for
  `style="`). A component library that injects styles at runtime would break this and would need its own ADR.
- **Development** (`next dev`) adds `'unsafe-eval'` for scripts and `'unsafe-inline'` for styles, because the dev server injects both
  (hot reload). Production is the only environment the policy is asserted for.
- Everything else as before: `default-src 'self'`, `connect-src 'self'` (the point of the BFF), `img-src 'self' data:`, `font-src 'self'`,
  `frame-ancestors 'none'`, `base-uri 'self'`, `form-action 'self'`, `object-src 'none'`. No third-party origin appears anywhere; a test
  asserts the policy contains no `http(s):` source and that no page requests another origin.
- Every page is dynamic (the root layout reads `headers()`), which also keeps the per-request nonce honest.

## Consequences

- **Residual weaknesses, stated:** (1) `'self'` for scripts trusts every JavaScript file the origin serves; if a route ever echoed
  attacker text as `application/javascript`, it would be an injection path (the API answers JSON with `nosniff`). (2) A CSP is defence in depth: the
  primary control remains encoding at render, and React escapes by default; no `dangerouslySetInnerHTML` is used. (3) The nonce is
  only as good as the proxy: a path excluded from the matcher gets no CSP (only static assets are excluded). (4) `style-src 'self'` does not
  stop CSS-only exfiltration from an attacker who can inject *markup*; there is no user-rendered markup today.
- Pages cannot be statically cached; the product is small and dynamic by nature, so nothing is lost.
- Trigger to revisit: the first component library or analytics script (needs an ADR), or Next adding first-class nonce support for static pages.
