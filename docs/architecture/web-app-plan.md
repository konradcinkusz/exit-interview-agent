# Web interview app: plan

Status: **approved by the owner, being built in parallel tasks** (section 7). Decisions taken are in section 9; the contract between the service, the BFF and the page is section 10 and is the one thing the tasks must not change on their own.

## 1. What it is

**Status (2026-10-10): implemented (W2–W8), NOT deployed.** The Fly files and the deployment runbook are W10a. Open: W7 (e2e), W9 (real-model run and price), W10b, W11 (see the task table in section 7). Nothing is deployed and nothing has been run with a real model.

A hosted version of the interview for people who will not use a terminal: sign in (authservice), pay, talk to the interviewer in a chat page, and at the end read the record and the draft publishable texts (tiles). The interview logic is the existing `ExitInterviewAgent.Agent` library; this plan adds a session API around it, a chat page, payment and cost controls. It adds no new interview behaviour.

Not in scope: publishing anything to Glassdoor, Google or Reddit (the user copies the text), the employer-facing side (signals already exist), a mobile app, the verified-employment check (OP-1 stays a mock; the web app must say so).

## 2. Whose API key

**The service's own key, held on the server.** The browser never sees it. This is the model in the owner's idea (the owner's key, a price per interview). The alternative (the user pastes their own key) is kept as an optional later mode, not the default: it needs the user to own an Anthropic account, which a non-technical visitor does not.

Because the owner pays Anthropic per call, the key must be fenced:

| Control | Where | Value |
|---|---|---|
| A dedicated Anthropic workspace for this app, with a monthly spend limit set in the Anthropic console | Anthropic console | the owner picks the limit; the app sets `ANTHROPIC_WORKSPACE_ID` for it |
| The key is a Fly secret, never in the repository or the client bundle | Fly | existing rule (AGENTS.md) |
| Hard per-interview budget | protocol 1.2 | 90 000 estimated tokens, 90 model calls, 60 interviewer turns; tiles share the budget |
| One paid interview per payment; no free interviews on the shared key | InterviewService | a session is created only from a paid, unconsumed credit |
| Rate limit per account and per IP; account needs a verified email | BFF + authservice | |
| A global kill switch (env flag) that refuses new sessions | InterviewService | |

Upper bound of one interview's cost: the protocol caps estimated tokens at 90 000, so even if all of them were billed at the output price of the chosen model the cost is bounded (about $0.90 at $10 per million output tokens). The real cost has not been measured with a real model; it is measured in W9 below and the price is set from that, not guessed.

## 3. Architecture

```
browser (Next.js page, no key)
   |  cookie session (existing BFF, authservice login)
BFF  web/app  ── bearer ──>  InterviewService (new: /interviews endpoints)
                                  |  uses ExitInterviewAgent.Agent + Providers (server key)
                                  |  PostgreSQL: sessions, credits, payments (no transcript)
                                  v
                              Anthropic API
Stripe Checkout / webhook ──> InterviewService (credits)
```

- **Session API** (InterviewService, authenticated): `POST /interviews` (consumes a credit, language, tenure band; returns the opening turn), `POST /interviews/{id}/reply` (one reply in, the next interviewer turn out, or the end), `GET /interviews/{id}/result` (record, tiles), `DELETE /interviews/{id}` (withdraw and wipe). The state machine is already an in-memory object with explicit transitions; a session holds one machine and one transcript.
- **Transcript lives in server memory only** and is dropped when the interview ends or after an idle timeout (for example 30 minutes), as the CLI does. The database holds the session id, owner, status, timestamps, token counts and the credit used, never interview text. A restart loses running interviews; the user is told and the credit is refunded (see W3).
- **Result** (record JSON and tiles) is returned once to the browser and kept by the user (download as JSON and HTML). The server stores it only if the user clicks "submit to employer signals", which is the existing submission flow and stays opt-in.
- **Tiles** reuse `TileGenerator` unchanged.
- **Chat page** (`/interview`): consent screen first (the same disclosure as the CLI, in Polish and English), then a single chat column, a visible "stop and delete" button, and the result page with copy buttons for each tile and the legal notice. No analytics on interview text.
- **Streaming**: not in the first version (each turn is one request; the model answers in a few seconds).

## 4. Payment

Stripe Checkout, one-time purchase of N interview credits (default 1), webhook adds credits idempotently (event id stored). No card data touches the service. Refund path: a credit is returned when a session fails before a record exists for a reason that is the service's fault (restart, provider outage, extraction failure). A user who withdraws consent does not get a refund.

Price: set after W9 from measured cost plus a margin; the figure of $10 in the idea is a price decision, not a cost.

## 5. Privacy and legal (blocking for launch)

- Privacy policy and terms of service; processor agreement with Anthropic reviewed; retention statement ("not stored" must be true and tested).
- Consent text names Anthropic as the recipient of the conversation (existing CLI wording, adapted).
- Tile notice stays: drafts, not facts; the author is responsible for what they publish; no legal advice.
- A lawyer reviews the tile feature and the terms before the first paid interview (defamation and platform rules are the open risk). This plan does not replace that review.
- Roles under GDPR (controller or processor) are decided with the lawyer; the plan assumes the service is the controller for account and payment data and a processor-like conduit for the conversation.

## 6. Security

- Key and Stripe secrets only as Fly secrets; the existing gitleaks gate covers the repository.
- All session endpoints require a bearer from authservice (RS256 via JWKS, already implemented); a session id is bound to the account; ids are random and not guessable.
- Reply size limits already exist (`maxReplyChars`); the PII guard stays fail-closed.
- No interview text in logs or traces (a test already asserts this for the agent; W2 extends it to the endpoints).

## 7. Tasks (atomic, in order; each its own PR)

| Task | Content | Depends on | PR |
|---|---|---|---|
| W1 | ADR for the web app (platform key, no stored transcript, credit model) and this plan's decisions | owner decisions | plan #40 |
| W2 | InterviewService: `/interviews` session endpoints around the agent, in-memory session store with idle timeout, wipe on end, tests (including no text in logs) | W1 | #44 |
| W3 | Credits and payments tables, Stripe Checkout + idempotent webhook, refund rule, tests against a Stripe stub | W2 | #46 |
| W4 | Cost controls: kill switch, per-account and per-IP rate limits, verified-email gate, spend metrics (counts only) | W2 | #45 |
| W5 | BFF routes for the session API (no key, cookies only) | W2 | #43 (with W6) |
| W6 | Chat page: consent, chat, stop and delete, result page with tiles and copy buttons; Polish and English | W5 | #43 (with W5) |
| W7 | e2e journey (Playwright, stub provider): pay (stub), interview, result, delete | W3, W6 | no PR yet |
| W8 | Legal pack: privacy policy, terms, consent wording, legal review recorded in RELEASE-GATE | W1 | #42 |
| W9 | Real-model run: 10 interviews in Polish, measured cost, quality notes, price decision; release gate item 15 updated | W2 | not run (owner) |
| W10 | Deploy to Fly (staging first), spend limit set in the Anthropic console, runbook | W3..W8 | W10a: this PR (files, runbook, status); W10b: no PR yet |

W9 is the gate: if the Polish interview or the tiles are not good with a real model, nothing after it is worth launching.

## 8. What stays unmeasured until built

Real-model quality in Polish, real cost per interview, completion rate, whether people pay, whether tiles are used, extraction reliability with the production model.

## 9. Decisions

Taken (2026-10-10, the owner said: build everything for the former employee, all decisions not named are the builder's):

1. Customer: the leaving employee. No employer-facing product here.
2. The key: the service's own key, server side only (section 2). The user-key mode is not built.
3. Price: a configuration value (`Billing:PriceMinorUnits`, `Billing:Currency`), no number is hard-coded; the first interview is not free. The value is set after W9.
4. Model: configurable (`Interviews:Provider`, `Interviews:Model`), default the one the CLI documents for Anthropic; chosen for production after W9.
5. Hosting: Fly, as the repository already declares; the build adds files and a runbook and **deploys nothing** (AGENTS.md). Domain name: the owner's, set at deploy time.
6. Legal: W8 delivers drafts (privacy policy, terms, consent wording) and a checklist. Review by a lawyer is recorded in RELEASE-GATE as **NOT RUN, owner action**; no paid interview before it.

Still the owner's, outside the code: the Anthropic spend limit, the Stripe account and keys, the domain, the lawyer, and W9 (needs a key).

## 10. Contract (fixed; a task that needs a change proposes it in its PR and does not diverge)

All routes are in `ExitInterviewAgent.InterviewService` under `/api/v1`, require the account bearer (`AuthPolicies.Account`) unless marked anonymous, return `application/json`, errors are RFC 9457 problem documents with a stable `code`. Types live in `ExitInterviewAgent.Contracts` (`InterviewContracts.cs`); the BFF mirrors them in `web/app/lib/interview-contract.ts`.

| Method and path | Request | Success | Errors |
|---|---|---|---|
| `POST /interviews` | `{ "language": "pl"\|"en", "tenure": "lt_6m"\|"6m_1y"\|"1y_3y"\|"3y_5y"\|"5y_10y"\|"gt_10y" }` | `201 { id, status, language, expiresAt, turn }` (the opening turn) | `400 invalid_request` (bad language or tenure), `402 payment_required` (no credit), `429 rate_limited`, `503 interviews_disabled` (kill switch or provider not configured), `409 interview_in_progress` (one open session per account) |
| `POST /interviews/{id}/reply` | `{ "text": string }` (1..2000 chars) | `200 { status, turn?, ending? }`. A closing or stop turn is returned with `status` still `in_progress` and `ending` null; the tiles are made after it, and `status` turns `completed` or `stopped` when they are ready (poll `GET /interviews/{id}`) | `404 not_found`, `409 interview_ended`, `409 reply_in_progress` (a reply is already pending), `410 gone`, `422 reply_invalid`, `429`, `503 provider_unavailable` (also when the session failed on this reply) |
| `GET /interviews/{id}` | | `200 { id, status, language, turnCount, expiresAt }` | `404`, `410 gone` |
| `GET /interviews/{id}/result` | | `200 { record, tiles, usage }` once `status` is `completed` | `404`, `409 not_completed`, `410 gone` |
| `DELETE /interviews/{id}` | | `204` (the transcript and result are wiped) | `404` |
| `GET /credits` | | `200 { balance }` | |
| `POST /checkout` | `{ "quantity": 1..10 }` | `200 { url }` (provider-hosted payment page) | `503 billing_disabled` |
| `POST /webhooks/payments` (anonymous, signature verified) | provider event | `200` (idempotent by event id) | `400 bad_signature` |

- `status` is `awaiting_consent`, `in_progress`, `completed`, `stopped` (consent refused or withdrawn: nothing kept) or `failed` (service fault; the credit is returned).
- `turn` is `{ index, kind, text }` with `kind` one of `opening`, `consent_reask`, `topic`, `probe`, `clarification`, `deep_probe`, `redirect`, `close`, `stop`. `ending` is `{ reason }` on the last turn.
- `tiles` is `{ items: [{ kind, text }], dropped: [{ code }], notice }` where `kind` is `glassdoor`, `google_review`, `reddit`, `short_note`, `overview`, `facts` as the CLI renders them (`TileKind`); `notice` is the same legal notice the CLI prints, in the interview's language.
- `usage` is counts only: `{ modelCalls, tokensEstimated }`. No text appears in any log, trace tag or metric.
- A session expires 30 minutes after its last request; after completion the result stays readable for 30 minutes, then it is wiped. A restart loses open sessions; the next request is `410 gone` and the credit is returned.
- The interview runs in a background task per session: an `IInterviewee` backed by a channel feeds `InterviewRunner`; `reply` writes to the channel and waits (with a timeout) for the next interviewer turn.
- Credits are consumed when the session is created and returned when it ends as `failed`.
- Implementation (W2, ADR-0076): the closing turn is returned at once, not after the tiles; `404` carries the code `not_found` on every route (added to the contract); a restart leaves the id unknown, so the answer is `404` (not `410`, which needs the session to be remembered across restarts; W3 records session ids in the ledger and then returns the credit with `410`).
