# Secrets: what is secret, where it lives, how to set it

> **Nothing here has been done.** This phase builds and tests locally only: no Fly app exists, no
> `FLY_API_TOKEN` is configured anywhere, no secret has been added to GitHub (PROJECT-BRIEF §2).
> This file is what a *future* deployment decision would need, written down now so it is reviewed once.

Rule of thumb: if you would not paste it into a pull request, it is a secret. `[env]` blocks in
`flyio/*.fly.toml` are public (they appear in `fly config show` and image metadata).

## Per app

| App | Secret | What it is | Notes |
|---|---|---|---|
| `exit-interview-agent-postgres` | `POSTGRES_PASSWORD` | superuser password | generate hex, not base64 (`+ / = ;` break connection strings) |
| | `INTERVIEW_DB_PASSWORD` | password of role `interview` (owns `interviewdb`) | read by the first-boot init script |
| | `AUTH_DB_PASSWORD` | password of role `authservice` (owns `authdb`) | read by the first-boot init script |
| `exit-interview-agent-authservice-dev` | `ConnectionStrings__DefaultConnection` | `Host=exit-interview-agent-postgres.internal;Port=5432;Database=authdb;Username=authservice;Password=<AUTH_DB_PASSWORD>` | assembled by the pipeline from the password plus a known host |
| | `Jwt__PrivateKeyPem` | **the only signing key in the system**, RSA-2048+, PKCS#8 PEM | never reused from a laptop key; held by this app and nothing else |
| `exit-interview-agent-interview-service-dev` | `ConnectionStrings__interviewdb` | `Host=exit-interview-agent-postgres.internal;Port=5432;Database=interviewdb;Username=interview;Password=<INTERVIEW_DB_PASSWORD>` | no credentials for any other database |
| `exit-interview-agent-web-dev` | none today | the BFF holds no key: it verifies against authservice's JWKS | |

Later work adds secrets (ledger HMAC key, authservice MCP client secret and encryption key, model
API keys for hosted use). They are added to this table in the same pull request that introduces them.

## How, when it is ever done

Set from the pipeline, never by hand, so environments cannot drift:

```bash
fly secrets set -a <app> "Key__SubKey=value" --stage   # --stage: no restart until the next deploy
fly secrets list -a <app>                              # names and digests only
```

One-time human setup per repository, from `docs/guides/FLY-IO-DEPLOYMENT.md` §11 of the standards:

1. `fly tokens create org`, stored as `FLY_API_TOKEN` in a GitHub **environment** (here: `dev`), not a
   repository secret.
2. The root secrets above in that environment.
3. Nothing else: no `fly launch`, no manual app or volume creation. `.github/workflows/flyio.yml` creates
   missing apps and volumes idempotently.

## Local development

Local secrets are dev-only and generated, never invented: `scripts/setup.sh` / `setup.ps1` write a
throwaway RSA key to `dotnet user-secrets`; the AppHost generates an ephemeral one when none exists.
See `scripts/README.md` for the key's journey. A local key must never be used in a deployment.

## Open question for the deployment phase (not decided here)

The images are published to GHCR (ADR-004). If a package is private, Fly needs a pull credential for it.
The mechanism has not been verified in this phase and is deliberately not written into the workflow as
if it were known; resolve it before the first tag.
