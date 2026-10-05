# UI and UX

Screens, flows and the rules the copy must obey. Product rules are in `PROJECT-BRIEF.md` §5-§6: the portal is an
account only (no link to an employer), runs no model, and shows aggregates only above a minimum count and always with
uncertainty. This page is written before the code (T9) and is the reference the Playwright suite is checked against.

## Principles that shape every screen

1. **Say what the system cannot do.** Submissions are not linked to accounts, so the portal has no "my submissions"
   list and no way to recover a lost receipt code. Both are stated where a user would look for them.
2. **A secret is shown once and never stored by the page.** A submission ticket and a receipt code live in memory
   only: not in `localStorage`, `sessionStorage`, a cookie, a URL, a log or analytics. Reloading or navigating away
   clears them.
3. **Uniform answers stay uniform.** Deleting by receipt code answers the same for every well-formed code
   ([ADR-0029](../adr/0029-receipt-deletion-semantics.md)); the copy says "if it existed, it is deleted now" and never
   "deleted successfully".
4. **No inference language.** Records are personal data ([ADR-0018](../adr/0018-records-are-treated-as-personal-data.md)):
   the words "anonymous" and "anonymised" are not used for records. Aggregates, when T10 lands, are "aggregated".
5. **No third parties.** No script, font, image or analytics from another origin; the CSP forbids it.

## Information architecture

| Route | Access | What it does |
|---|---|---|
| `/` | public | what this is, what is stored, what is not, the non-goals; entry points to every other page |
| `/login` | public | email and password; if the account has two-factor, a **second step** (authenticator code or one recovery code) on the same page; generic errors per failure class |
| `/register` | public | create an account; the Terms and Privacy versions in force are fetched from authservice and shown; a second consent screen is not needed because registration records the acceptance |
| `/verify-email` | public | landing page of the link in the verification email (`?token=&email=`); resend form |
| `/consent` | signed in | the consent step (versions in force, both boxes required; decline signs out) |
| `/connect` | public | **Connect your AI client**: the MCP URL from `/api/config`, the steps in Claude, what the connector can and cannot do |
| `/cli` | signed in | **Use the CLI**: mint a submission ticket (shown once, copy button, expiry countdown), what a ticket is and is not, the CLI commands that exist today |
| `/delete-submission` | public | **Delete a submission** by receipt code. Public on purpose: the code is the only credential, and it must work after the account is gone |
| `/account` | signed in | the subject, sign-out, **export your data** (authservice only), **delete account** with the exact semantics of [ADR-0014](../adr/0014-account-deletion-semantics-and-no-pii-in-telemetry.md) |
| `/account-deleted` | public | what deletion did and did not do |
| `/privacy` | public | the key facts of [`DESIGN.md`](../privacy/DESIGN.md), linked and not duplicated |
| `/healthz`, `/api/config` | public | probe; runtime config (proxy base path, identity enabled, MCP URL) |
| 404, error | public | plain-language pages; the error page never prints an error message or digest into the document |

Out of scope here and not invented: a submit form (interviews happen in the user's AI client or the CLI), a "my
records" list, aggregates (T10).

### Navigation

One header on every page: Home, Connect your AI client, Use the CLI, Delete a submission, Privacy, Account. Pages that
need a session are gated by the edge gate (a visitor lands on `/login?redirect=...` and returns), so the header does
not need to know whether the visitor is signed in.

## Flows

1. **Sign in.** Credentials -> (consent step if versions moved) -> the page asked for. With two-factor: credentials ->
   code step -> the same continuation. The challenge never reaches page JavaScript: the BFF keeps it in a short-lived
   HttpOnly cookie. A wrong code keeps the user on the step; an expired challenge or a locked account sends them back
   to the first step with a message.
2. **Sign up.** `/register` -> "check your email" (when the deployment verifies email) -> the emailed link opens
   `/verify-email` -> sign in. When authservice cannot send email it marks the account verified at once; the page says
   "account created, sign in".
3. **Use the CLI.** `/cli` -> "Create a ticket" -> the ticket appears once with a countdown -> copy -> use it in the
   CLI. Clearing: expiry, "Clear it now", navigating away, reloading. TICKET_LIMIT and rate limits are shown as
   what-to-do-next messages.
4. **Delete a submission.** `/delete-submission` -> paste the 46-character code -> the uniform answer. A malformed code
   (wrong length or checksum) is an error the user can fix; rate limiting says to wait.
5. **Delete the account.** Account page -> the four facts -> password and typed confirmation -> `/account-deleted`.

## Copy rules

- Plain language, one idea per sentence, no marketing. English is the shipped language; every string lives in
  `web/app/lib/messages/en.ts`, so a Polish catalog is a new file with the same keys, not a refactor. Polish is **not**
  shipped: a half translation is worse than none.
- The exact semantics sentences (account deletion, receipt deletion, ticket) are catalog entries and are asserted by tests.

## Accessibility and quality bar

- Keyboard-only operable; a skip link; one `h1` per page; every control labelled; errors in `role="alert"`; focus moves
  to the new step (second factor, ticket) when it appears.
- Contrast meets WCAG 2.1 AA in light and dark (`prefers-color-scheme`, the template's convention).
- Responsive down to 320 px, no horizontal scroll; 200% zoom works.
- Every page has loading, empty and error states.
- An automated check (axe-core, WCAG 2.0/2.1 A and AA rules) runs in the browser suite on every page. It finds a subset
  of problems; keyboard and screen-reader testing by a person are still owed ([OP-16](../OPEN-PROBLEMS.md)).

## Ranked backlog

1. **Aggregates with uncertainty** (n >= K), no composite ranking, confidence on every number. T10.
2. Polish catalog, when someone can write and review it properly.
3. Cookie-consent categories, default-deny (identity guide §9): the portal sets only strictly necessary cookies today,
   so there is nothing to consent to; revisit if that changes.
4. Password reset and email change screens (authservice owns the flows).
5. Two-factor enrolment screens (authservice supports it; the portal only signs in with it today).
6. Manual accessibility pass with assistive technology.

Done by T2 and T9: consents, refresh rotation, account deletion, registration and verification, two-factor sign-in,
tickets, deletion by receipt, export, privacy and connect pages.
