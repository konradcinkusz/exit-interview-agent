# 0050. Two-factor sign-in, registration, email verification and data export through the BFF

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `identity-and-accounts`, `frontend-bff` §3, threat model T-12, [ADR-0013](0013-bff-session-refresh-rotation-and-consent-gate.md) (removes its "501" limit).

## Context

T2's BFF answered `501 two_factor_not_supported` when authservice returned a two-factor challenge, so an account with two-factor could not sign in.
Registration, email verification and the data export had no BFF route. Contracts read from the authservice source (`TwoFactorController`, `AuthController`),
not assumed:

- `POST /api/v1/auth/login` answers `200 {requiresTwoFactor:true, challengeToken, expiresIn:300}` for a two-factor account, tokens otherwise.
- `POST /api/v1/auth/2fa/login` takes `{challengeToken, code?, recoveryCode?}` (exactly one), rate-limited, and answers `200` tokens, `400` (neither supplied), `401`
  with `error` text for a wrong code, a dead challenge **and** a lockout (no code field distinguishes them), `429`. A wrong second factor counts toward lockout.
- `POST /api/v1/auth/register` needs the exact Terms and Privacy versions in force (`GET /api/v1/auth/consents/versions`, anonymous), answers `200` tokens (no verification
  needed) or `202` (email sent). `POST …/verify-email {email, token}` and `…/resend-verification {email}`. `GET …/export` returns the account export as an attachment.

## Decision

- **The challenge never reaches page JavaScript.** The password step stores it in an HttpOnly, SameSite=Strict cookie `eia_2fa` (lifetime capped at the 5 minutes
  authservice gives it), and answers the page `{twoFactorRequired:true}`. `POST /api/auth/two-factor` reads the cookie, takes only a `code` or a `recoveryCode`
  (bounded, never both), calls authservice, and on success sets the normal session and clears the cookie. A wrong code keeps the challenge (authservice counts it); a dead
  challenge or a lockout clears it and the page returns to the first step with a message. The three `401` meanings are told apart by authservice's message text (the only
  signal it offers); an unrecognised text is treated as "wrong code", the safe reading.
- **Registration records acceptance of versions the BFF fetches itself** at that moment, so a page cannot claim acceptance of a version it was not shown. No tokens are
  issued at sign-up: the account signs in afterwards, so one path sets a session. The 202/200 difference becomes "check your email" vs "you can sign in".
- **Email verification** lands on `/verify-email?token=&email=`; the token is sent once to the BFF and removed from the address bar. One answer for a bad token and an
  unknown address (authservice's choice, kept: no existence oracle).
- **Export** is a plain download link to `GET /api/auth/export` (streamed, attachment name passed through, `no-store`), gated by the session. The account page states that
  **submissions are not in it** because they are not linked to the account.
- Two-factor **enrolment** screens are out of scope (authservice supports it; the portal only signs in with it): backlog.
- MFA policy (optional or required) is not decided here: authservice makes it optional; the portal does not require it. Recorded under T-12 as open.

## Consequences

- An account with two-factor can sign in, with an authenticator code or one recovery code. Every route is unit-tested against authservice's response shapes and covered in the
  browser suite against the stub, which mirrors the authservice behaviour above. **Not run against the real authservice image** (no container runtime in this session).
- The lockout text match is brittle against a wording change in authservice; the unit test pins the three texts, and a change shows up there first.
