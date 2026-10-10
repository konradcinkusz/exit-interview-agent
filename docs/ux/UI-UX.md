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
| `/interview` | signed in | **Interview with an AI** (plan [section 3 and 10](../architecture/web-app-plan.md)): language, time at the company, credits, the full consent, the chat, the record and draft texts, and the delete. Its own section below. Not in the header navigation yet: where it is linked is the owner's decision |
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

## Interview screens (`/interview`)

One page with five steps, held by a pure reducer (`app/interview/state.ts`). Copy: `lib/messages/interview.ts`, in Polish and
English; the Polish is typed against the English keys and is a builder's draft until a native speaker has reviewed it (the W9
quality gate in the plan). The rest of the portal stays English-only.

| Step | What the person sees | Leaves when |
|---|---|---|
| start | language (the browser's language on the first visit, then their choice), time at the company, the credits they have, **Continue** (needs one credit), **Buy one interview** (goes to the payment provider's page) | Continue -> consent |
| consent | the whole disclosure: the interviewer is an AI and says so; everything typed goes to the model provider (Anthropic); the conversation is held in memory and discarded when the interview ends; the result stays readable for 30 minutes; stop and delete at any time; the drafts are not facts and not legal advice; nothing checks employment; a failure on our side returns the credit. A checkbox is needed before **Start the interview**, which uses one credit | start -> chat; failures stay here; **Back** uses nothing |
| chat | a live log of the conversation; an answer box (Enter sends, Shift+Enter starts a new line); "The interviewer is typing…" while a reply is pending; **Stop and delete** (asks once) | completed -> result; stopped or failed -> ended; deleted -> start; session lost (410) -> start with the credit back |
| result | the legal notice first, then the draft warning; the record as sentences per topic (rating, confidence, quotes), never raw JSON; one card per draft with **Copy**; downloads: the record (JSON), the drafts (`tiles.html`, built in the browser from the same JSON with no script and no network resource), the drafts (JSON); **Delete everything now** (asks once) | deleted -> start with "The interview was deleted. Nothing from it is kept." |
| ended | "stopped": nothing kept, no record; "failed": a fault on our side, the credit is returned | start |

Failure copy (the mapping is one function, `failureFor` in `lib/interview-api.ts`):

| Answer | What the person is told | Where they stay |
|---|---|---|
| 402 `payment_required` | no credit; buy one | start |
| 409 `interview_in_progress` | an open interview exists; finish or delete it | consent |
| 409 `interview_ended` | this interview has ended | result |
| 409 `not_completed` | the result is not ready | result |
| 410 `gone` | expired or lost in a restart; the credit is back | start |
| 422 `reply_invalid` | empty or over 2000 characters | chat, text kept |
| 429 `rate_limited` | a wait in seconds when the service gives one; **no automatic retry** | where they were |
| 503 `provider_unavailable` | the model is not responding; the interview is still open; the typed text is kept | chat |
| 503 `interviews_disabled`, `billing_disabled` | paused for now; no credit is used | where they were |
| 503 or 504, backend unreachable | the service cannot be reached right now | where they were |
| 401 | signed out: a link to sign in again | where they were |
| 403 `consent_required` | accept the current terms first: a link to `/consent` | where they were |
| 403 `email_not_verified` | the contract answer for an unconfirmed address; **today the page shows the generic copy** (the BFF swallows backend 403s, see the scenarios below) | where they were |

Rules this page keeps:

- **Nothing is stored by the page.** The transcript and the result live in memory. No `localStorage`, `sessionStorage`, cookie, URL or
  log holds interview text, and a reload empties both (asserted in the browser suite).
- **The record is read as sentences.** Raw JSON is only a download.
- **Copy puts the draft text alone on the clipboard**, without its label or the notice. When the clipboard is not available the page
  says so and asks the person to copy by hand.
- **The language of the document follows the page's language choice.** The root layout is English, so the page sets `lang` itself.
- **No third party.** No script, font, analytics or image from another origin (the browser suite checks the requests and the CSP).
- **Accessible.** One `h1`, the log is a live region (`role="log"`, `aria-live="polite"`), the typing line is a status, every failure is
  a `role="alert"` next to the control that caused it, and the confirmation of a deletion takes focus. Axe runs on all four steps.
- **The result has no server copy after it is shown.** The service keeps it for 30 minutes after the interview ends, then wipes it, and
  "Delete everything now" wipes it at once. The page does not offer to keep anything.

Not on this page, and not invented: a submit to employer signals (that is the existing flow, opt-in, and separate), a "my interviews"
list, a way to see an earlier transcript, and any score or comparison.

## Interview scenarios (end-to-end, W7)

The browser suite runs these against the production build and the stub of the service (`tests/e2e/specs/interview-journey.spec.ts`,
`interview.spec.ts`). The contract they hold to is plan section 10 and `tests/contracts/*.json`.

| # | Scenario | What the suite asserts |
|---|---|---|
| a | No credit, purchase, interview, result, copy, download, delete | 402 closes the start; the payment page is reached; a paid event (sent twice, same id) adds one credit; the closing turn is shown and the page waits for `completed` before the result; "Copy" puts the draft on the clipboard; `tiles.html` has the notice and no script; after deletion the result answers 404 `not_found`; no storage, cookie (HttpOnly included), URL or IndexedDB holds the text; no request leaves the origin except the payment page |
| b | Consent declined | "Start the interview" stays disabled; **Back** starts nothing, the credit is not used, no interview exists |
| b2 | Stopped in the chat | the stop line and "nothing is kept, no record" are shown; no result; the credit stays used (the contract: a stopped session keeps its credit) |
| c | Rate limited (429) on start | "Too many requests. Wait 42 seconds, then try again." from the body's `retryAfter`; no credit used |
| c | Rate limited (429) on a reply | the typed answer is kept, the interview stays open, the wait is shown |
| c | Paused (503 `interviews_disabled`) | "Interviews are paused for now. Your credit has not been used." |
| c | Model unavailable (503 `provider_unavailable`) | the typed text is kept; the interview is still open (existing suite) |
| c | Unverified email (403 `email_not_verified`) | no interview starts and the credit is kept. **Known defect:** the message shown is "The service cannot be reached right now." (see below) |
| d | Nothing kept after the visit | covered in (a): storage, cookies, URL, IndexedDB, and the text is absent from all of them |
| e | Keyboard only | consent box by Space, start by Enter, answer field sends on Enter, Stop and delete opens a dialog with Cancel focused |
| e | Axe (WCAG 2.0/2.1 A and AA) | the start, consent, chat and result steps, and the no-credit and rate-limited screens, have no violations |

Known defect, recorded and not fixed here: `web/app/lib/upstream.ts` treats every backend 403 as "wrong ingress for this rung" and
moves to the next candidate, so a real 403 from the service becomes `backend_unavailable`. The fix belongs to the BFF (outside the
interview files); until then the 403 row above shows the wrong copy. The suite pins that copy with a comment so the fix changes it on purpose.

## Copy rules

- Plain language, one idea per sentence, no marketing. English is the shipped language for the portal; every string lives in
  `web/app/lib/messages/en.ts`. The interview page is the one exception: Polish and English (`lib/messages/interview.ts`), with the
  Polish typed against the English keys, so a missing translation is a build error, not a blank. A Polish catalog for the rest of the
  portal is still not shipped: a half translation is worse than none.
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

1. Polish for the rest of the portal, when someone can write and review it properly (the interview page is done, in draft).
2. Cookie-consent categories, default-deny (identity guide §9): the portal sets only strictly necessary cookies today,
   so there is nothing to consent to; revisit if that changes.
3. Password reset and email change screens (authservice owns the flows).
4. Two-factor enrolment screens (authservice supports it; the portal only signs in with it today).
5. Manual accessibility pass with assistive technology.

Done by T10b: the Signals pages (list and employer), the copy contract of AGGREGATION §8, the receipt-deletion sentence about the next update.

Done by T2 and T9: consents, refresh rotation, account deletion, registration and verification, two-factor sign-in,
tickets, deletion by receipt, export, privacy and connect pages.
