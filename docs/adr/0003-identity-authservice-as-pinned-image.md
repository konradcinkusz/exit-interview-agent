# 0003. Identity: authservice as a separate instance, consumed as a pinned image

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: P5 (exactly one service holds a signing key; others validate against its
  JWKS), `SHARED-SERVICE-REUSE.md` §1-§5, `IDENTITY-AND-ACCOUNTS.md` §10.

## Context

The system has users, so identity is `konradcinkusz/authservice`. The guide requires an independent
instance (own compute, database and signing key), consumed as a version-pinned image, never as source and
never as `:latest`.

Facts checked on 2026-10-05:

- Tags listed through the GHCR tag-list endpoint anonymously: `v0.1.0`, `v0.1.1`, `v0.1.2`, `v0.2.0`,
  `v0.3.0` … `v0.3.4`, `latest`. The newest versioned tag is `v0.3.4`. (Reproduce: request an anonymous
  pull token for `repository:konradcinkusz/authservice:pull`, then `GET /v2/konradcinkusz/authservice/tags/list`.)
- The tag **exists but its image layers could not be pulled from the build sandbox**: the sandbox proxy
  answered 403 for `pkg-containers.githubusercontent.com`. So the AppHost's `authservice` resource was
  not started in the authoring environment. GitHub-hosted runners are not behind that proxy.

## Decision

- The AppHost declares `ghcr.io/konradcinkusz/authservice:v0.3.4` with its own database `authdb`, schema
  mode `Migrate`, and `Jwt__Issuer`/`Jwt__Audience` set to the product-specific `ExitInterviewAgent`.
- The dev signing key is **dev-only**: it comes from `dotnet user-secrets` (written by `scripts/setup.*`)
  or, absent that, is generated ephemerally by the AppHost for the run. It is delivered to the authservice
  container only; `interview-service` and `web` get the authority URL and validate against the JWKS.
- authservice listens on the fixed host port 5100 locally: it is the externally contracted JWKS/issuer
  address, so it does not float (P1).
- `Identity:Enabled=false` (AppHost configuration) skips the resource. The rest of the stack runs, protected
  endpoints answer 401 and `/health` reports `identity: not configured` (P8). This is the documented path
  where the image cannot be pulled.
- Upgrading the pin is a deliberate edit of the tag in `src/ExitInterviewAgent.AppHost/Program.cs` and
  `flyio/authservice.fly.toml` together, with a note here.

## Consequences

Two JWT schemes for the MCP path (brief §4) and the OAuth client registration are T2/T8 work and change
this wiring only by adding configuration. Local verification of the authservice resource needs a machine
that can pull the image; CI runners can. Nothing from authservice's source is in this repository.
