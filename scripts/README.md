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

## The MCP path (Claude connector) locally

What it needs, and why it is off by default (ADR-0012): authservice becomes an OAuth authorization server only when a client is
configured, and it **refuses http** for its issuer (`Jwt:PublicBaseUrl`) and for every resource. Claude's servers must also be able to
reach both. So the MCP path needs two public **https** URLs in front of the local ports, and nothing here can invent them:

| Setting (user-secrets or environment) | Meaning |
|---|---|
| `Mcp:AuthPublicBaseUrl` | https origin that reaches authservice (local port 5100). It becomes authservice's `Jwt:PublicBaseUrl`, the issuer of MCP tokens |
| `Mcp:ResourceUrl` | https URL of the MCP endpoint on interview-service (local port 5200), **with a path**, for example `https://<tunnel>/mcp` |

```bash
scripts/setup.sh                                   # also generates the two dev-only secrets below
dotnet user-secrets set "Mcp:AuthPublicBaseUrl" "https://<tunnel to :5100>" --project src/ExitInterviewAgent.AppHost
dotnet user-secrets set "Mcp:ResourceUrl"       "https://<tunnel to :5200>/mcp" --project src/ExitInterviewAgent.AppHost
dotnet run --project src/ExitInterviewAgent.AppHost
```

With both set the AppHost configures one authservice client (`claude-exit-interview-dev`, redirect
`https://claude.ai/api/mcp/auth_callback`, scopes `interview:submit offline_access`, `AllowedResources` = `Mcp:ResourceUrl`) and gives
interview-service `Mcp__Issuer` / `Mcp__Resource`. Then, in Claude: Settings → Connectors → Add custom connector with `Mcp:ResourceUrl`,
**Advanced settings**: client id `claude-exit-interview-dev` and the client secret (the Aspire dashboard shows the `authservice-mcp-client-secret`
parameter; `scripts/setup.*` stores it in user-secrets). The MCP transport is Implemented (T8); the full runbook, with every step marked verified or not, is [`../docs/guides/connect-claude.md`](../docs/guides/connect-claude.md).

| Dev-only secret (user-secrets key) | What | Made by |
|---|---|---|
| `Parameters:authservice-mcp-client-secret` | the client's secret, 32 random bytes as hex | `setup.*`, else generated per run |
| `Parameters:authservice-encryption-key` | authservice's token-encryption key, base64 of 32 random bytes | `setup.*`, else generated per run |

A per-run value works but changes at every start: Claude's stored client secret and every connection break. Use `setup.*`.

**Without those two URLs:** `Mcp__*` is unset, `GET <interview-service>/health` lists `mcp-auth` as not configured, the protected-resource
metadata answers 404 and every MCP token is refused. Web login, consent and refresh are unaffected.

### What cannot be exercised where

| Part | Cloud sandbox | Needs |
|---|---|---|
| `interview-service` schemes, scope policy, metadata, challenges | yes: `dotnet test` (per-run RSA key, no container) | nothing |
| BFF refresh, consent, deletion, headers | yes: `pnpm test`, Playwright against a stub identity | nothing |
| authservice image `v0.3.4` | no: image blobs answer 403 through the sandbox proxy | a machine that can pull from `ghcr.io` |
| Claude completing the OAuth flow and calling the endpoint | no | two public https URLs and a Claude account |

How the authservice side was checked without the image (2026-10-05, not part of CI): authservice built from source at commit `cccf978`
(`v0.3.4` is source-equal for the code involved, ADR-0012), run against a local Postgres behind a self-signed TLS proxy so its issuer was
https; a client with the Claude redirect URI; a scripted authorization-code + PKCE flow (sign in, consent, code exchange) produced a real
MCP token, which `interview-service` accepted from the real metadata and JWKS, while the same service refused that token on `/api/v1/*`
and a real web token on `/mcp`.

## Troubleshooting (keyed on the literal text you will see)

| You see | Cause | Fix |
|---|---|---|
| `Container runtime 'docker' could not be found.` | No container engine on `PATH` | Install Docker (or Podman: `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman`) and re-run |
| `Cannot connect to the Docker daemon` / `failed to connect to the docker API` | Engine installed, daemon not running | Start Docker, check `docker info` |
| `A compatible .NET SDK was not found` | SDK older than `global.json` | Install .NET SDK 10 |
| `IDX10214: Audience validation failed` on `/mcp` | The token's `aud` is not `Mcp:Resource` exactly (case, trailing slash, path) | Make `Mcp:ResourceUrl`, authservice's `AllowedResources` and the URL Claude was given the same string |
| `IDX10500: Signature validation failed. No security keys were provided` (in interview-service logs) | The service has no JWKS to validate against: `Jwt:Authority` empty or authservice not reachable | Run via the AppHost (it injects the authority) or set `Jwt__Authority` |
| authservice exits naming `Jwt:Algorithm` | The signing key did not reach authservice, so the symmetric path was selected | Run `scripts/setup.*`, or let the AppHost generate its ephemeral key; check `GET <authservice>/.well-known/jwks.json` returns a non-empty `keys` array |
| `pnpm: command not found` | pnpm missing | `npm install -g pnpm` or `corepack enable` |
| `scan-secrets: neither 'gitleaks' nor a running Docker daemon is available.` | Hook cannot run | Install gitleaks or start Docker |
| `initdb: error: directory "/var/lib/postgresql/data" exists but is not empty` (Postgres container exits, AppHost never gets healthy) | The named volume `exit-interview-agent-pgdata` was initialised by a different Postgres major (the AppHost pins 17, matching `flyio/postgres.fly.toml`) | `docker volume rm exit-interview-agent-pgdata` (dev data only) and re-run |
| `Pulling` stalls or `429 Too Many Requests` from Docker Hub | Anonymous pull rate limit | `docker login`, or retry later |
