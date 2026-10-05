# 0004. Deployment topology decisions (generated, not deployed)

- Status: accepted (topology only; nothing is deployed)
- Date: 2026-10-05
- Principle or guide served: P7, P12, `FLY-IO-DEPLOYMENT.md` §4-§11, `INIT-GENERIC-TEMPLATE.md` §1 (the
  three decisions: region, registry, whether the system has users).

## Context

The template makes three things decisions rather than derivations. The brief forbids any deployment in
this phase, so these decisions shape generated files only.

## Decision

- **Region: `fra`.** The data concerns employment, and the project handles GDPR-relevant subjects
  (brief §1 topics, §6). A European region is the conservative default for that population. This is a
  judgement, not a verified legal claim (`docs/` legal considerations are T3's job); the owner's location was
  not available. A region is expensive to change once volumes exist, so confirm it before the first tag.
- **Registry: GHCR** (`ghcr.io/konradcinkusz/exit-interview-agent-<service>`), deployed with
  `flyctl deploy --image`. A private GHCR package needs a pull credential on Fly. The exact mechanism was
  not verified here and is recorded as an open question in `flyio/SECRETS.md` rather than guessed in a workflow.
- **Users: yes**: identity is authservice (ADR-003). The AppHost, the web BFF and `interview-service`
  consume it; nothing in this repository stores users or mints tokens (P5).
- **Environments:** one, `dev`, in app names (`<slug>-<service>-dev`). Adding `prod` is a copy of four
  files and one workflow variable.
- **Code scanning:** the CodeQL workflow runs and keeps SARIF as a run artifact. Uploading to GitHub code
  scanning needs GitHub Advanced Security on a private repository, which is not assumed; `CODEQL_UPLOAD` in
  `.github/workflows/codeql.yml` flips to `always` when the repository is public or GHAS is enabled. The
  dependency-audit job runs regardless. This is a recorded deviation from `REPO-BASELINE.md` §1 (SAST results
  visible in the repository), listed in the register.
- **Machines:** `interview-service` and `authservice` pin one machine; `web` scales to zero; `postgres` never
  stops. Reasons and the synchronous calls that force them are in `flyio/INFRASTRUCTURE-ANALYSIS.md`.

## Consequences

Before the first tag a human must: confirm the region, create the `dev` GitHub environment with
`FLY_API_TOKEN` and the root secrets, resolve the GHCR pull credential, and decide the CodeQL upload
setting. Until then no workflow under `flyio*.yml` runs: they trigger on `v*` tags or manual dispatch only.
