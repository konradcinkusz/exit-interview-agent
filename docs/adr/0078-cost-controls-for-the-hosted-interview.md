# 0078. Cost controls for the hosted interview

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): P8 (optional dependencies degrade and are visible), P5 (configuration via environment), P15 (observability: counts, no content), `SERVICE-API-PATTERNS` §1 (rate limiting: partition by account, fall back to address; uniform 429; no queue; one resolver for the client), `SECURITY-REVIEW` §8 (deny by default; roles and claims, never e-mail comparisons; ownership at the resource), `METRIC-ETHICS` §1, §5 (anti-goals enforced by architecture; the unit is the artifact, never the person), `METRICS-EXPOSITION` §1, §3 (every label is a cardinality decision; no label carries data; a cap is visible). Amends ADR-0014 (the email claim is minimised; a boolean flag is kept).

## Context

The web app (`docs/architecture/web-app-plan.md`, sections 2 and 7, task W4) runs the model with the owner's key. Every start and every reply costs money, so the owner needs four brakes that work without a person watching: a per-account and per-address rate limit, an emergency switch, a gate that refuses unverified accounts, and a global daily cap. The owner also needs spend counts that say how many interviews started, ended and failed, without knowing who they were.

Facts checked for this decision:

- The kernel's rate limiting (`ApiExtensions.AddStandardRateLimiting`) has one policy per endpoint, and an endpoint holds one partition. A limit by account and a limit by address therefore need two partitions, which one policy cannot hold. The framework's `PartitionedRateLimiter` lets the endpoint filter ask both. No package is added.
- `Interviews:Enabled` (the emergency switch) already refuses new sessions with 503 `interviews_disabled` (ADR-0076). It is read on every start.
- Client addresses are resolved once, in `ClientKey` (ADR-0029, ADR-0030). The forwarded header is read only when configuration names one.
- **authservice's access token carries `email`, not `email_verified`** (`authservice/src/AuthService/Services/TokenService.cs`, `BuildClaimsAsync`, checked 2026-10-10). The flag exists only for external (Google) sign-ins (`ProviderEmailVerifier.cs`). For password accounts, authservice refuses a sign-in while the address is unconfirmed (`AuthController.cs`, `PasswordSignInStatus.EmailNotConfirmed`, 403 `emailVerificationRequired`), and the BFF maps that to `email_not_verified` at login. So a token that reaches this service was issued to an account that had confirmed its address when it signed in. The service cannot see that fact in the token.
- The service's claim minimisation (ADR-0014, `McpAuthenticationExtensions.RetainedClaims`) drops every claim except the protocol ones, so an `email_verified` flag would be removed before any handler sees it.
- The W2 interview sessions already count model calls and tokens per interview (`ModelMeter`), so the usage histograms need no new counting.

## Decision

1. **Per-account and per-address limits** on `POST /interviews` (starts) and `POST /interviews/{id}/reply` (replies), in `Interviews/CostControls/InterviewRateLimits.cs`, configured under `Interviews:RateLimits:*`. Starts: fixed window of one hour; replies: fixed window of one minute. Both limits are checked, the account first. A refusal is 429 with the kernel's body `{ error: "rate_limited", retryAfter }` and a `Retry-After` header. There is no queue. A fixed window does not return a permit, so an attempt counts even when the other limit refuses it.
2. **The emergency switch** (`Interviews:Enabled`, ADR-0076) is checked again by the start filter, so the refusal is the same whichever layer answers first. It stops new sessions only; a running interview can still finish.
3. **Verified email gate.** `POST /interviews` answers 403 `email_not_verified` unless the token has the claim `email_verified` equal to `true`. The flag is `Interviews:RequireVerifiedEmail`: **true in `appsettings.json` (production), false in `appsettings.Development.json`**. A missing claim is a refusal (fail closed).
4. **Daily cap.** `Interviews:MaxStartsPerDay` (default 20) counts admitted starts per UTC day in memory (`DailyStartCap`, with `TimeProvider`). At the cap, starts answer 503 `interviews_disabled` with `Retry-After` up to midnight UTC. A start reserves a slot before it runs and gives it back unless it answers 201, so an invalid or refused request never uses the cap. Zero refuses every start. The count is lost on restart; it is a brake, not a ledger. A monthly cap is not built: the spend limit in the provider console is the monthly control (see the runbook).
5. **Metrics** (`CostMetrics`, meter `ExitInterviewAgent.InterviewService.Interviews`, exported through the kernel's OpenTelemetry meter provider): counters `interviews_started`, `interviews_completed`, `interviews_failed`, `interviews_withdrawn`, `rate_limited`, `rejected_email_unverified`; histograms `tokens_estimated` and `model_calls`, recorded once per interview when its run ends (including tiles and a cancelled run). **No instrument has a tag.** There is no per-account, per-session or per-address series. The test suite asserts that no measurement carries a tag.
   - `interviews_withdrawn` counts an interview ended by the person: a deletion of an open session, a consent refusal, or a stop.
   - `interviews_failed` counts a session that ended as a service fault (its credit comes back).
6. **Health and banner.** The integration `interview-cost-controls` is configured when the emergency switch is on and a provider is set. Its detail (in `/health` and the startup banner) says whether the switch is on, whether a provider is configured, whether the email gate is on, and the daily cap. It never carries a key or the name of the variable that holds one.
7. **Wiring.** The checks are attached to the two routes in `Interviews/Endpoints/InterviewEndpoints.cs` (`WithInterviewStartControls`, `WithInterviewReplyControls`). The services are registered by `AddInterviewCostControls` (`Infrastructure/ServiceCollectionExtensions.cs`), called from `Program.cs`.

### Amendment to ADR-0014 (claim minimisation)

`RetainedClaims` now also keeps **`email_verified`**. It is a boolean; it is not the address and not a name. The address and the name are still dropped. `ClaimMinimizationTests` still asserts the list for a principal without the flag, and a test in this ADR's suite asserts that the flag survives and the address does not.

### Open dependency: authservice must send `email_verified`

With the gate on (production), **every start is refused until authservice puts `email_verified` in the access token** (its `TokenService.BuildClaimsAsync`). That change is outside this repository and is the owner's to make in authservice. It is not made here, and nothing here pretends the claim exists. Until it does, the production configuration refuses all starts, which is the intended fail-closed behaviour and is visible on `/health`.

### Contract (web-app-plan §10)

Proposed change, to be recorded in the plan by its owner (the plan is fixed, and this ADR does not edit it): a start may answer `403 email_not_verified` (new code); `429 rate_limited` carries `Retry-After` and the body `{ error, retryAfter }` (the kernel shape, not an RFC 9457 problem); `503 interviews_disabled` also covers the daily cap.

## Consequences

- **Easier:** the owner has four brakes that act without a person (the switch, the per-account and per-address limits, the daily cap), and spend counts that need no identity. The checks are endpoint filters in one file, tested with a fake clock for the cap and the gate and with the framework limiter for the windows.
- **Harder:** the production email gate depends on an authservice change that is not made yet (see above). Limits are per process: a second replica has its own counts (the same limit as ADR-0076 and threat model T-18). The daily cap resets on restart.
- **Memory per key:** each account and each address that starts or replies gets a limiter partition. This code does not bound their number; a flood from many distinct addresses grows the table until the process restarts. The kernel's limiters have the same property. Trigger for review: a memory graph that climbs with traffic, or a shared limiter store (a second replica).
- **Known limit of the tests:** the framework's fixed-window limiter reads system time, not `TimeProvider`, so the tests prove the refusal and the `Retry-After` value but do not run a window to its reset. The daily cap and the gate are tested on a fake clock.
- **Deviation from ADR-0014:** a boolean flag is now retained. The email address and name remain dropped; the amendment is recorded above, and the register in `00-ARCHITECTURE.md` is unchanged because this is an ADR amendment, not a principle deviation.
- **Deferred:** a monthly cap (the provider console's monthly spend limit is the control for now); a shared counter for several replicas; a per-account spend ledger (rejected: it is a per-person record of usage, which METRIC-ETHICS §5 and the privacy brief do not allow for this purpose).
- **Trigger for revisiting:** authservice starts sending `email_verified` (then nothing here changes); the first real-model run (W9), whose cost per interview sets `MaxStartsPerDay` and the window sizes; a second replica.
