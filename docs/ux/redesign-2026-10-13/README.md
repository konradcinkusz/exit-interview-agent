# UI round, 2026-10-13: the chat as a conversation, calmer platform drafts

This continues [the 2026-10-12 refinement](../redesign-2026-10-12/README.md). It covers the screens added since then: the chat's
phase line, the consent quick answers and the interview dynamics 1.3 turns (warm-up, 1-5 rating, check-in, support and legal
notices), and the per-platform drafts on the result (with the "Other platforms" list from #82). The flows, the copy rules and the
API are unchanged: drafts are still drafts, not facts, and the AI is still disclosed in the title bar and the opening. No dependency
was added.

Screenshots: full page, production build against the e2e stub, reduced motion, 1280×800 (`-desktop`) and 390×844 (`-mobile`).
`before-*` is `main` before this branch, `after-*` is this branch, `-dark-` is the dark scheme and `-pl` is Polish. Each PNG is
palette-reduced to stay under 250 KB, so small colours shift slightly. A few very tall pages (the long drafts) are also scaled down.

## How it was reviewed

1. New stub fixtures made the 1.3 states reachable: a `dynamics` switch serves the protocol's warm-up, the 1-5 rating question,
   the check-in, the support notice and the legal notice, verbatim from `interview-protocol*.v1.json`. A `longDrafts` switch
   lengthens every draft. The capture spec gained 19 states: quick answers, rating, check-in, support, legal, long answers, the
   typing state, long drafts, copied, Other platforms open, result loading, result failed and stopped.
2. I captured every state in light and dark (`tests/e2e/capture`). Five reviewer agents each took one area: home, auth and
   account; start, consent and quick answers; chat states; the platform drafts; and the rest of the result plus the end states.
   They read the PNGs, ran their own Playwright probes (keyboard order, focus, contrast, 390 px, 200% zoom, clipboard), and
   listed defects with element, screen, reason and fix.
3. I checked their findings against the PNGs and the code, and implemented the ones that held up. The rejected ones are listed
   below.
4. Two verifier agents compared before and after on the new build and probed again. They found three regressions: chips 40 px
   tall, a phase line that wrapped onto a "·", and "Copied" coming back when the warning was ticked again. All three are fixed,
   and the last one is now pinned by a test.

## What changed

**Chat**

- **A conversation, not a form.** The bubbles no longer print "Interviewer" and "You". The side, the avatar and the colour say
  who speaks, and the title bar says once that the interviewer is an AI. The name is still in the text that screen readers read
  ("You: …"). A change of speaker gets a little more air than two lines from the same speaker.
- **The conversation sits at the bottom of the window,** next to the answer box, like a messenger. Before, the opening question
  floated at the top with an empty band between it and the composer. This uses an auto margin, not `justify-content`, so the top
  of a long log still scrolls into reach.
- **Quick answers are chips** (pill, 44 px tall) on the person's side, right under the question they answer. Before, they were
  outlined form buttons with no space above the composer. They disappear once the person has answered, including when that
  answer failed and waits to be sent again.
- **1-5 rating chips.** When the interviewer's last turn is a 1-5 scale question, five number chips appear under a caption
  ("Your rating, from 1 to 5"). A chip sends its number as the answer, and typing still works. The wire has no turn kind for the
  scale move, so `isRatingQuestion` recognises the wording: a scale named together with 1 and 5 in one sentence, in English or
  Polish. It is unit-tested, including sentences that must not match. A miss only means no chips.
- **The phase line is a quiet caption,** not a grey pill that wrapped into a blob on a phone. When it wraps, a line never
  starts with "·".
- **No jump on start.** On a phone the page used to open scrolled down to the composer, which hid the title bar and the opening
  question. Now the page goes to the top when the chat appears, and it brings the composer into view only on later messages.
- At phone width the Enter hint is hidden, so the character count and Send share one row. The typing bubble is a little taller.

**Platform drafts**

- **One card anatomy.** Each card has a head (the platform's name and its risk in words), a single callout with the warning and,
  for a higher risk, the "I have read this warning" box and a lock hint, the texts, and a footer. The footer holds "Copy all", the
  link to the site with an external-link icon, and "You fill in on the site" as one inline line. Before, the warning, the box,
  the hint, every section, "Copy all", the list and the link were stacked rows separated by hairlines, which read like a form.
- **Texts as sheets:** a measure of at most 68 characters (before, about 90 on desktop), a little more leading, and no
  separator lines between sections.
- **Lighter per-field copy.** The section buttons are the quiet variant, so "Copy all" is the card's main action. While the
  warning is not acknowledged, the texts get a dashed edge and stay fully readable.
- Medium risk is a neutral pill and higher risk an amber one, and both say the risk in words. Only a higher-risk card gets the
  amber callout, so a page of cards is not all amber.
- **"Other platforms"** (#82) is a quiet disclosure row with a chevron, closed by default. Its order logic was not touched.

**Copy feedback**

- "Copied" appears in green with a check next to the button, and the button's icon turns into a check. After 4 seconds it goes
  away. Before, it stayed forever, so a page could say "Copied" next to several buttons at once.
- **A second copy is announced again:** each copy puts a new node in the `role="status"` region. Before, the same text was
  set twice, which changed nothing, so a screen reader said nothing.
- Unticking the warning clears "Copied", and ticking it again does not bring it back.
- "Copy all" is named with its card ("Copy all: LinkedIn"), and the acknowledgement box is described by the lock hint.

**Focus and end states**

- When the interview ends, the result's heading takes the focus. Before, focus fell to `<body>` when the chat unmounted. The
  "interview ended" screen does the same.
- After "Delete everything now", the page scrolls to the top and the deletion notice takes the focus. Before, the notice was
  about 350 px above the viewport and focus was on `<body>`.
- A heading or notice that takes the focus by script has no ring, because it is not a Tab stop. Every Tab stop still shows one.
- The "interview ended" screen is a card with an icon instead of a bare paragraph.

**Elsewhere**

- Header items turn teal on hover. Before, the colour did not change.
- Footer links are 44 px tall on a phone. They were 32 px.

## Before and after

| Screen | Before | After |
|---|---|---|
| Consent turn, quick answers | [desktop](before-interview-chat-quick-desktop.png) · [mobile](before-interview-chat-quick-mobile.png) · [Polish mobile](before-interview-chat-quick-pl-mobile.png) · [dark Polish](before-dark-interview-chat-quick-pl-mobile.png) | [desktop](after-interview-chat-quick-desktop.png) · [mobile](after-interview-chat-quick-mobile.png) · [Polish mobile](after-interview-chat-quick-pl-mobile.png) · [dark Polish](after-dark-interview-chat-quick-pl-mobile.png) |
| Rating question | [desktop](before-interview-chat-rating-desktop.png) · [mobile](before-interview-chat-rating-mobile.png) · [Polish mobile](before-interview-chat-rating-pl-mobile.png) · [dark](before-dark-interview-chat-rating-mobile.png) | [desktop](after-interview-chat-rating-desktop.png) · [mobile](after-interview-chat-rating-mobile.png) · [Polish mobile](after-interview-chat-rating-pl-mobile.png) · [dark](after-dark-interview-chat-rating-mobile.png) |
| Check-in | [mobile](before-interview-chat-checkin-mobile.png) | [mobile](after-interview-chat-checkin-mobile.png) |
| Support notice | [desktop](before-interview-chat-support-desktop.png) · [mobile](before-interview-chat-support-mobile.png) · [Polish mobile](before-interview-chat-support-pl-mobile.png) · [dark](before-dark-interview-chat-support-desktop.png) | [desktop](after-interview-chat-support-desktop.png) · [mobile](after-interview-chat-support-mobile.png) · [Polish mobile](after-interview-chat-support-pl-mobile.png) · [dark](after-dark-interview-chat-support-desktop.png) |
| Legal notice | [mobile](before-interview-chat-legal-mobile.png) | [mobile](after-interview-chat-legal-mobile.png) |
| Long answer | [desktop](before-interview-chat-long-desktop.png) · [mobile](before-interview-chat-long-mobile.png) | [desktop](after-interview-chat-long-desktop.png) · [mobile](after-interview-chat-long-mobile.png) |
| Waiting for the interviewer | [mobile](before-interview-chat-typing-mobile.png) | [mobile](after-interview-chat-typing-mobile.png) |
| 429 with a kept answer | [desktop](before-interview-chat-429-desktop.png) · [mobile](before-interview-chat-429-mobile.png) | [desktop](after-interview-chat-429-desktop.png) · [mobile](after-interview-chat-429-mobile.png) |
| Result | [desktop](before-interview-result-desktop.png) · [mobile](before-interview-result-mobile.png) · [Polish](before-interview-result-pl-desktop.png) | [desktop](after-interview-result-desktop.png) · [mobile](after-interview-result-mobile.png) · [Polish](after-interview-result-pl-desktop.png) |
| Long drafts | [desktop](before-interview-result-long-desktop.png) · [mobile](before-interview-result-long-mobile.png) · [Polish mobile](before-interview-result-long-pl-mobile.png) · [dark](before-dark-interview-result-long-desktop.png) | [desktop](after-interview-result-long-desktop.png) · [mobile](after-interview-result-long-mobile.png) · [Polish mobile](after-interview-result-long-pl-mobile.png) · [dark](after-dark-interview-result-long-desktop.png) |
| Copied | [desktop](before-interview-result-copied-desktop.png) · [mobile](before-interview-result-copied-mobile.png) · [dark](before-dark-interview-result-copied-desktop.png) | [desktop](after-interview-result-copied-desktop.png) · [mobile](after-interview-result-copied-mobile.png) · [dark](after-dark-interview-result-copied-desktop.png) |
| Other platforms, open | (did not exist) | [desktop](after-interview-result-other-desktop.png) · [mobile](after-interview-result-other-mobile.png) · [dark](after-dark-interview-result-other-desktop.png) |
| Result failed to load | [desktop](before-interview-result-failed-desktop.png) | [desktop](after-interview-result-failed-desktop.png) |
| Stopped | [desktop](before-interview-stopped-desktop.png) · [mobile](before-interview-stopped-mobile.png) | [desktop](after-interview-stopped-desktop.png) · [mobile](after-interview-stopped-mobile.png) |
| After deleting | [desktop](before-interview-deleted-desktop.png) · [mobile](before-interview-deleted-mobile.png) | [desktop](after-interview-deleted-desktop.png) · [mobile](after-interview-deleted-mobile.png) |
| Start page | [mobile](before-home-signed-out-mobile.png) | [mobile](after-home-signed-out-mobile.png) |

200% zoom, Polish: [chat](after-zoom200-interview-chat-pl-desktop.png) · [result](after-zoom200-interview-result-pl-desktop.png).

## Rejected or deferred findings

- **"Merge the generic draft texts into the platform drafts; their ungated Copy bypasses the high-risk gate."** Which sections
  the result shows comes from the service (ADR-0083). The generic items are a different product of the service, not a copy of a
  gated draft (the stub happens to reuse one sentence). This is a product decision, not a visual fix. Flagged here for the owner.
- **"The English support notice names no helpline"** and **"the 429 notice says the wait twice".** Both are copy. The support
  text is the protocol's (`interview-protocol.v1.json`), and copy is not rewritten in a visual pass.
- **"The quick answers do not fit the opening question."** The stub's opening is an older text that asks about onboarding. The
  1.3 opening asks for consent, and the Yes/No and Polish address-form answers belong to that question.
- **"Use `aria-disabled` so the gated copy buttons stay focusable."** That changes how the gate behaves, and the suite measures
  enabled controls at 3:1. Instead, the reason (the lock hint) now describes both the box and the disabled buttons.
- **"Focus the answer box after a chip opens the keyboard on a phone."** The quick-answers spec pins the focus moving to the
  answer box, so a keyboard user is never left on a button that disappeared. Not verified on a real touch device.
- **"Scroll the chat itself to the top on start at 200% zoom."** At 640×400 CSS px the page goes to its top, and the chat's
  title is just below the fold. That is the page's normal reading order, not a jump.
- **Rating chips before the answer box in the Tab order.** The order follows reading order (question, its answers, the box) on
  purpose.
- Home, auth and account findings that are layout or product changes: one header row on phones, two left edges on prose pages,
  a forgot-password path, show/hide for passwords, and the Polish "zalogowany/zalogowana" break (the Polish catalogs wait for a
  native speaker's review, UI-UX backlog item 1). The result findings A7 (a skeleton in the result's shape), A9 (a jump bar for
  long results) and A10 ("draft" used for three things) are deferred to a later round, and A10 is a copy decision.

## Checks run

- `cd web && pnpm lint && pnpm typecheck && pnpm test && pnpm build`: clean, 437 unit tests pass. New tests:
  `isRatingQuestion`, `canRate`, and the quick answers hiding after a failed answer.
- `cd tests/e2e && pnpm test`: 173 of 173 pass, including axe on every page and every interview state (light and dark, English and
  Polish), the 3:1 control-border and focus-ring measure, a visible focus on every Tab stop, reduced motion, and no sideways scroll
  at 390 px and 200% zoom. The two accessibility specs, the drafts spec and the quick-answers spec also passed twice more in a row
  (`--repeat-each 2`).
- New e2e assertions:
  1. The rating chips: no chips on the warm-up, 1-5 on the scale question, a press sends the number, the chips then go, and focus
     is in the box.
  2. Focus on the result heading, and the opening question in view on a phone after Start.
  3. The deletion notice is focused and in view.
  4. Ticking the warning again does not bring "Copied" back.
- Tests changed for the new UI: none needed. The speaker names, "Copied" in its status region and "Copy all" as a name substring
  are all still where the suite reads them. No test was skipped or loosened.

## Not checked

- The design is mine, judged from screenshots with the help of reviewer agents. No designer and not the owner has reviewed it.
- Chromium only. Firefox and Safari were not looked at. `text-wrap: pretty`, `field-sizing` and `:has()` behave as described in
  the 2026-10-12 notes.
- Real touch devices: the chips' tap feel, the on-screen keyboard after a chip, and the hidden Enter hint.
- A screen reader. That the speaker name is still read ("You: …"), that each copy is announced again, and that the focused
  heading and notice are announced were checked in the DOM and with mutation observers, not heard. OP-16 still owes a person's
  check.
- Windows high-contrast (forced colours) mode: the dashed edge of a locked text and the chips were not seen there.
- The rating chips depend on the interviewer's wording. A model that phrases the scale question without "scale" and both
  numbers gets no chips. That is safe, since typing works, but it has not been measured on real model output.
- The `interview-unverified-403` capture times out intermittently in the capture run (the suite's own 403 test passes). Its
  screen did not change in this round.
