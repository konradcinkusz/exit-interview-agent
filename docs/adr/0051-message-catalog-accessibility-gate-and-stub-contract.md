# 0051. Message catalog, the accessibility gate, and keeping the browser-suite stub in step

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `testing-strategy` §7 (loading, empty and error states), `e2e-acceptance-testing`, `frontend-bff`.

## Context

The portal is the product's face and the place where "what this cannot do" has to be said precisely. Copy that states a fact must not drift from the design, and a
second language must not need a refactor. Accessibility needs an automated floor that can fail.

## Decision

- **Message catalog.** Every user-facing string is in `web/app/lib/messages/en.ts`, typed by `Messages` (a deep string shape), registered in `lib/messages/index.ts`.
  A second language is a new file with the same keys; a missing key is a compile error. **English only is shipped**: Polish is not provided because a complete,
  reviewed translation was not possible in this task, and a partial one is worse than none. The strings that state a fact (deletion semantics, the uniform receipt answer, the ticket
  rules, "not linked to your account") are asserted by the unit and browser tests.
- **Accessibility gate.** `@axe-core/playwright` 4.13.0 (axe-core 4.13.0, **MPL-2.0**; a dev-only test dependency in `tests/e2e`, unmodified, not shipped in any image, so the file-level
  copyleft does not reach this repository's code) runs the WCAG 2.0/2.1 A and AA rules on every page, in light and dark, and on the states reached by an action (login error, second factor,
  receipt error and result, ticket shown, consent). A mutation check (lowering a contrast token) was run and made the gate fail, so it can fail. Also asserted: the skip link is the first
  Tab stop and `main` takes focus, a form can be completed by keyboard, no horizontal overflow at 320 px. axe finds a subset of problems; it does **not** replace keyboard and screen-reader
  testing by a person (listed as open).
- **Gate behaviour kept:** the edge gate sends any non-public path to sign in, including unknown ones (fail closed, no disclosure of which paths exist). The 404 page is therefore seen by signed-in
  visitors; the test signs in first. Gated links (`/cli`, `/account`) are not prefetched: a prefetch made signed-out is answered with the redirect to `/login`, and the router replayed that cached
  redirect after sign-in (found by the browser suite).
- **The stub backend** (`tests/e2e/support/stub-backend.mjs`) mirrors the interview-service's ticket and receipt contract (status codes, problem+json `{type,title,status,code}`, the kernel's
  rate-limit body and `Retry-After`, the `X-Receipt-Code` header, the receipt code's length and checksum, the 43-character ticket, the 3-live-tickets cap) and authservice's two-factor, register,
  verify, export and consent-versions behaviour. It carries a **contract note**; **the stub must change in the same pull request as any change to those contracts**.

## Consequences

- A change to a copy string that states a fact fails a test unless the test moves with it: intended friction.
- The stub can drift from the real services; the full-stack journey against the AppHost is still a later layer. The note is the control; it is not enforced by a machine.
- Trigger to revisit: a second language (add the file); a person doing the manual accessibility pass.
