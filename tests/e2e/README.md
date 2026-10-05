# End-to-end tests

## Charter (E2E-ACCEPTANCE-TESTING, TESTING-STRATEGY §1)

E2E exists to protect the flows that cost users or trust when broken, and to verify the BFF integration
that no unit test sees. Today those flows are: **sign in and reach the account through the BFF**, **a
forged or unsigned token never passes the edge**, **tokens never reach page JavaScript**, **sign-out ends
the session**, and **runtime config exposes no backend address**.

E2E does not exist to test single-field validation (unit-test it), to duplicate the backend's own
integration tests through a browser, or to pixel-check visuals.

## What runs, and against what

The suite runs the **production artifact** (the Next.js standalone server from `web/`) against
`support/stub-backend.mjs`, a stub that serves a JWKS, fake accounts (created on first login, one per test), login, single-use
refresh rotation that revokes on reuse like authservice does, consents, logout, account deletion and an authenticated `/api/v1/me`. It is test scaffolding: it lets the web app's real code (login, HttpOnly cookies, edge gate,
proxy with bearer injection) run unmodified without a database or the identity container. Credentials in
it are fake and exist only there. A full-stack journey against the real AppHost is a later layer (T9).

| Layer | Budget | Trigger | Contents |
|---|---|---|---|
| Smoke (`@smoke`) | 5-10 min | every PR (`ci.yml`, job `e2e`) | the specs in `specs/` |
| Core regression, extended | not yet | not yet | arrive with the portal features; no empty config is committed |

## Run it

```bash
cd web && pnpm install --frozen-lockfile && pnpm build      # build the artifact first
cd ../tests/e2e && pnpm install --frozen-lockfile
npx playwright install --with-deps chromium                  # once
pnpm test
```

If a Chromium is already installed, set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` to its path instead of installing.

## Per-test bar

One business goal per test, independent, selected by `data-testid`, no `waitForTimeout`, zero tolerance for
flakiness: fix it or delete it. CI retries are a diagnostic aid (`retries: 2`), not a licence.
