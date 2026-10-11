# Redesign, phase 2: the interview page, and a visual identity

Phase 2 of the redesign that follows the [UI audit of 2026-10-11](../../audit-2026-10-11/README.md). It covers the
`/interview` page: the start screen, consent, chat, result and what happens after a deletion. These are the audit's changes 5 to 9
and the interview half of 10. [Phase 1](../phase1/README.md) did the portal around it. Phase 3 is the accessibility sweep and the
Polish end-to-end journey.

Screenshots: full page, production build against the e2e stub, light scheme, 1280×800 (`-desktop`) and 390×844 (`-mobile`).
`before-*` is phase 1 (the new header and tokens, the old interview page); `after-*` is this branch. Captured with
`tests/e2e/capture/` (`-g interview-`). There is no `before-interview-deleted`: the old page had no dialog for the capture script to
confirm in, and the state is in the audit as `28-interview-deleted-start`. A modal's backdrop covers only the viewport in a
full-page capture (`after-interview-delete-dialog-*`). Two PNGs were reduced to a 256-colour palette to stay under 250 KB.

| State | Before | After |
|---|---|---|
| No credit | [desktop](before-interview-no-credit-desktop.png) · [mobile](before-interview-no-credit-mobile.png) | [desktop](after-interview-no-credit-desktop.png) · [mobile](after-interview-no-credit-mobile.png) |
| Start | [desktop](before-interview-start-desktop.png) · [mobile](before-interview-start-mobile.png) · [PL](before-interview-start-pl-desktop.png) | [desktop](after-interview-start-desktop.png) · [mobile](after-interview-start-mobile.png) · [PL](after-interview-start-pl-desktop.png) |
| Consent | [desktop](before-interview-consent-desktop.png) · [mobile](before-interview-consent-mobile.png) | [desktop](after-interview-consent-desktop.png) · [mobile](after-interview-consent-mobile.png) |
| 402 at start (balance went to 0 meanwhile) | [desktop](before-interview-402-desktop.png) · [mobile](before-interview-402-mobile.png) | [desktop](after-interview-402-desktop.png) · [mobile](after-interview-402-mobile.png) |
| 403 unverified email | [desktop](before-interview-unverified-403-desktop.png) · [mobile](before-interview-unverified-403-mobile.png) | [desktop](after-interview-unverified-403-desktop.png) · [mobile](after-interview-unverified-403-mobile.png) |
| Chat | [desktop](before-interview-chat-desktop.png) · [mobile](before-interview-chat-mobile.png) · [PL](before-interview-chat-pl-desktop.png) | [desktop](after-interview-chat-desktop.png) · [mobile](after-interview-chat-mobile.png) · [PL](after-interview-chat-pl-desktop.png) |
| Chat, 429 on a reply | [desktop](before-interview-chat-429-desktop.png) · [mobile](before-interview-chat-429-mobile.png) | [desktop](after-interview-chat-429-desktop.png) · [mobile](after-interview-chat-429-mobile.png) |
| Chat, model down (503) | [desktop](before-interview-chat-503-desktop.png) · [mobile](before-interview-chat-503-mobile.png) | [desktop](after-interview-chat-503-desktop.png) · [mobile](after-interview-chat-503-mobile.png) |
| Stop and delete | [desktop](before-interview-delete-dialog-desktop.png) · [mobile](before-interview-delete-dialog-mobile.png) | [desktop](after-interview-delete-dialog-desktop.png) · [mobile](after-interview-delete-dialog-mobile.png) |
| Result | [desktop](before-interview-result-desktop.png) · [mobile](before-interview-result-mobile.png) · [PL](before-interview-result-pl-desktop.png) | [desktop](after-interview-result-desktop.png) · [mobile](after-interview-result-mobile.png) · [PL](after-interview-result-pl-desktop.png) |
| After deleting | (audit: 28) | [desktop](after-interview-deleted-desktop.png) · [mobile](after-interview-deleted-mobile.png) |
| Start page (new identity) | [phase 1](../phase1/after-home-signed-out-desktop.png) | [desktop](after-home-signed-out-desktop.png) · [mobile](after-home-signed-out-mobile.png) · [PL](after-home-signed-out-pl-desktop.png) · [signed in](after-home-signed-in-desktop.png) |
| Dark scheme | | [start page](after-dark-home-signed-out-desktop.png) · [interview start](after-dark-interview-start-desktop.png) · [chat](after-dark-interview-chat-desktop.png) · [dialog](after-dark-interview-delete-dialog-desktop.png) · [result](after-dark-interview-result-desktop.png) |

Captures run with reduced motion, and sticky elements are laid out in place for the full-page picture (a capture-only setting in
`tests/e2e/capture/capture.spec.ts`; the product keeps them sticky).

## What changed

**A visual identity, for the whole site** (the owner asked for a site that looks excellent, not only correct):

- **Palette:** warm paper (`#f6f5f1`), ink, and one deep teal (`#0d6b62`) for action, focus and the brand, with a matching dark
  scheme. Every text pair passes AA and every control border and focus ring 3:1; phase 3's checks measure both.
- **Brand and icons:** a small inline-SVG mark (a speech bubble in a rounded tile) in the header, and an inline-SVG icon set drawn for
  the project (`app/_components/Icon.tsx`). No icon font, nothing fetched.
- **Type, space and depth:** the system UI face (no download, no third party), a display size for the hero, a 4-based spacing
  scale, and layered shadows in light and dark. The page is now a three-track grid: reading width, plus a wider track for the hero
  and rows of cards. On wide screens the header is sticky and translucent.
- **Start page:** a two-column hero (eyebrow, headline, one button, an inline-SVG illustration of a conversation turning into a
  record), the journey as a three-step stepper (Sign in → Interview → Your record), and "what happens to your words" as three cards
  with icons.
- **Motion:** messages rise into place, the typing indicator has three dots, and loading shimmers. All of it is off under
  `prefers-reduced-motion`. Animations move things but never fade text, so contrast holds at every frame (axe caught a fade).

**The interview page:**

- **Start:** one status block: "1 interview ready" (green) or "No interview available: you have no interview credit" (amber), each
  with one line on what it means. Continue is the primary button. When it is disabled, its accessible description and a line under
  it say "Continue needs an interview credit."; Buy then becomes the primary button. The interview language is two choice rows,
  defaulting to the portal's language; the select is styled. Any start failure re-reads the balance, and a 402 shows 0 at once, so
  "1" never sits next to "no credit". With `DEV_HINTS=1` and no credit, a "Local development" line says how to get the test
  account's credits.
- **Consent:** the three facts that matter come first, numbered: an AI; Anthropic receives what you type; the result is readable for
  30 minutes and everything can be deleted. "Read the full terms" opens the whole list, which is unchanged. After a 429, Start waits
  out the seconds.
- **Stepper** at the top: Sign in (done), Interview (current), Your record.
- **Chat:** a chat panel with bubbles (the interviewer on the left with the mark, you on the right in teal), animated typing dots,
  auto-scroll to the newest message, and a sticky composer. The page brings the composer into place when a message arrives, so it
  never covers the newest message. There is a progress line ("Introduction", "Topic n of about 6"). Send is the only primary button and reads "Send again" when a
  failure kept the answer. A 429 shows a countdown that follows the clock (Send is re-enabled at 0, nothing is sent by itself). The
  speaker's name shows on each bubble, and the log carries the interview's `lang`.
  - **Finish interview** (secondary, from the first topic question on) sends "Can we finish now?" / "Czy możemy już skończyć?" as the
    person's answer. The interview engine already treats that as a request to finish and keep the record (`FinishRequestCue` in
    `ReplySignals.cs`), so no endpoint was added (plan §10 is unchanged). The stub now mirrors that cue.
  - **Stop and delete** (red outline) opens a modal `<dialog>`: Cancel first and focused, focus kept inside, Escape cancels, focus
    returns to the button.
- **Result**, laid out as a short report:
  - A summary card with a check says the result stays readable until about HH:MM, 30 minutes after the end the page saw, and that the interview used
    one credit.
  - One warning, "Drafts, not facts", holds the service's legal notice; the second box is gone.
  - The facts are a grid of tiles. Each discussed topic is a card with a five-dot meter (decorative; the text says "Rating 2 of 5")
    and its quotes. Topics that were not discussed are one line.
  - Draft cards have their kind as a label and Copy in the card's header.
  - Dropped drafts are explained in a sentence.
  - The three downloads sit under one "Save a copy" control (primary), with "Delete everything now" (red outline, same dialog) on
    the other side of one action bar.
- **After deleting:** "The interview was deleted. Nothing from it is kept." Then what happened to the credit ("not returned: a credit
  is used when an interview starts", per plan §10) and the next step (start another, or buy one). A stopped interview says its credit
  stays used; a lost one (404/410) says the credits are shown below. No billing rule or price changed.
- **Language:** the page's copy follows the portal's language (the header switch). The interview's language is a separate choice on
  the start screen.

## How it was checked

- `cd web && pnpm lint && pnpm typecheck && pnpm test && pnpm build`: pass. New unit tests cover the reducer (402 sets 0, Finish only
  after a topic question, the progress count, the end time) and the copy (Polish plurals, the three consent facts, the finish
  sentences match the agent's cue and do not read as a withdrawal).
- `cd tests/e2e && pnpm test`: pass. The interview specs were updated to the new copy and controls (no test removed or skipped). The
  new `interview-redesign.spec.ts` covers the status line, the 402 balance, the consent summary, Finish, the dialog's focus trap,
  Escape and focus return, Send again, the 429 countdown (with Playwright's clock) and the result layout. Axe still runs on start,
  consent, chat and result.

## Not checked

- The visual design was reviewed by the builder from these screenshots, not by a designer or the owner. Dark mode was reviewed in
  the five dark captures only.

- The finish request was checked against the stub, which copies the agent's regex, and not against the real interview engine with a
  model. The engine treats it as a finish only after the in-chat consent answer. The button appears only after a topic question for
  that reason; whether a real session's first turns are labelled `topic` as the stub's are was not verified.
- "Topic n of about 6" counts `topic` turns. A real interview that skips or merges topics will show fewer; the line says "about".
- The "until about HH:MM" time uses the browser's clock when the page saw the end, not the service's; a few seconds may differ.
- Screen reader behaviour of the dialog and of the silent countdown; Safari and Firefox (`<dialog>` focus return); 200% zoom in a real
  browser. These are phase 3 or need a person.
- Polish copy is a builder's draft (no native review).
