# 0077. Credits ledger and payment provider

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): P3 (the ledger lives in `interviewdb`, no second database), P5 (provider secrets only from the environment), P8 (the billing module degrades and is visible through `AddIntegration`), P11 (the provider vocabulary stops at the boundary), `PAYMENTS-AND-MONETIZATION` §2 (gateway, not merchant of record, record decided), §3 (mock-first: fake provider on the real path), §4 (raw-body signature, constant-time compare, idempotency by event id, server-side amount), §10–§11 (failure modes, checklist), `SERVICE-API-PATTERNS` §1 (rate limit on an anonymous surface), §5 (no auto-redirect on the provider client), §10 (checklist), `SECURITY-REVIEW` §5 (CSPRNG for secrets), §7 (errors do not echo provider text), §8 (deny by default; one anonymous route, named), `TESTING-STRATEGY` §4–§5 (InMemory first, PostgreSQL where InMemory cannot enforce, the fake as an in-service seam)

## Context

`docs/architecture/web-app-plan.md` §4 decides the payment shape: Stripe Checkout, one purchase of N interview credits, a webhook that adds credits idempotently by event id, and a refund when a session fails for a service reason. §10 fixes the endpoints (`GET /credits`, `POST /checkout`, `POST /webhooks/payments`) and the 402 on a start without a credit. ADR-0076 left two seams for this: `ICreditGate` (consume when a session starts) and `ICreditRefund` (called once when a session ends as `failed`), and it required `Interviews:RequireCredit` to refuse every start until a ledger existed.

Facts checked for this decision:

- The payments guide (`PAYMENTS-AND-MONETIZATION.md`, read in the standards repository) says the webhook is the only place money becomes entitlement, that it must be signature-checked over raw bytes, idempotent by provider event id, and that the amount must be checked server side. It also says a missing webhook secret must reject, not accept.
- The Npgsql provider in ServiceDefaults enables a retrying execution strategy. A user-started transaction outside that strategy throws at run time; the PostgreSQL race test found this (the InMemory run did not, because InMemory has no transactions).
- The interview-service has no NuGet package for Stripe and the brief asks for none (`Directory.Packages.props` is unchanged). The Stripe surface this needs is one form POST and one signed JSON event.
- **Not checked:** the Stripe wire format (the header name `Stripe-Signature`, the event and session field names, the `checkout.session.completed` shape) has been implemented from the provider's documented scheme and is covered by tests written against that scheme. It has **not** been compared with live or sandbox traffic: the environment has no provider credentials and no network to the provider. PAYMENTS-AND-MONETIZATION §2 requires that comparison before the first real payment.

## Decision

1. **The ledger is append-only** (`CreditEntries`): rows of `+N` (purchase), `-1` (consume) and `+1` (refund), each with a reason, a reference (the provider event id or the session id) and a week bucket. The balance is the sum of the account's rows. Nothing is updated in place.
2. **Three uniqueness facts are database facts**: `PaymentEvents.ProviderEventId` is UNIQUE (a redelivered event finds its row); `CreditEntries (Reason, Reference)` is UNIQUE (one purchase per event, one consume and one refund per session). The code also checks before writing, because the InMemory provider does not enforce unique indexes.
3. **The ledger is the only writer of credits** (`CreditLedger`). Each write runs under a process-wide lock. On PostgreSQL each write also runs in a SERIALIZABLE transaction inside the Npgsql execution strategy, retried on SQLSTATE 40001, with a fresh tracker per attempt. A mutation check showed the two layers are independent: with the lock removed, the InMemory races fail, and the PostgreSQL races still pass because the database holds the invariant.
4. **Provider seam**: `IPaymentProvider` (`CreateCheckoutAsync`, `VerifyAndParseWebhook`) returns a normalized `PaymentEvent`. `StripePaymentProvider` uses a typed `HttpClient` with redirects refused. `FakePaymentProvider` uses the same signature code and the same classifier, so the fake tests the production path. Its secret comes from `Billing:FakeWebhookSecret` or is random per process.
5. **Verification**: `Stripe-Signature` is `t=<unix>,v1=<hex>` (several `v1` allowed); HMAC-SHA256 over `t`, a dot and the raw body; constant-time comparison; a five-minute tolerance in both directions. No secret configured means every webhook is refused.
6. **Classification** (`WebhookEventParser`, shared by both providers): only a paid `checkout.session.completed` whose quantity (1..10, from the signed metadata), currency and amount (`PriceMinorUnits × quantity`) match the service's own price becomes a purchase. Every other event is acknowledged with 200 and a reason that names the rule, and nothing is stored from it.
7. **Price and secrets**: no price has a default in code; the checkout answers 503 `billing_disabled` until price, currency and both return addresses are set. Secrets are environment variables only, with placeholders in `secrets.env.example`. A provider is registered only when its settings are complete; an incomplete Stripe setup answers 503 rather than a half-working checkout. The fake provider is refused in Production.
8. **Order of the checks on a start** (after the merge with W4, ADR-0078): the emergency switch, then the verified-email gate, then the per-account and per-address rate limit, then the daily cap; the session takes its credit last, only after all of those have passed and the session is registered. A start refused by the rate limit or the cap writes nothing to the ledger. A session that fails to start returns its credit through the refund path. Production requires the `email_verified` claim (`Interviews:RequireVerifiedEmail`, true in the base configuration): authservice must emit it, which is an operator item.
9. **Credit gate**: `Interviews:RequireCredit` selects the ledger gate and refund; false (development, tests) keeps the free default. `appsettings.Production.json` sets it to true. A ledger failure during a start is a refusal, not a free start (fail closed).
10. **Refund rule**: a session that ends as `failed` returns its credit once. A withdrawn session (DELETE) and a stopped one (consent refused or withdrawn) keep it, as the plan says.
11. **Webhook route**: `POST /api/v1/webhooks/payments` is the one anonymous route added. It reads the raw body (64 KiB limit), verifies it, and answers 400 `bad_signature` (or 400 `invalid_request` for a signed body that is not an event). It has its own rate-limit policy (600 per minute, one window), not the per-client policies, because the provider calls from behind the edge proxy and a per-socket key would put every caller in one bucket.
12. **Privacy of the ledger**: the account is held in `CreditEntries` and `PaymentEvents` (the schema test now names these two tables as the only account holders besides the ticket row). No card data, no contact data, no body and no interview text is stored or logged. A test plants a card number and an email in a signed event and checks both the database and the log capture.

## Contract additions (for the owner to accept)

Section 10 of `web-app-plan.md` is the fixed contract and is **not edited by this decision**. The implementation answers these codes, which the table does not list. They are proposed here for acceptance:

- `POST /checkout`: `400 invalid_request` (quantity outside 1..10), `503 provider_unavailable` (the provider refused to create the session; its text is not returned).
- `POST /webhooks/payments`: `400 invalid_request` (oversized or non-event body), `503 billing_disabled`.
- The success body of the webhook is `200 { "received": true }`; the plan does not specify it.

The `billing_disabled` and `bad_signature` codes are already in section 10.

## Consequences

- **Easier:** the payment path is testable with no provider: the fake provider and the same signed-event tests run in-process, and the PostgreSQL run covers the races the database must hold.
- **Harder:** the Stripe format is unverified against the provider (see Context). Its first real check is a sandbox run before any money moves.
- **Not built, and recorded as open:**
  - **Credits of sessions open at a restart are not returned.** Plan §10 wants this (a restart answers 410 and the credit comes back). The ledger records the consume with the session id, but nothing marks a session as settled when it completes or stops, so after a restart the service cannot tell a finished session from an open one. The fix is a settle row per session at its end and a startup sweep that refunds consumes without one. Until then a restart during an interview loses that credit. This is the most important open gap of W3.
  - No simulate endpoint for development. A development purchase is a signed event from a test or from a process that knows the fake secret.
  - No payment status or refund API is exposed; a payer cannot see their ledger except through the balance.
  - The per-process lock covers one instance. Several instances rely on PostgreSQL's SERIALIZABLE behaviour, which is tested on one server only.
- **Privacy note:** the ledger links an account to a session id and a week. It does not link an account to a record or to text, because session ids are random and the record has none of the account. The plan accepted this link (web-app-plan §4); it is named here so a reviewer can see it.
- **Types location:** the request and response records live in `Billing/Endpoints/BillingEndpoints.cs`, not in `ExitInterviewAgent.Contracts` as the plan's §10 says the types should. Moving them is a follow-up that needs the contracts project and the BFF mirror.
- **Trigger for revisiting:** the first sandbox run (format check), a second instance, or the restart gap being closed.
