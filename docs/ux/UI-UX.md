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
   the words "anonymous" and "anonymised" are not used for records. Aggregates are "aggregated", and nothing stronger.
5. **No third parties.** No script, font, image or analytics from another origin; the CSP forbids it.
6. **Show exactly what the API returns, and nothing derived** (T10b, [ADR-0067](../adr/0067-signals-pages-render-the-api-and-derive-nothing.md)). The Signals pages never compute, sort, rank, average, round or
   compare. There is no control that could ask for it and no element that could show it; tests assert the absence. A number is never shown without its interval and n on the same line.

## Information architecture

| Route | Access | What it does |
|---|---|---|
| `/` | public | what this is, what is stored, what is not, the non-goals; entry points to every other page |
| `/login` | public | email and password; if the account has two-factor, a **second step** (authenticator code or one recovery code) on the same page; generic errors per failure class |
| `/register` | public | create an account; the Terms and Privacy versions in force are fetched from authservice and shown; a second consent screen is not needed because registration records the acceptance |
| `/verify-email` | public | landing page of the link in the verification email (`?token=&email=`); resend form |
| `/consent` | signed in | the consent step (versions in force, both boxes required; decline signs out) |
| `/connect` | public | **Connect your AI client**: the MCP URL from `/api/config`, the steps in Claude, what the connector can and cannot do, and where the operator runbook lives |
| `/signals` | signed in | **Employer signals**: the employers that have something to show, alphabetical and paged; "this tool does not rank employers"; what the figures are and how to read them |
| `/signals/[employerRef]` | signed in | **One employer**: snapshot freshness, respondent band, six topics each standing alone (mean with interval and n, reliability, coverage, distribution, verification, three single-band cuts) |
| `/cli` | signed in | **Use the CLI**: mint a submission ticket (shown once, copy button, expiry countdown), what a ticket is and is not, the CLI commands that exist today |
| `/delete-submission` | public | **Delete a submission** by receipt code. Public on purpose: the code is the only credential, and it must work after the account is gone. It also says published figures drop the record at the next update |
| `/account` | signed in | the subject, sign-out, **export your data** (authservice only), **delete account** with the exact semantics of [ADR-0014](../adr/0014-account-deletion-semantics-and-no-pii-in-telemetry.md) |
| `/account-deleted` | public | what deletion did and did not do |
| `/privacy` | public | the key facts of [`DESIGN.md`](../privacy/DESIGN.md), linked and not duplicated |
| `/healthz`, `/api/config` | public | probe; runtime config (proxy base path, identity enabled, MCP URL) |
| 404, error | public | plain-language pages; the error page never prints an error message or digest into the document |

Out of scope here and not invented: a submit form (interviews happen in the user's AI client or the CLI), a "my
records" list, an employer search, any sorting or ranking of employers, any view that compares employers, topics or bands.

### Navigation

One header on every page: Home, Connect your AI client, Use the CLI, Employer signals, Delete a submission, Privacy, Account. Pages that
need a session are gated by the edge gate (a visitor lands on `/login?redirect=...` and returns), so the header does
not need to know whether the visitor is signed in. "Employer signals" is such a gated entry (like "Use the CLI" and "Account"): it is not
prefetched, so a signed-out prefetch cannot cache the sign-in redirect.

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
6. **Read an employer's signals.** Navigation -> `/signals` (list, alphabetical, paged by "Previous" and "Next") -> an employer -> `/signals/<ref>`. An employer with nothing to show, a
   reference that is not well formed and one the service has never heard of all get the same page: "There is nothing to show for this employer." The page never says which of the three it is.

## Signals screens

Binding copy: [AGGREGATION §8](../privacy/AGGREGATION.md#8-api-contract-and-ui-copy-contract). Every string is a catalog entry (`m.signals`) and is asserted by the browser suite.

**`/signals`**

| State | What the page shows |
|---|---|
| loading | "Loading…" in a status region |
| list | the explainer; the claimed-not-verified line; "Employers are listed alphabetically. This tool does not rank employers."; the snapshot line; one link per employer reference, in the order the API gave; Previous / Next links only when the API's `total` says there is another page |
| empty (no snapshot yet, or no employer has a displayable cell) | "No employer has enough responses to show yet." and what that means; no count |
| 429 | calm message with the wait the service asked for; the button is disabled until then; **no automatic retry** |
| 401, 403 | sign in again / accept the current terms first |
| other failure | "The signals could not be loaded. Nothing is shown." |

No search box, no sort control, no filter, no score, no count of employers or of respondents.

**`/signals/[employerRef]`** (the reference is validated against the API's pattern before any request; an invalid one never reaches the network)

1. `h1` "Employer signals" and the reference as text.
2. The claimed-not-verified line, then the snapshot line: "Updated {date}. Figures change once per update, not when someone submits. A record deleted after this date is still counted until the next update."
3. Respondents as a band ("10-24"), never a count.
4. Six topic sections, always in the API's order (not sorted by anything), each standing alone, with no summary across them:
   - `ok`: one line "{mean} (95% interval {lower}-{upper}), {n} ratings"; a range bar on a fixed 1-5 axis (decorative, `aria-hidden`, text beside it; it encodes the interval and the mean, never n); "A wide interval means early, not wrong.";
     Reliability; Coverage ("{high/medium/low}: the share of respondents who rated this topic."); the three-bin distribution as a table when present; the verification breakdown as a table when present, under "Employment is claimed, not verified.";
     three single-band cuts, each a table (band, details) when `published`.
   - `insufficient_data`: "Not enough responses to show this topic." No number, no cuts.
   - a `suppressed` cut: "This breakdown is hidden to protect small groups." No band is named, no cell is listed.
   - a `none` band inside a published cut: "No ratings." (a zero is not a group of people, [AGGREGATION §3](../privacy/AGGREGATION.md#3-clean-partitions-by-example)).
5. No colour carries meaning on its own: the range bar uses one neutral colour, and every state is text.

| State | What the page shows |
|---|---|
| 404 or invalid reference | "There is nothing to show for this employer." The same words for unknown, below the minimum and malformed |
| 429, 401, 403, other failure | as on the list |
| revisit | the browser revalidates with the weak ETag the service sent; a 304 costs no body; the figures cannot move before the next update, so the page does not poll |

**Never on these pages** (and asserted absent): ranking, sorting or scoring controls; "best", "worst", "top", "average", "score", "percentile", "trend", an up or down arrow; a composite across topics; any view that puts two employers or two bands next to each other;
a colour scale that implies good or bad; any figure computed in the browser or the BFF.

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

1. Polish catalog, when someone can write and review it properly.
2. Cookie-consent categories, default-deny (identity guide §9): the portal sets only strictly necessary cookies today,
   so there is nothing to consent to; revisit if that changes.
3. Password reset and email change screens (authservice owns the flows).
4. Two-factor enrolment screens (authservice supports it; the portal only signs in with it today).
5. Manual accessibility pass with assistive technology.

Done by T10b: the Signals pages (list and employer), the copy contract of AGGREGATION §8, the receipt-deletion sentence about the next update.

Done by T2 and T9: consents, refresh rotation, account deletion, registration and verification, two-factor sign-in,
tickets, deletion by receipt, export, privacy and connect pages.
