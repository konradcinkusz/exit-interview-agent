# 0013. BFF session: single-flight refresh rotation and the consent gate

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `FRONTEND-BFF.md` §3 (HttpOnly cookies, no tokens in the browser),
  `IDENTITY-AND-ACCOUNTS.md` §2 (single-use rotated refresh tokens), §8 (account deletion), §9 (versioned consent).

## Context

T0 left the BFF without refresh rotation and without consent handling. Behaviour of authservice, **observed** by running its
source locally (commit `cccf978`; the pinned `v0.3.4` has the same code for all of it, see ADR-0012):

- `POST /api/v1/auth/refresh` consumes the presented token and returns a new pair. Presenting the consumed token again returns
  401 **and revokes the family**: the new token from the first call then also answers 401. A browser fires several requests
  at once with the same cookie, so uncoordinated rotation ends the user's session.
- `GET /api/v1/auth/consents` returns the versions in force and `requiresConsent` (current Terms or Privacy not accepted;
  cookie consent is not gating). `POST /api/v1/auth/consents` with `{acceptedTerms, acceptedPrivacy, locale}` records the
  acceptance **of the versions in force at that moment** (an immutable row with time, IP, user agent, locale). Bumping
  `ConsentVersions:*` therefore forces every account through the step again.
- authservice's own gates enforce consent too (its OAuth consent step refuses an account that has not accepted), but a
  resource server receives no consent claim in the token.

## Decision

1. **Refresh rotation, single-flight, server side.** `web/app/lib/refresh.ts`: one upstream refresh per refresh token per
   process. Concurrent callers share the in-flight promise; callers arriving within 15 s with the same (already replaced)
   cookie get the same result; failures are not remembered. The key is a SHA-256 of the token; nothing is logged. Cookies stay
   HttpOnly, `SameSite=Strict`; the browser never sees a token.
2. **Where it runs.** A lapsed access cookie on a *navigation* is redirected by the edge gate through `GET /api/auth/refresh`
   (rotate, then back to the page; a short-lived `eia_refreshed` cookie prevents a loop if the browser does not keep the
   cookies). *API* calls rotate in their own handler (`/api/proxy/*` also retries once on an upstream 401). `GET
   /api/auth/session` rotates for a returning visitor. An unreachable identity service answers 503 and leaves the cookies
   alone; a refused refresh token ends the session and clears the cookies.
3. **Consent before anything else.** After login, and after every rotation, the BFF reads the consent status with the new
   token. If acceptance is required (or the status is unknown: it fails closed) the login response says
   `consentRequired`, no consent marker cookie (`eia_consent`) is set, and the edge gate sends every page to `/consent` and
   answers `/api/proxy/*` with 403 `consent_required`. `/consent` shows the versions in force, requires both boxes, and posts a
   plain "yes": **authservice picks the versions**, so a client cannot record acceptance of a version it was not shown.
   Declining signs out. The marker lives as long as the access token, so a bumped version reaches a signed-in user within one
   access-token lifetime.
4. **Logout revokes** the account's refresh tokens at authservice (best effort) before clearing the cookies.

## Consequences

- The consent marker is a **UX gate, not an authorization boundary**: it is an unsigned cookie, and the authoritative record is
  authservice's. A user who forges it only skips their own prompt; nothing in `interview-service` depends on it. If a later task
  needs consent enforced server side (for example before a submission), the proposal is a `consent` claim or an introspection-free
  check in authservice (see the report).
- Single-flight is per process. With one web machine per environment (the generated topology) that is sufficient; with several,
  two machines can present the same token at once and authservice would revoke the family. Trigger for a shared store (or sticky
  routing on the refresh cookie): scaling `web` beyond one machine.
- The refresh cookie lifetime (7 days) is the BFF's; authservice enforces its own expiry and answers 401 past it.
- Two-factor sign-in is still unsupported in the BFF (501), as in T0.
