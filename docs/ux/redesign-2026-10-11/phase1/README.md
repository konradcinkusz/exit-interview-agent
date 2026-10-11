# Redesign, phase 1: navigation, start page, footer, languages

Phase 1 of the redesign that follows the [UI audit of 2026-10-11](../../audit-2026-10-11/README.md). It covers the audit's changes
1 to 4 and part of 9 and 10 for the pages around the interview. The interview page itself (start, consent, chat, result, delete) is
phase 2; the accessibility sweep and the Polish end-to-end journey are phase 3.

Screenshots: full page, production build against the e2e stub, light scheme, 1280×800 (`-desktop`) and 390×844 (`-mobile`).
`before-*` is `main` at `6c7f504`; `after-*` is this branch. Polish screens (`-pl`) exist only after: before, every page was English.
Captured with `tests/e2e/capture/` (see the comment in `playwright.capture.config.ts`), which runs the web app with `DEV_HINTS=1`,
so `after-login*` shows the local-development box; without that variable it is not rendered. Two PNGs were reduced to a 256-colour
palette to stay under 250 KB (`after-privacy-*`).

| Screen | Before | After |
|---|---|---|
| Start, signed out | [desktop](before-home-signed-out-desktop.png) · [mobile](before-home-signed-out-mobile.png) | [desktop](after-home-signed-out-desktop.png) · [mobile](after-home-signed-out-mobile.png) · [PL](after-home-signed-out-pl-desktop.png) |
| Start, signed in | [desktop](before-home-signed-in-desktop.png) · [mobile](before-home-signed-in-mobile.png) | [desktop](after-home-signed-in-desktop.png) · [mobile](after-home-signed-in-mobile.png) · [PL](after-home-signed-in-pl-desktop.png) |
| Sign in | [desktop](before-login-desktop.png) · [mobile](before-login-mobile.png) | [desktop](after-login-desktop.png) · [mobile](after-login-mobile.png) · [PL](after-login-pl-mobile.png) |
| Sign up | [desktop](before-register-desktop.png) · [mobile](before-register-mobile.png) | [desktop](after-register-desktop.png) · [mobile](after-register-mobile.png) · [PL](after-register-pl-desktop.png) |
| Consent | [desktop](before-consent-desktop.png) · [mobile](before-consent-mobile.png) | [desktop](after-consent-desktop.png) · [mobile](after-consent-mobile.png) |
| Account | [desktop](before-account-desktop.png) · [mobile](before-account-mobile.png) | [desktop](after-account-desktop.png) · [mobile](after-account-mobile.png) · [PL](after-account-pl-desktop.png) |
| Privacy | [desktop](before-privacy-desktop.png) · [mobile](before-privacy-mobile.png) | [desktop](after-privacy-desktop.png) · [mobile](after-privacy-mobile.png) |
| 404 | [desktop](before-not-found-desktop.png) · [mobile](before-not-found-mobile.png) | [desktop](after-not-found-desktop.png) · [mobile](after-not-found-mobile.png) |
| About (new) | | [desktop](after-about-desktop.png) · [mobile](after-about-mobile.png) |
| For developers (new) | | [desktop](after-developers-desktop.png) · [mobile](after-developers-mobile.png) |

## What changed

- **Header:** the product name, Interview, Account and Sign out (or Sign in), and a language switch. The four developer links and
  Privacy left the header. Keyboard order on `/`: skip link, product name, Interview, Sign in, Polski (asserted).
- **Footer:** Privacy, About this project, For developers, and the development notice, which now appears once per page and nowhere else.
- **Start page:** "Give your exit interview to an AI", one paragraph on what happens, one primary button "Start your interview"
  (to `/interview` when signed in, through sign-in when not), one card on what is kept and what is not (Anthropic receives the
  conversation; it is held in memory; the result is wiped after 30 minutes), four steps, what this is not. "Not for real people yet"
  is gone. The open-source description and the CLI/AI-client facts moved to `/about`; the developer pages are listed on `/developers`.
- **Sign in:** one line on why ("Sign in to start your exit interview."); sign-up keeps the redirect; signing in without a redirect
  goes to `/interview`. With `DEV_HINTS=1` (set by the AppHost for the web resource only) a dashed "Local development" box shows the
  DevSeed account and the dashboard command that creates it.
- **Sign up and consent:** plain-language checkbox labels with the version on a line under them; buttons grouped.
- **Account:** sections as cards (session, your interview, export, delete account in a red-bordered section). It shows the account ID
  in small print; it cannot show the email address because the access token carries none (not changed: that would need an API).
- **Privacy:** a new first section on the web interview (Anthropic, memory, 30 minutes, nothing in browser storage) and a cookies
  section that lists the language cookie. The existing facts are kept, now headed as the AI-client and CLI path.
- **Languages:** a typed Polish catalog for every page on the path; default from the browser, a switch in the header, remembered in a
  `locale` cookie (`SameSite=Lax`, one year). Developer pages stay English and say so in Polish. ADR-0082; `docs/ux/UI-UX.md` updated.
- **Look:** colour, type, spacing and shape tokens in `globals.css`, light and dark; one button system (primary, secondary, danger),
  44 px controls, bordered choice rows for checkboxes and radios, a styled select, cards, notices and alerts.

## How it was checked

- `cd web && pnpm lint && pnpm typecheck && pnpm test && pnpm build`: pass (unit tests include the locale resolution, the Polish
  catalog's completeness and the dev-hints gate).
- `cd tests/e2e && pnpm test` (Chromium from `/opt/pw-browsers`): pass, including the updated specs and the new `portal.spec.ts`;
  axe (WCAG 2.0/2.1 A and AA) on every portal page in light and dark, including `/about` and `/developers`; 320 px without
  horizontal scroll on the new pages.

## Not checked

- **.NET gates not run locally:** the one-line AppHost change (`DEV_HINTS`) was not built here: the .NET SDK download is blocked by
  this environment's network policy. CI runs `dotnet build -warnaserror`, `dotnet format` and `dotnet test`.
- The dev-hints box was seen only against the stub (via `DEV_HINTS=1` in the capture config), not in the running AppHost.
- The Polish copy has not been reviewed by a native speaker.
- Screen reader, 200% browser zoom on a real browser, and Windows high-contrast mode: not tried. Dark mode was checked by axe only;
  the screenshots are light.
- The interview page has the new header and tokens but not its redesign (phase 2); its own screens are not in this set.
