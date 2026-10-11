# 0082. The portal in Polish and English, and a header for the former employee

- Status: accepted
- Date: 2026-10-11
- Principle or guide served (or deviated from): `FRONTEND-BFF` §2 (runtime configuration, nothing baked at build time: the language and the dev hints are read per request), ADR-0051 (message catalog: "a second language is a new file with the same keys"; its revisit trigger fired), P8 (a development-only aid is visible and off by default)

## Context

The [UI audit of 2026-10-11](../ux/audit-2026-10-11/README.md) walked the portal as a former employee and found: an English-only
portal around an interview that offers Polish; a header of seven links of which four are developer tools; a start page that leads
with the open-source description and says "Not for real people yet"; the development notice shown twice. The owner and the first
users are Polish former employees. ADR-0051 shipped English only because a complete, reviewed translation did not exist, and named
"a second language" as its trigger to revisit.

## Decision

1. **Two languages for the portal.** `lib/messages/pl.ts` is typed as `Messages` (the shape of `en.ts`), so a missing key is a
   compile error. It covers every page on the former employee's path. The developer pages (Connect, CLI, Delete a submission,
   Employer signals) stay English-only in `lib/messages/developer.ts` and say so in Polish.
2. **Which language, per request:** the `locale` cookie set by the header's switch, else `Accept-Language`, else English. Read on the
   server (`lib/i18n.ts`), so the first paint and `<html lang>` are right; client components get it from one context.
3. **The cookie is a preference, not a secret:** `SameSite=Lax`, one year, not HttpOnly (the switch writes it). It is listed on the
   privacy page. Session cookies are unchanged (HttpOnly, Strict).
4. **Header:** Interview, Account or Sign in, Sign out, language. **Footer:** Privacy, About this project, For developers, and the
   development notice once. Every developer route keeps working and is reached through `/developers`. Signing in without a
   `redirect` goes to `/interview`.
5. **Local development hints:** with `DEV_HINTS=1`, set by the AppHost for the web resource only, the sign-in page shows the
   DevSeed test account in a box labelled "Local development", and `/api/config` reports `dev.hints: true`. Without the variable, as in
   every deployment, nothing is rendered (unit test and browser test).

## Consequences

- Easier: a Polish speaker can use the whole path; adding a third language is one file.
- Harder: every new portal string needs two translations, or the build fails. The Polish is a builder's draft until a native
  speaker reviews it (UI-UX ranked backlog, item 1).
- The header now depends on whether a session cookie is present. That is a display hint only; access is still decided by the edge
  gate and the APIs. Sign-in, sign-out and consent call `router.refresh()` so the header re-renders.
- Revisit when: a third language is wanted, or the developer pages gain an audience that needs them translated.
