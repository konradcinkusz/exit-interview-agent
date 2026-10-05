# End-to-end tests

## Charter (E2E-ACCEPTANCE-TESTING, TESTING-STRATEGY §1)

E2E exists to protect the flows that cost users or trust when broken, and to verify the BFF integration
that no unit test sees. Today those flows are: **sign in (with two-factor) and reach the account through the BFF**, **a
forged or unsigned token never passes the edge**, **tokens never reach page JavaScript**, **sign-out ends
the session**, **runtime config exposes no backend address**, **a ticket is shown once and kept nowhere**, **deletion by
receipt code is anonymous, header-only and uniform**, **the CSP carries a nonce and nothing violates it**, **cross-origin
state changes are refused**, and **every page passes the axe-core accessibility rules** (a subset of what a person would check).

E2E does not exist to test single-field validation (unit-test it), to duplicate the backend's own
integration tests through a browser, or to pixel-check visuals.

## What runs, and against what

The suite runs the **production artifact** (the Next.js standalone server from `web/`) against
`support/stub-backend.mjs`, a stub that serves a JWKS, fake accounts (created on first login, one per test), login, single-use
refresh rotation that revokes on reuse like authservice does, consents, logout, account deletion, two-factor sign-in, registration, email
verification, the data export, ticket minting and receipt deletion, and an authenticated `/api/v1/me`. It is test scaffolding: it lets the web app's real code (login, HttpOnly cookies, edge gate,
proxy with bearer injection) run unmodified without a database or the identity container. Credentials in
it are fake and exist only there. A full-stack journey against the real AppHost is a later layer.

**Contract note: the stub must stay in sync.** Its ticket and receipt routes mirror the interview-service contract
(`src/ExitInterviewAgent.Contracts/SubmissionContracts.cs`, ADR-0029, ADR-0030): status codes, the problem+json body, the kernel's
rate-limit body, the `X-Receipt-Code` header, the code's length and checksum, the 3-live-tickets cap. Its two-factor, register, verify
and export routes mirror authservice's controllers. Change the stub in the same pull request as any of those contracts (ADR-0051).

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
