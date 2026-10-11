# 0080. Interview service runs as a single instance (in-memory sessions)

- Status: accepted
- Date: 2026-10-11
- Principle or guide served (or deviated from): P7 (Fly topology: `flyio/*.fly.toml` is the reviewable topology), P3 and P4 (database per service, migrate never ensure), P6 (container per service, pinned build), P8 (degraded and visible), `FLY-IO-DEPLOYMENT` §4 (`min_machines_running`, `context`), §6 (networking), §7 (scale to zero: name the synchronous call), §8 (volumes), §10 (ordered deploy), §13 (the cost reasoning); `SECURITY-REVIEW` §4 (cookies set server-side, `secure`), §9 (the blockers). Builds on ADR-0076 (sessions in memory), ADR-0077 (credit refund on a failed session) and ADR-0078 (cost controls).

## Context

The web interview runs the agent inside `interview-service` (ADR-0076). The transcript, the result, the idle timers and the one-open-session-per-account rule are held in that process. Nothing about an open interview is in `interviewdb`, and a second process cannot see the first one's sessions.

The Fly topology written before this decision was not consistent with that fact:

- `interview-service.fly.toml` had `auto_stop_machines = "stop"`. The proxy may stop the only machine when it is idle; a stopped machine loses every open session without any error for the person.
- It had no deploy strategy. Fly's default rolling strategy starts the new machine next to the old one before it stops the old one. For that window, a request for a session can reach the machine that does not hold it, and the person sees an unexplained 404.
- It had 512 MB. Nothing in the repository measured that, and the .NET runtime, EF Core, the Anthropic SDK and in-memory transcripts were never sized together.
- The Dockerfile, the workflow and the TOML files did not state the single-instance rule in one place. Only ADR-0076 said it, in prose.

The workflow never scales the service above one machine. `flyio-scale.yml` normalizes it to 1. Nothing in the repository, however, makes a second machine impossible to add by hand.

## Decision

1. **One machine, always.** `[http_service] min_machines_running = 1` and no autoscaling. `flyio-scale.yml` keeps `scale count 1`. Fly's TOML does not express a maximum for this app, so the maximum is an operator rule written in the file's header comment. `scripts/check-fly.py` checks the minimum and the stop policy, and `scripts/check-fly-selftest.py` shows that the check fails on a broken copy.
2. **Never stop the machine.** `auto_stop_machines = false`. The proxy still starts a machine when it is missing (`auto_start_machines = true`).
3. **Deploy strategy `immediate`.** The old machine is stopped before the new one starts, so no request is routed to two machines that hold different sessions. This ends every open interview at each deploy. That is accepted: a session is short, a failed session returns its credit (ADR-0077), and a rolling deploy would only trade a clean end for an unexplained 404 during the overlap. Lifting this needs shared session state, which is a future ADR.
4. **Migrations run in a hosted service after the listener starts, not in a release command.** `MigrationExtensions` applies `MigrateAsync` after Kestrel is answering (P4). A `[deploy] release_command` was rejected: it would run on a separate machine before the new one exists, and it would not be the mechanism the rest of the estate uses. With `immediate` there is never a second process applying migrations at the same time.
5. **Memory 1024 MB.** A guess that replaces an unmeasured 512 MB. It is marked NOT RUN until a Fly machine exists to measure it.
6. **Service-to-service addressing.** The browser and the BFF call `interview-service` and `authservice` at their public URLs (`FLY-IO-DEPLOYMENT` §6). The public URL is also the JWT issuer, which every validator must fetch from. The database is reached only at `exit-interview-agent-postgres.internal:5432`. `web` scales to zero and is entered from a browser only, so `.internal` would not auto-start it.
7. **Build sources are explicit.** A `[build]` section declares either a pinned `image` (`authservice`, `postgres`) or `dockerfile` and `context` (`interview-service`, `web`). The brief's wording "every `fly.toml` declares `dockerfile` and `context`" cannot hold for a pinned image that this repository does not build. The checker enforces the either-or rule and forbids `:latest`.
8. **Session cookies are `Secure`.** `SESSION_COOKIE_SECURE = "true"` is written into `web.fly.toml`. The runtime default is also true, so the value is visible rather than implied.
9. **No pgAdmin on Fly.** A repository search finds no pgAdmin reference at all, neither in the AppHost nor in `flyio/`. The rule is enforced by `scripts/check-fly.py` over `flyio/`, so a future addition fails the build.

## Consequences

- **Easier:** the interview service's constraints are in one place: the header of its TOML, this ADR, and an automated check that breaks if the minimum, the stop policy or the strategy drifts.
- **Harder:** the service cannot be scaled out. Load beyond one `shared-cpu-1x` machine means a shared-state change (sessions in a store, sticky routing that the proxy does not give us for this) before any scaling. Sessions are lost on every deploy.
- **Residual risk:** the single machine is a single point of failure for every open interview. A machine crash ends them, and the credit refund path (ADR-0077) must cover that. Crash recovery of open sessions is not tested against Fly.
- **Not verified in this phase (NOT RUN):** whether a Fly machine with `auto_stop_machines = false` and `strategy = "immediate"` behaves as described; the memory figure; the authservice health path `/health/ready` and the pinned tag `v0.3.4` (the image is not pulled or inspected here); whether the GHCR images are pullable by Fly (an open question in `flyio/SECRETS.md`).
- **Deviation register:** the brief's invariant "every `fly.toml` declares `dockerfile` and `context`" is amended by point 7. The register row belongs in `docs/architecture/00-ARCHITECTURE.md`; it is not written in this change, because that file is outside this task's scope.

## Verification

- `python3 scripts/check-fly.py` (the invariants on the real files) and `python3 scripts/check-fly-selftest.py` (each rule shown to fail on a mutated copy, and the clean copy shown to pass). Both run in the CI job "workflow syntax, doc links, fly.toml".
- `python3 scripts/check-doc-links.py`: the links in this ADR and the runbook resolve.
- NOT RUN: `actionlint` on `.github/workflows/ci.yml` (no Docker daemon in this environment); any `fly` command; any build of the images in this change.

## Related

- [ADR-0076](0076-web-interview-sessions-in-memory-service-key.md): sessions in memory. This ADR states its consequence for deployment.
- [ADR-0077](0077-credits-ledger-and-payment-provider.md): a failed session returns its credit.
- [ADR-0078](0078-cost-controls-for-the-hosted-interview.md): the start cap and the emergency switch, which apply to the one instance.
- [Runbook](../runbooks/web-app-deploy.md): the table of apps, the first deployment order, and the commands as examples.
