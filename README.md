<a name="readme-top"></a>

# exit-interview-agent

[![ci](https://github.com/konradcinkusz/exit-interview-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/exit-interview-agent/actions/workflows/ci.yml "ci workflow runs")
[![secret-scan](https://github.com/konradcinkusz/exit-interview-agent/actions/workflows/secret-scan.yml/badge.svg)](https://github.com/konradcinkusz/exit-interview-agent/actions/workflows/secret-scan.yml "secret-scan workflow runs")
[![License: MIT](https://flat.badgen.net/static/license/MIT/black?scale=1.01)](LICENSE "MIT licence")
<!-- Only badges for things that exist and work (docs/guides/README-BADGES). The live badgen metadata row
     (issues, PRs, commits, branches) queries the public GitHub API and renders empty for a private
     repository; it is added by the release-gate task (T12) when the owner makes the repository public. -->

Open-source AI agent for structured exit interviews with former employees. Bring your own model: use it via MCP from your own AI client, or run the CLI with your API key or a local model. Privacy-preserving employer signals, verified employment, and an eval harness scoring interview quality from OpenTelemetry traces. .NET Aspire.

A tool and a portfolio piece, not a company and not a public review platform. The project hosts no model: you bring the compute. The goal, the non-goals and the decisions already taken are in the binding [project brief](docs/architecture/PROJECT-BRIEF.md).

## Status: scaffold only

This repository is at **phase 0**: the estate's default containerized application, initialized and green, with **no interview logic yet**. What exists today is the mechanism: an Aspire composition root, a shared kernel, one service that owns its database, a Next.js portal with its BFF, containers, and CI. The interview agent, the record schema, the ingest path, the MCP adapter, the CLI and the eval harness are later tasks (see the backlog in the brief §10).

## Non-goals, stated up front

- **Nothing is deployed, and nothing will be in this phase.** Everything is built and tested locally. The Fly.io files and the `flyio*.yml` workflows are generated and syntax-checked, never triggered: no `v*` tag, no Fly app, no GitHub secret. The repository is private; making it public is the owner's decision.
- **No Claude subscription tokens and no GitHub Copilot as a model backend.** Anthropic prohibits subscription OAuth tokens (Free/Pro/Max) in third-party tools, and the terms for using Copilot as a backend could not be verified. Supported model access will be API keys (Anthropic, OpenAI-compatible endpoints) and local models (Ollama).
- **No real interviews and no real personal data.** Simulated personas only.
- No blockchain, tokens or DAO; no public publication of individual reviews (aggregates only above a minimum count); no real employment verification (an interface and a mock, real verification is an open problem).

## Run it

Prerequisites: .NET SDK 10, Node 22+, pnpm, and a container engine (Docker or Podman).

```bash
# once: checks prerequisites, installs the pre-commit secret scan, initialises dotnet user-secrets
scripts/setup.sh            # Windows: scripts/setup.ps1   (add --check / -Check to only report)

# start the whole local stack: Postgres, authservice (pinned image), interview-service, web
dotnet run --project src/ExitInterviewAgent.AppHost
```

The AppHost prints the Aspire dashboard URL; `web` and `interview-service` get their ports from it. With no credentials at all the stack still starts with reduced features: `GET <interview-service>/health` lists what is degraded, and the startup banner prints the same list.

If `ghcr.io/konradcinkusz/authservice` cannot be pulled where you are, run without identity: `Identity__Enabled=false dotnet run --project src/ExitInterviewAgent.AppHost` (protected endpoints then answer 401 and say so). Details: [ADR-003](docs/adr/0003-identity-authservice-as-pinned-image.md).

## Test it

```bash
dotnet build -warnaserror && dotnet test                    # service, kernel guards, architecture tests
scripts/check-kernel-size.sh                                # the shared-kernel ceiling
cd web && pnpm install --frozen-lockfile && pnpm lint && pnpm typecheck && pnpm test && pnpm build
cd ../tests/e2e && pnpm install --frozen-lockfile && npx playwright install --with-deps chromium && pnpm test
```

CI runs the same (plus container image builds and workflow linting): [`.github/workflows/ci.yml`](.github/workflows/ci.yml). The end-to-end suite and its charter are in [`tests/e2e/README.md`](tests/e2e/README.md).

## What is in the tree

| Path | What it is |
|---|---|
| `src/ExitInterviewAgent.AppHost` | the composition root (P1): development only; one command brings the system up |
| `src/ExitInterviewAgent.ServiceDefaults` | the shared kernel (P2): telemetry, health, discovery, resilience, JWT validation, CORS, DB provider, rate limiting; plumbing only |
| `src/ExitInterviewAgent.Contracts` | DTOs that cross a service boundary |
| `src/ExitInterviewAgent.InterviewService` | the single service; owns `interviewdb`; `/health`, `/alive`, `GET /api/v1/me` |
| `web/app` | Next.js portal and BFF (HttpOnly-cookie sessions, runtime config, verifying edge gate, catch-all proxy) |
| `tests/` | xUnit project for the service and kernel; Playwright journeys in `tests/e2e` |
| `flyio/` | generated Fly.io topology, secrets and cost analysis (not deployed) |
| `docs/` | [architecture and deviation register](docs/architecture/00-ARCHITECTURE.md), [ADRs](docs/adr/), [UI/UX and backlog](docs/ux/UI-UX.md), diagrams |

How the brief's service layout (`interview-service` with a future `Signals` module, `authservice`, `cli`, `eval`, `web`) maps onto this tree is [ADR-002](docs/adr/0002-service-layout.md).

## Built to standards

The repository follows [`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards) (principles P1-P15). `.claude/settings.json` declares the marketplace and enables `architecture-core`. Every deviation is an ADR plus a dated row in the [register](docs/architecture/00-ARCHITECTURE.md#deviation-register). Contributing: [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Licence

[MIT](LICENSE), copyright Konrad Cinkusz.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
