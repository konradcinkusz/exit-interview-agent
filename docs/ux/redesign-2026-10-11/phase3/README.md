# Redesign, phase 3: accessibility sweep, the Polish journey, and a final design pass

Phase 3 of the redesign that follows the [UI audit of 2026-10-11](../../audit-2026-10-11/README.md). It covers the audit's change 10:
run axe and contrast checks on every state and capture the Polish screens. It also adds an end-to-end test of the former
employee's journey in Polish and makes a last critical pass over the design.

Screenshots: full page, production build against the e2e stub, light scheme, reduced motion, 1280×800 (`-desktop`) and 390×844
(`-mobile`). `before-*` is main after phase 2 (copied from the phase 1 and phase 2 folders); `after-*` is this branch.

## The design pass

I reviewed every screen at both widths for spacing rhythm, alignment, illustration quality and the empty and loading states. Four
things looked weaker than the rest and were changed:

| Screen | Before | After | What changed |
|---|---|---|---|
| Sign in | [desktop](before-login-desktop.png) · [mobile](before-login-mobile.png) | [desktop](after-login-desktop.png) · [mobile](after-login-mobile.png) | The form was a bare full-width column. It is now one centred card with a deep shadow and a full-width primary button; the local-development box sits under it at the same width |
| Sign up | [desktop](before-register-desktop.png) · [mobile](before-register-mobile.png) | [desktop](after-register-desktop.png) · [mobile](after-register-mobile.png) | the same card |
| Consent | [desktop](before-consent-desktop.png) · [mobile](before-consent-mobile.png) | [desktop](after-consent-desktop.png) · [mobile](after-consent-mobile.png) | the same card; the two buttons stack, primary first |
| Account | [desktop](before-account-desktop.png) · [mobile](before-account-mobile.png) | [desktop](after-account-desktop.png) · [mobile](after-account-mobile.png) | each card is anchored by an icon badge (the delete card's badge is red); "Go to the interview" has an arrow |
| Interview start | [desktop](before-interview-start-desktop.png) · [mobile](before-interview-start-mobile.png) | [desktop](after-interview-start-desktop.png) · [mobile](after-interview-start-mobile.png) | the status line carries an icon; the choices and the buttons sit in one card instead of floating on the page; the loading line has a shimmer placeholder |
| After deleting | [desktop](before-interview-deleted-desktop.png) · [mobile](before-interview-deleted-mobile.png) | [desktop](after-interview-deleted-desktop.png) · [mobile](after-interview-deleted-mobile.png) | as the start screen, after the deletion notice |

The result's loading state also got a placeholder card. The final set for the rest of the journey, unchanged by this pass:

- [start page](after-home-signed-out-desktop.png) ([mobile](after-home-signed-out-mobile.png))
- [no credit](after-interview-no-credit-desktop.png)
- [consent](after-interview-consent-desktop.png)
- [chat](after-interview-chat-desktop.png) ([mobile](after-interview-chat-mobile.png))
- [dialog](after-interview-delete-dialog-desktop.png)
- [result](after-interview-result-desktop.png) ([mobile](after-interview-result-mobile.png))

## Evidence for the checks

| Evidence | Image |
|---|---|
| Keyboard focus on Continue (3 px teal ring, offset) | [focus-start-continue](focus-start-continue.png) |
| Keyboard focus on a language choice row (the ring is drawn on the row) | [focus-choice-row](focus-choice-row.png) |
| Stop and delete: the dialog opens with Cancel focused; the page behind is inert | [focus-dialog-cancel](focus-dialog-cancel.png) |
| 200% zoom of a 1280 px window (640 CSS px), Polish: start, chat, result | [start](zoom200-interview-start-pl.png) · [chat](zoom200-interview-chat-pl.png) · [result](zoom200-interview-result-pl.png) |

The zoom captures were taken at scale 2 and are shown at their CSS size to stay under 250 KB; the focus and zoom captures predate
the design pass. A dialog's backdrop covers only the viewport in a full-page capture.

## What the suite now checks

New `tests/e2e/specs/a11y-interview.spec.ts` walks every interview state the stub can reach: no credit, start, consent with the
full terms open, 429 with its countdown, 403 unverified email, 402, chat, chat at topic 1, chat 429, chat 503 with the answer kept,
the delete dialog in the chat, after deleting, the result with Save open, and the delete dialog on the result. On that walk:

| Check | Where | Result |
|---|---|---|
| axe, WCAG 2.0/2.1 A and AA (text contrast included) | every state, light and dark, English and Polish (4 walks) | no violations |
| Control borders (text fields, select, outlined buttons, the Save control, the language switch) against their background, 3:1 (WCAG 1.4.11; axe does not measure it) | every state and six portal pages, light and dark | pass; at least one control is measured on each page |
| Focus ring colour against the page, main, card and dialog surfaces, 3:1 | same | pass |
| A visible focus indicator (outline of 2 px or more) on every Tab stop | same | pass |
| Tab order on `/interview`: skip link, product name, Interview, Account, Sign out, language, then the form in reading order | start | pass |
| No animation or transition under `prefers-reduced-motion` | interview consent | pass |
| No sideways scrolling at 390 px and at 200% zoom (640 CSS px, scale 2), in Polish (the longer words) | every portal page and every interview state | pass |

The existing `a11y.spec.ts` still covers every portal page in both schemes, the action states, and 320 px.

New `tests/e2e/specs/journey-pl.spec.ts` follows the target journey from the audit, in a Polish browser without touching the switch:

1. the start page;
2. sign-in with the reason shown;
3. the start screen ("1 rozmowa gotowa");
4. consent with the three facts and the full terms;
5. the chat with its progress line;
6. "Zakończ rozmowę";
7. the result (time readable, the credit, the warning, "Nie poruszono", Save);
8. the delete dialog with Anuluj focused;
9. after deleting: the credit and the next step.

It then asserts that nothing of the conversation stayed in the browser, and that no English sentence from the portal or the
interview copy appeared on the way (the product name aside).

## Found and fixed by the sweep

- **The chat log scrolled but could not be reached by keyboard** (axe `scrollable-region-focusable`, serious). It is now focusable
  (`tabIndex=0`, with its label and the focus ring), fixed in phase 2.
- **A fading entrance animation lowered text contrast mid-frame** (axe `color-contrast`, seen intermittently in phase 2's suite).
  Entrance animations now move things and never fade text.

## Not checked

- The design pass was mine, from screenshots; no designer and not the owner reviewed it.

- A screen reader (NVDA, VoiceOver, TalkBack): how the live log, the silent countdown and the dialog are announced. Still owed by
  a person (OP-16).
- Real browser zoom (Ctrl +) in Chrome, Firefox and Safari: 200% was emulated with a 640 px viewport at scale 2, which is how
  zoom reflows a layout but not every browser's zoom.
- Windows high-contrast (forced colours) mode, and the native checkbox and radio drawn by each browser (excluded from the border
  check).
- Text spacing overrides (WCAG 1.4.12) and orientation lock.
- Firefox and Safari in general: the suite runs Chromium only.
