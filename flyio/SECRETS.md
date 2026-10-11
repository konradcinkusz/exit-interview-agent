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
| | `AuthorizationServer__Clients__0__ClientSecret` | MCP client secret for Claude, 32 random bytes as hex (`openssl rand -hex 32`) | entered once in Claude's connector "Advanced settings"; never the dev value |
| | `AuthorizationServer__EncryptionKey` | token-encryption key, base64 of 32 random bytes (`openssl rand -base64 32`) | durable: a key that changes invalidates every MCP connection (authservice ADR 0005 A4) |
| | `Jwt__PrivateKeyPem` | **the only signing key in the system**, RSA-2048+, PKCS#8 PEM | never reused from a laptop key; held by this app and nothing else |
| `exit-interview-agent-interview-service-dev` | `ConnectionStrings__interviewdb` | `Host=exit-interview-agent-postgres.internal;Port=5432;Database=interviewdb;Username=interview;Password=<INTERVIEW_DB_PASSWORD>` | no credentials for any other database |
| | `Ledger__ActiveKeyId`, `Ledger__Keys__0__Id`, `Ledger__Keys__0__Secret` | the submission ledger's HMAC key: an id of 1-32 characters and `openssl rand -base64 32` (at least 32 bytes) | **the service refuses to start without it outside Development.** Keep it outside the database's backup domain. Rotation: add `Keys__1`, switch `ActiveKeyId`, remove the old key after the ledger window ([ADR-0028](../docs/adr/0028-submission-ledger-hmac-rotation-and-window.md)) |
| | `ANTHROPIC_API_KEY` (or the variable named by `Interviews__ApiKeyEnv`) | the model provider key, from a dedicated workspace with a monthly spend limit (runbook §4) | read from the environment by the Providers resolver, never from `Interviews__*` configuration; without it a start answers 503 |
| | `ANTHROPIC_WORKSPACE_ID` | optional workspace id, sent as the `anthropic-workspace-id` header (`ProviderChatClients.cs`); needed only for a key that is not workspace-scoped | optional; the spend limit in the console is the control that matters |
| | `Interviews__Provider`, `Interviews__Model` | which provider and which model run the interview | set with `fly secrets set --stage`, not in `[env]`: the model is an owner decision after the W9 run, and the code has no default |
| | `Billing__StripeSecretKey`, `Billing__StripeWebhookSecret`, `Billing__StripePriceId` | payment provider keys and the one-credit price (test mode in staging, `sk_test_` only) | added after the Stripe test-mode checks (runbook §5) |
| | `Billing__PriceMinorUnits`, `Billing__Currency`, `Billing__SuccessUrl`, `Billing__CancelUrl` | price, currency and the owner's return pages | decisions of the owner; the code has no default and refuses checkout (503) until they are set |
| `exit-interview-agent-web-dev` | none today | the BFF holds no key: it verifies against authservice's JWKS | |

**Known gap before the first deployment (not closed in this change).** `Ledger__*` is required outside
Development, and the interview service refuses to start without it. `.github/workflows/flyio.yml` sets only
`ConnectionStrings__interviewdb` for this app, so until the ledger keys are set by hand (runbook §3) the first
deploy fails its health check. That is the fail-closed behaviour working, not a bug to hide. The authservice's
`AuthorizationServer__Clients__0__ClientSecret` and `AuthorizationServer__EncryptionKey` are in the same position.
The rows above for hosted-interview secrets are the ones the code reads today; each is set with `fly secrets set --stage`.

Later work adds secrets. They are added to this table in the same pull request that introduces them.

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
