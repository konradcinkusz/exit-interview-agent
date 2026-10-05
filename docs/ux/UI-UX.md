# UI and UX

Screens and flows as scaffolded, and a ranked backlog so the next sessions pick it up instead of
re-deriving it. Product rules are in `PROJECT-BRIEF.md` §5-§6: the portal is an account only (no link to an
employer), runs no model, and shows aggregates only above a minimum count and always with uncertainty.

## Screens today

| Route | Access | What it does |
|---|---|---|
| `/` | public | what the project is; development-build notice; links |
| `/login` | public | email and password; posts to the BFF, never to the identity service directly; one generic error per failure class; `?redirect=` is constrained to same-origin paths |
| `/account` | gated by the edge gate | reads the account through the BFF proxy and shows the subject; sign-out |
| `/healthz`, `/api/config` | public | probe; runtime config (proxy base path, whether identity is configured) |

## Flows

1. Visitor opens `/account` unauthenticated: redirected to `/login?redirect=%2Faccount`, signs in, returns
   to `/account`. (Playwright: `session.spec.ts`.)
2. Sign-out deletes both cookies with the attributes they were set with; the page is gated again.
3. Without identity configured the login form answers "Sign-in is not configured in this environment."
   (P8), and `/api/config` reports `identity.enabled = false`.

## Ranked backlog (each item names the brief's task)

1. **Consents** (versioned, per authservice's consent contract), forced re-acceptance. T2/T9.
2. **Own submissions**: list, status; **deletion by receipt code** (no account link). T5/T9.
3. **Submission tickets for the CLI**: mint, show once, expiry. T9/T11.
4. **Aggregates with uncertainty** (n >= K), no composite ranking, confidence on every number. T10.
5. **Refresh-token rotation** in the BFF and account deletion. T2/T9.
6. Registration and email verification screens (authservice owns the flows; the BFF forwards them).
7. Cookie-consent categories, default-deny (identity guide §9).
8. Accessibility pass: keyboard-only, contrast, 200% zoom; loading/empty/error states as a first-class
   category (testing guide §7).
