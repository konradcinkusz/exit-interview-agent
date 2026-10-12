# Redesign, 2026-10-12: a stronger visual direction

This continues the [three-phase redesign of 2026-10-11](../redesign-2026-10-11/phase3/README.md). The structure, the copy rules and
the flows are unchanged. This pass is about how the portal and the interview look: type, spacing, colour, hierarchy, motion and
the empty, loading and error states. No dependency, framework, API or billing change.

Screenshots: full page, production build against the e2e stub, reduced motion, 1280×800 (`-desktop`) and 390×844 (`-mobile`).
`before-*` is `main` before this branch and `after-*` is this branch. `-dark-` is the dark scheme and `-pl` is Polish. The PNGs are
palette-reduced to stay under 250 KB, so a few small colours (for example the amber dot in the footer) shift slightly.

## How it was reviewed

1. I captured 58 states in light and dark (116 PNGs) with `tests/e2e/capture`.
2. Five reviewer agents each took one area: home and auth, interview start and consent, chat, result and delete, and account, footer
   and the developer pages. They looked at every PNG and listed what was wrong: element, screen, why, and a suggested fix.
3. I checked their findings against the PNGs and the code. I implemented the ones that held up. I rejected the ones that were wrong
   or out of scope (listed below).
4. I captured again and four reviewer agents checked for regressions. I fixed what they found, ran the suite, and made the final
   capture.

## The direction

The look is calm and editorial: warm paper and ink, one deep teal for every action, quiet surfaces lifted by a hairline and a soft
shadow, and a few clearly ranked headings with room around them. It builds on the existing tokens in `web/app/app/globals.css`.

- **Colour.** New "solid" tokens (`--accent-solid`, `--danger-solid`, `--on-accent-solid`, `--accent-solid-edge`) for filled
  controls stay deep in both schemes, with white text. Before, the dark scheme filled buttons and your chat bubble with a bright mint
  that outshouted everything else. The light accent is now only for text, links and the focus ring. The dark scheme has a deeper
  page colour and more distance between page, surface and border, so cards no longer read as flat.
- **Type.** Headings balance their lines (`text-wrap: balance`, so no "AI" alone on the last line) and paragraphs avoid orphans
  (`text-wrap: pretty`). Headings are tighter and heavier. Page titles are a little smaller so the interview's title no longer
  dominates the chat. The home page sections have larger headings.
- **Controls.** Radios, checkboxes and the select arrow are drawn by the stylesheet, so they are clearly visible in both schemes and
  the same in every browser. A text field's focus is one ring joined to its border, with a soft halo (before: a ring, a gap, then
  the border). A disabled button is neutral grey with a readable label. Before, it was a washed-out teal with white text.
- **Header and footer.** Nav items are pills: hover tints the pill, and the current page is an accent-tinted pill instead of an
  underline. The language switch is a smaller pill with a globe icon. On a phone the items fit one row, and the language sits at the
  right edge. The footer has the product name and the development notice on the left and the links on the right.
- **Interview.** The three-step progress is a slim line of badges joined by a rule, not three boxes. The chat is one window with a
  title bar: "Interviewer · AI" and "Topic n of about 6". Bubbles are top-aligned with their avatars and have a tail corner. The
  composer has a placeholder ("Write in your own words…") and grows with the text. Send sits on the same row as the hints. Notices
  appear above the field. The Enter hint is hidden on touch screens, where there is no Shift+Enter. Consent is one card with the
  three facts as a numbered list divided by hairlines. On the result, the record's values line up, the draft labels are muted
  (the accent is kept for the actions), and on a phone Save and Delete are full width and spaced apart.
- **Other pages.** The developer cards are whole-card links with an arrow and a hover lift. The account card titles are one size.
  The delete-account fields are narrower and the button sits under them. The 404 and error pages are a centred message with a
  "404" tag. The local-development box is a quiet dashed panel instead of a loud amber one.
- **Motion.** Buttons press down by a pixel and filled buttons gain a shadow on hover. Transitions move only shadow and position,
  never a colour, because the suite measured a border mid-transition below 3:1 (see below). Everything is still off under
  `prefers-reduced-motion`.

## Before and after

| Screen | Before | After |
|---|---|---|
| Start page | [desktop](before-home-signed-out-desktop.png) · [mobile](before-home-signed-out-mobile.png) · [dark](before-dark-home-signed-out-desktop.png) | [desktop](after-home-signed-out-desktop.png) · [mobile](after-home-signed-out-mobile.png) · [dark](after-dark-home-signed-out-desktop.png) · [dark mobile](after-dark-home-signed-out-mobile.png) |
| Sign in | [desktop](before-login-desktop.png) · [mobile](before-login-mobile.png) | [desktop](after-login-desktop.png) · [mobile](after-login-mobile.png) · [Polish mobile](after-login-pl-mobile.png) |
| Interview start | [desktop](before-interview-start-desktop.png) · [mobile](before-interview-start-mobile.png) · [dark](before-dark-interview-start-desktop.png) | [desktop](after-interview-start-desktop.png) · [mobile](after-interview-start-mobile.png) · [dark](after-dark-interview-start-desktop.png) |
| Consent | [desktop](before-interview-consent-desktop.png) · [mobile](before-interview-consent-mobile.png) | [desktop](after-interview-consent-desktop.png) · [mobile](after-interview-consent-mobile.png) |
| Chat | [desktop](before-interview-chat-desktop.png) · [mobile](before-interview-chat-mobile.png) · [dark](before-dark-interview-chat-desktop.png) · [dark mobile](before-dark-interview-chat-mobile.png) | [desktop](after-interview-chat-desktop.png) · [mobile](after-interview-chat-mobile.png) · [dark](after-dark-interview-chat-desktop.png) · [dark mobile](after-dark-interview-chat-mobile.png) · [Polish mobile](after-interview-chat-pl-mobile.png) |
| Chat, 503 | | [desktop](after-interview-chat-503-desktop.png) · [mobile](after-interview-chat-503-mobile.png) |
| Delete dialog | [dark](before-dark-interview-delete-dialog-desktop.png) · [dark mobile](before-dark-interview-delete-dialog-mobile.png) | [dark](after-dark-interview-delete-dialog-desktop.png) · [dark mobile](after-dark-interview-delete-dialog-mobile.png) |
| Result | [desktop](before-interview-result-desktop.png) · [mobile](before-interview-result-mobile.png) · [dark](before-dark-interview-result-desktop.png) | [desktop](after-interview-result-desktop.png) · [mobile](after-interview-result-mobile.png) · [dark](after-dark-interview-result-desktop.png) · [Polish](after-interview-result-pl-desktop.png) |
| After deleting | [desktop](before-interview-deleted-desktop.png) · [mobile](before-interview-deleted-mobile.png) | [desktop](after-interview-deleted-desktop.png) · [mobile](after-interview-deleted-mobile.png) |
| No credit (402) | | [desktop](after-interview-402-desktop.png) · [mobile](after-interview-402-mobile.png) |
| Account | [desktop](before-account-desktop.png) · [mobile](before-account-mobile.png) · [dark](before-dark-account-desktop.png) | [desktop](after-account-desktop.png) · [mobile](after-account-mobile.png) · [dark](after-dark-account-desktop.png) |
| For developers | [desktop](before-developers-desktop.png) · [mobile](before-developers-mobile.png) | [desktop](after-developers-desktop.png) · [mobile](after-developers-mobile.png) |
| 404 | [desktop](before-not-found-desktop.png) · [mobile](before-not-found-mobile.png) | [desktop](after-not-found-desktop.png) · [mobile](after-not-found-mobile.png) |
| Start page, signed in, Polish | | [desktop](after-home-signed-in-pl-desktop.png) · [mobile](after-home-signed-in-pl-mobile.png) |

Accessibility evidence after the change: [focus on Continue](after-focus-start-continue-desktop.png),
[focus on a choice row](after-focus-choice-row-desktop.png), [the dialog opens on Cancel](after-focus-dialog-cancel-desktop.png),
and [200% zoom, the Polish chat](after-zoom200-interview-chat-pl-desktop.png).

## Rejected findings

Some reviewer findings were wrong or would have changed more than the look:

- "The delete has no confirmation". It does: a modal dialog with Cancel focused.
- "Disabled labels are below 4.5:1". The muted text on the disabled surface is about 6:1 in light and 7:1 in dark. Disabled controls
  are exempt from the contrast rules anyway.
- "Put the language in a segmented control" and "move Sign out into an account menu". These are behaviour changes, and the suite
  pins the header's Tab order.
- "Make the drafts editable", "show a live countdown", "a status line on the signed-in start page", "hide the nav on consent",
  "disable Start on a 403". These are product or behaviour changes outside a visual pass.
- Copy rewrites (the Polish gendered "zalogowany/zalogowana", the omitted-drafts sentence). The Polish catalogs are waiting for a
  native speaker's review (UI-UX backlog item 1), so they are not rewritten piecemeal here.

## Checks run

- `cd web && pnpm lint && pnpm typecheck && pnpm test && pnpm build`: clean, 388 unit tests pass.
- `cd tests/e2e && pnpm test`: 166 of 166 pass, including axe on every page and every interview state (light and dark, English and
  Polish), the 3:1 control-border and focus-ring measure, a visible focus on every Tab stop, the Tab order, reduced motion, and no
  sideways scroll at 390 px and at 200% zoom. Both accessibility specs also passed twice more in a row (`--repeat-each 2`).
- The suite caught three things during this pass, and all three were fixed in the design, not in the tests:
  1. The developer cards first moved the focus ring to the card, so the focused link had no ring.
  2. A `border-color` transition was measured mid-frame below 3:1, so transitions no longer animate colour.
  3. In the dark scheme the deep-teal Save control's edge was 2.7:1 against its panel, so filled controls got a lighter edge token.

No test was skipped or loosened.

## Not checked

- The design is mine, judged from screenshots with the help of reviewer agents. No designer and not the owner has reviewed it.
- Firefox and Safari. Everything was rendered in Chromium only. `field-sizing: content` (the growing composer) and `text-wrap:
  pretty` are not supported everywhere yet. Where they are missing the composer keeps a fixed height and the text wraps as before.
  Neither was seen in those browsers.
- Real touch devices: the hidden Enter hint relies on `(hover: none) and (pointer: coarse)`, which the suite does not emulate.
- Windows high-contrast (forced colours) mode. The radios and checkboxes are now drawn by the stylesheet. In forced colours the
  browser should repaint their borders and the checked fill, but nobody has looked.
- A screen reader. The structure is unchanged except the chat title bar, which adds "Interviewer AI" once above the log. OP-16 still
  owes a person's check.
- The Signals, CLI, Connect, verify-email and delete-submission pages were not captured. They inherit the new tokens and controls,
  and the suite's axe and contrast checks cover them, but nobody looked at them.
