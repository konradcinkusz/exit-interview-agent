# 0081. Full-stack test of the AppHost: Aspire test host, Docker in CI, skipped without it

- Status: accepted
- Date: 2026-10-11
- Principle or guide served (or deviated from): P13 (test at the layer that has the logic: the integration of the composed stack is its own layer), P1 (the AppHost is the composition root; its change here is testability only), P8 (degraded dependencies stay visible: the test asserts the model row and the health answer, not a fake green), `TESTING-STRATEGY` §2 and §4 (tier 2, compose per run), `E2E-ACCEPTANCE-TESTING` §2 (no pass without a checked assertion; a scaffold is a skip with a reason), §6 (CI wiring is part of done)

## Context

Nothing in CI, until now, started the composition the product runs on. The unit, service and browser suites each test one layer, against a stub or an in-memory store. The stack that a developer runs with `dotnet run --project src/ExitInterviewAgent.AppHost` (PostgreSQL, pgAdmin, authservice, interview-service, the web app) had never been exercised as a whole by an automated check.

Facts checked (2026-10-11):

- **Docker is not available in the authoring container** (no daemon, no socket). The full stack therefore cannot run here. It runs on the GitHub-hosted runner, which has Docker. Everything that does not need a container was verified locally: the solution builds with `-warnaserror`, `dotnet format --verify-no-changes` passes, the existing test projects run, the AppHost publishes its manifest (`--publisher manifest`), and the test host builds the resource model without starting it.
- **Test host:** `Aspire.Hosting.Testing` 13.6.0 matches the AppHost's `Aspire.Hosting.*` 13.6.0. The test host starts the AppHost in run mode (the same resources as a developer's run). The web app's folder is resolved from the AppHost's own directory (`builder.AppHostDirectory`), not from the working directory.
- **authservice, tag `v0.3.4` (checked from its source at that tag, not from `main`):** `POST /api/v1/auth/register` takes `email`, `password`, `acceptedTermsVersion`, `acceptedPrivacyVersion` and an optional `locale`. The published versions come from `GET /api/v1/auth/consents/versions`. A new account is confirmed at sign-up when the deployment cannot send email (`EmailConfirmed = !RequireConfirmedEmail`, and `RequireConfirmedEmail` defaults to whether an email provider is configured). The AppHost configures no email provider, so registration needs **no verification step**, and the test does not write to authdb. `POST /api/v1/auth/login` returns `accessToken`, `refreshToken`, `expiresIn`, or a two-factor challenge.
- **The BFF** (web app) sets HttpOnly cookies `eia_access`, `eia_refresh` and `eia_consent` on sign-in. It refuses a state change whose `Origin` is not its own host (ADR-0048). Every proxied `v1/*` path is forwarded (`routeFor`), and `isInterviewPath` accepts the interview, credit and checkout shapes.
- **interview-service:** `/health` answers 200 while the interview model runs the scripted mock (the model row says degraded; the HTTP status is not a degradation, ADR-0079). Migrations run after the server starts, in a hosted service (`ServiceDefaults`' `MigrationExtensions`), so the test waits for `__EFMigrationsHistory` rather than assuming the tables exist. Interview text is held in memory (ADR-0076) and is written to no table.
- **The scripted interviewer** takes consent from a short yes (`tak`, `zgadzam się`). A stop phrase, including "nie zgadzam się", ends the interview, so the test answers never use one.
- **Web in CI:** `next dev` compiles on the first request, which is slow and is not what a CI run should measure. `next start` serves the artifact that ships.

## Decision

**Project.** `tests/ExitInterviewAgent.AppHost.Tests` (xUnit, `Aspire.Hosting.Testing`, a `ProjectReference` to the AppHost). One fixture starts the AppHost once, lazily, for the whole class. Every scenario owns its account and its interview, so the order does not matter. The start budget is 12 minutes, and each health wait is bounded by the same budget.

**Scenarios.** (a) every resource is up: postgres, authservice, interview-service and web reach healthy, pgAdmin reaches Running, and each probe answers (`/health/ready`, `/health` with the model row, `/healthz`). (b) an account registers at authservice, signs in through the BFF, and the cookies make a page call work; a wrong password sets no session. (c) a Polish interview runs end to end through the BFF: start, consent, answers including a short one, the result with the record and the Glassdoor, Google and Reddit tiles, delete, then 404. (d) with `interviews-require-credit=true`, a start without credit is refused with 402; the development grant adds one credit; the start takes it (balance 0); a completed interview keeps it spent (balance 0). (e) interviewdb has the migration history and the credit ledger, and the interview's marker text appears in no table.

**Settings fixed by the test, not by a developer's secrets:** the mock provider and model, the credit gate on, identity on, and the `Development` environment (the development grant is mounted only there, ADR-0079).

**Skip, never pass.** `FullStackFactAttribute` skips with the reason `NOT RUN: ...` unless `FULL_STACK_TESTS=1` is set and a Docker socket (or `DOCKER_HOST`) exists. The gate is a pure function and is unit-tested without Docker (`SupportTests`).

**Testability in the AppHost (minimal, no change in development).** The web script is `Web:Script` (default `dev`), and the web folder is resolved from `builder.AppHostDirectory`. Nothing else changes: the fixed ports (3000, 5050, 5100) stay fixed, as ADR-0079 requires.

**CI.** A new job `full stack (Aspire)` in `ci.yml`, separate from the `dotnet` job. It builds the web app once, sets `Web__Script=start` and `FULL_STACK_TESTS=1`, and runs the test project. On failure it writes each container's log and uploads the resource logs (seven days). The `dotnet` job runs the same solution, so it reports these tests as NOT RUN, which is the honest count there. No existing job is changed.

**Not tested here, on purpose.** Live model quality (the scripted mock is not a quality baseline, AI-EVALS applies to real models only); the Stripe path (the fake provider only); two-factor and email verification flows (covered by the browser suite and authservice's own tests).

**Database check.** The "no interview text" check scans every base table of `public` for the marker, with `position(marker in row::text)`. It is a negative assertion over the actual tables of the migrated database. The test does not show that the scan can see the marker in a row, because it writes nothing to the database. The check is therefore only as strong as the query, which is one `position()` over each row's text; a stronger check would need a deliberate write, which this test does not make.

## Consequences

- The composition is checked on every pull request, against the real pinned authservice image, PostgreSQL, pgAdmin and a production web build. A regression in a wire between services (a config name, a health path, a cookie name, a credit rule) now fails a job.
- The job is slower: an estimate of 5 to 10 minutes on a cold runner (three image pulls and a Next start); CI will show the real figure. It is a separate job, so the other jobs do not wait for it.
- The job depends on `ghcr.io` for `konradcinkusz/authservice:v0.3.4` and on Docker Hub for PostgreSQL and pgAdmin. A pull failure is an environment failure and is reported as such in the log, not as a product failure.
- Ports 3000, 5050 and 5100 are fixed (ADR-0079). A runner that has something on them fails the start with a clear message.
- Locally, a developer with Docker can run it with `FULL_STACK_TESTS=1 dotnet test tests/ExitInterviewAgent.AppHost.Tests`. Without the variable the tests are reported as NOT RUN.
- Trigger to revisit: a real payment provider or a real model in CI (then the scripted-mock assumptions change), a change of the authservice pin (this test follows it on purpose), or a second environment that needs the same composition (then the settings move out of the test).

## Resolved (CI, first runs of PR #55)

The job `full stack (Aspire)` is blocking and green on the PR head (14 of 14 journeys). The cause of the first four failures is below; the diagnostics that found it were removed once it was found.

**Cause.** The AppHost passed the web app and interview-service the literal address `http://localhost:5100`. Under the test host, the authservice container is published on a dynamic host port (40321 in the run that showed it), and nothing listens on 5100 there. Every identity call from the BFF therefore went to a dead port and hung until the fetch timeout (502 `identity_unreachable`, and 502 `identity_unavailable` on the consent-versions probe). The test host itself reached authservice, because it asks the AppHost for the endpoint (`App.GetEndpoint("authservice")`). The earlier attempt at `127.0.0.1` changed the host, not the port, so it could not help.

**Fix.** The AppHost passes the authservice endpoint reference (`authservice.GetEndpoint("http")`) to the web app and to interview-service. Each gets the address the stack actually uses: the dynamic port under the test host, and 5100 in a developer's `dotnet run`, as before. The fixed ports (3000, 5050, 5100) still hold for a developer, as ADR-0079 requires.

**Two further causes found after the first, both in the stack's own configuration or the test:**
- The AppHost always set `Interviews__ApiKeyEnv=ANTHROPIC_API_KEY`. The provider resolver requires a named key variable to be set, even for the offline mock, so every interview start answered 503 `interviews_disabled`. The name is now set only together with a key. A developer without a key was hit by the same fault.
- The scripted interview closes after its sixth answer with an interviewer turn of kind `close`. The session ends after it, so the next reply answers 409 `interview_ended`. The test waited for an `ending` object on a reply, which arrives only with the next event. The answer loop now stops on the closing turn.

**The last journey.** The scripted mock with short answers produces no Glassdoor or Reddit tile, and the product drops a tile it will not show, with a code in `dropped`. The journey asserted that the draft tiles always appear. It now asserts that `facts` is present, that every other tile kind is either shown or accounted for by a dropped code, and that the legal notice is present.

**Timeline (PR #55, UTC, 11 Oct 2026).** Attempt 1: stack starts, 10 of 14 pass (identity fails). Attempts 2 to 4: the `127.0.0.1` address and live logging, no change. Attempts 5 to 7: diagnostics in a second AppHost, which could not reproduce the test's state (DCP's watch timed out). Run 38140920190: the test's own failure message showed the dynamic endpoint and the refused literal port, which gave the cause. Then the API key name (503), the closing turn (409), and the tile assertion. Head `02d0d36` is green, 14 of 14.

**Lessons.**
- The test host gives containers dynamic host ports. Never pass a literal port from the AppHost to another resource; pass the endpoint reference.
- A failure message that names the endpoints the test host resolves, and probes them from inside the test host, was the fastest evidence. A second AppHost started by hand after the test host stops does not reproduce the state of the tests.
- A test should assert what the product promises. The tile set depends on the interview, and the product's answer is "shown or dropped with a code".
- A resource's own log was empty in CI. The failure report's probes were the reliable source, and the container log tails are now printed to the job log on failure.

**Kept from the diagnostics.** The failure message still names the endpoints and probes them (`StackFixture.ProbeAuthorityAsync`); it runs only on failure and costs nothing on success. The container-log step runs on failure and prints the tails to the job log.

**Still deferred (not part of this ADR's scope).**
- The AppHost's `next start` run prints a notice that the output is `standalone`. The production web image runs `node .next/standalone/server.js`, which the test does not exercise. A separate change.
- The runbook (`docs/runbooks/local-run.md`) describes `localhost:5100` for a developer's run. That is still true; the test uses the endpoint reference.
- Two-factor and email verification through the stack, a real model in CI, and the Stripe path: covered elsewhere, as the Decision section says.
