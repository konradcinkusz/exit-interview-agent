# Scripts

| Script | What it does |
|---|---|
| `setup.sh` / `setup.ps1` | One-command onboarding: prerequisites, git hook, local secret store, generated dev key. `--check` / `-Check` reports what is missing and changes nothing. |
| `scan-secrets.sh` | gitleaks over the tree, or `--staged`. Local binary, else the pinned container image. Fails closed. |
| `hooks/pre-commit` | Runs `scan-secrets.sh --staged`. Installed by `setup.*` via `core.hooksPath`. |
| `check-kernel-size.sh` | P2's ceiling: fails if `ExitInterviewAgent.ServiceDefaults` exceeds 800 lines. CI runs it. |

Runbook scripts will use the numbered-alias convention (`0-…`, `1-…`) when there is a runbook
to number; at t=0 there is setup and the scanner.

## Variables by tier

The authoritative list, with what degrades without each, is [`../secrets.env.example`](../secrets.env.example).

| Tier | Meaning | Examples |
|---|---|---|
| always | needed to start anything | none: a fresh clone starts with zero credentials |
| mode | needed for one deployment mode | `Jwt__Authority` (identity), `ConnectionStrings__interviewdb` (PostgreSQL) |
| secret | never in source | `Parameters:authservice-jwt-private-key` (dev-only PEM) |
| ci | CI only | none in this phase: nothing is deployed |
| tuning | optional | `DATABASE_PROVIDER`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `Cors__AllowedOrigins__0` |

## The dev signing key's journey (one place, once)

`dotnet user-secrets` (key `Parameters:authservice-jwt-private-key`, set by `setup.*` or absent)
→ AppHost parameter `authservice-jwt-private-key` (`AddParameter(..., secret: true)`; if the
secret store has no value the AppHost generates an ephemeral key for that run) → environment
variable `Jwt__PrivateKeyPem` on the **authservice container only** → config key `Jwt:PrivateKeyPem`
inside authservice. `interview-service` and `web` never receive it: they validate against the
JWKS authservice publishes. The key is **dev-only**; it is distinct from any deployed key and
must never be reused for one (identity guide §10).

## Troubleshooting (keyed on the literal text you will see)

| You see | Cause | Fix |
|---|---|---|
| `Container runtime 'docker' could not be found.` | No container engine on `PATH` | Install Docker (or Podman: `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman`) and re-run |
| `Cannot connect to the Docker daemon` / `failed to connect to the docker API` | Engine installed, daemon not running | Start Docker, check `docker info` |
| `A compatible .NET SDK was not found` | SDK older than `global.json` | Install .NET SDK 10 |
| `IDX10500: Signature validation failed. No security keys were provided` (in interview-service logs) | The service has no JWKS to validate against: `Jwt:Authority` empty or authservice not reachable | Run via the AppHost (it injects the authority) or set `Jwt__Authority` |
| authservice exits naming `Jwt:Algorithm` | The signing key did not reach authservice, so the symmetric path was selected | Run `scripts/setup.*`, or let the AppHost generate its ephemeral key; check `GET <authservice>/.well-known/jwks.json` returns a non-empty `keys` array |
| `pnpm: command not found` | pnpm missing | `npm install -g pnpm` or `corepack enable` |
| `scan-secrets: neither 'gitleaks' nor a running Docker daemon is available.` | Hook cannot run | Install gitleaks or start Docker |
| `initdb: error: directory "/var/lib/postgresql/data" exists but is not empty` (Postgres container exits, AppHost never gets healthy) | The named volume `exit-interview-agent-pgdata` was initialised by a different Postgres major (the AppHost pins 17, matching `flyio/postgres.fly.toml`) | `docker volume rm exit-interview-agent-pgdata` (dev data only) and re-run |
| `Pulling` stalls or `429 Too Many Requests` from Docker Hub | Anonymous pull rate limit | `docker login`, or retry later |
