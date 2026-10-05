# Infrastructure analysis (generated, not deployed)

The topology below is described by `flyio/*.fly.toml` and `.github/workflows/flyio*.yml`. **None of
it is deployed or triggered** (PROJECT-BRIEF §2). It answers the four questions of the Fly guide §13.
No price is stated: Fly pricing was not verified in this phase, and a number without a reproducing
command does not belong in this repository.

## 1. What runs when nothing is happening?

Read from the `fly.toml` files (`min_machines_running`, `[[vm]]`):

| App | Machines idle | Size | Why |
|---|---|---|---|
| `exit-interview-agent-postgres` | 1 (never stops; has a volume) | shared-cpu-1x, 1 GB, 3 GB volume | state |
| `exit-interview-agent-authservice-dev` | 1 | shared-cpu-1x, 512 MB | pinned (see 2) |
| `exit-interview-agent-interview-service-dev` | 1 | shared-cpu-1x, 512 MB | pinned (see 2) |
| `exit-interview-agent-web-dev` | 0 | shared-cpu-1x, 512 MB | scale to zero |

## 2. Which services pin a machine, and which synchronous call forces it?

- **authservice** pins one machine because `interview-service` fetches its JWKS in-request, on first use
  and again whenever its cache expires, and because `web`'s `POST /api/auth/login` calls
  `/api/v1/auth/login` in-request.
- **interview-service** pins one machine because `web`'s BFF proxy (`/api/proxy/v1/...`) calls it
  in-request with a 35 s timeout sized for a cold start; pinning is the preferred option over relying on
  that timeout.
- **web** scales to zero: it is entered only from a browser, so a cold start is a slow first page.
- **postgres** never stops: it carries a volume and is reached over `.internal`, which does not start
  stopped machines.

## 3. What is the cheaper option, and what does it actually cost?

Letting `interview-service` scale to zero saves one idle machine at the price of a failed (not slow)
first call from `web` after idle if the cold start exceeds the proxy's rung timeout, with a .NET start
plus a schema check inside it. Letting `authservice` scale to zero is worse: a JWKS cache expiry in
`interview-service` would turn into a rejected request. Neither is recommended.

## 4. What is off the table?

- Turning off `force_https`.
- Sharing one database between services, or sharing authservice's database or signing key with any other
  system (shared-service-reuse §1: independent instances).
- A public listener on Postgres.
- Pointing service-to-service calls at `.internal` for apps that scale to zero.
- A second Postgres server "for isolation" before there is a reason: one instance, one database and role
  per service is the recorded decision (ADR-004).
