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

## Status

Kept honest: **Implemented** means on `main`; everything else is a plan owned by a task in the [brief's backlog](docs/architecture/PROJECT-BRIEF.md#10-backlog-and-dependency-graph-orchestrator-assigns-sessions-may-add-rows-via-pr).

| Area | State |
|---|---|
| Scaffold: Aspire AppHost, shared kernel, one service with its own database, Next.js portal with BFF, containers, CI, secret scan | **Implemented** (T0) |
| Identity: `authservice` as a pinned image, RS256-only JWT validation against its JWKS, `GET /api/v1/me` | **Implemented** (T0, [ADR-0003](docs/adr/0003-identity-authservice-as-pinned-image.md)) |
| Documentation foundation: [privacy design](docs/privacy/DESIGN.md), [threat model](docs/security/THREAT-MODEL.md), [legal considerations](docs/legal/CONSIDERATIONS.md), [open problems](docs/OPEN-PROBLEMS.md), [evaluation methodology](docs/eval/METHODOLOGY.md) | **Implemented** (T3, documents only: they describe a design) |
| Record schema v1, validation library, deterministic PII detector | **Implemented** (T1, [ADR-0007](docs/adr/0007-record-context-bands.md)..[0011](docs/adr/0011-no-per-person-identifier-in-the-record.md); [record schema](docs/architecture/record-schema.md)) |
| Second JWT scheme for MCP (scope-enforced, RFC 9728 metadata, authservice client wired in the AppHost), BFF refresh rotation and consent step, account-deletion semantics, security headers | **Implemented** (T2, [ADR-0012](docs/adr/0012-two-jwt-schemes-and-the-mcp-resource-server.md)..[0014](docs/adr/0014-account-deletion-semantics-and-no-pii-in-telemetry.md)). Not run here: Claude completing the flow, the `v0.3.4` image |
| Interview agent core (protocol, state machine, roles, PII guard, quote step, tracing seam), scripted mock model, eight simulated personas, offline CLI demo | **Implemented** (T4, [ADR-0022](docs/adr/0022-interview-agent-core.md) to [0026](docs/adr/0026-cli-project-and-ci-artifacts.md); design in [interview-agent.md](docs/architecture/interview-agent.md)). The mock is a test seam, not a quality baseline |
| Server-side submission: validation, PII re-scan, one-per-employer ledger (rotatable keyed HMAC), receipt-code deletion, CLI submission tickets, retention purge, `EmploymentVerifier` mock (verifies nothing: [OP-1](docs/OPEN-PROBLEMS.md)). The [flow and table layout](docs/architecture/submission-flow.md) say what each table can and cannot link | **Implemented** (T5, [ADR-0027](docs/adr/0027-store-time-buckets-and-one-transaction.md)..[0031](docs/adr/0031-submission-pipeline-and-employment-verifier-seam.md)). The MCP tool (T8), web screens (T9) and CLI client (T11) that call it are not built; the PostgreSQL tests need `TEST_POSTGRES_CONNECTION` (CI sets it) |
| Model providers behind `IChatClient` and PII-free tracing | Planned (T6) |
| Evaluation harness: [behaviour spec](docs/eval/SPEC.md), 27 scenarios as data in six classes, in-process runner with trace capture, Layer 1 deterministic assertions (twelve constraints on every run), a pinned Layer 2 judge and a classifier experiment, a committed baseline and CI gate, a conformance report, a mutation proof | **Implemented** (T7, [ADR-0037](docs/adr/0037-eval-harness-architecture.md) to [0041](docs/adr/0041-mutation-proof-and-independent-rules.md); [tour](docs/eval/README.md), numbers in [METHODOLOGY](docs/eval/METHODOLOGY.md)). Mock profile only: **no real model has been evaluated and Layer 2 has not scored anything** (`skipped:no-credential`); the judge labels are author-labelled, not human |
| MCP adapter | Planned (T8) |
| Web panel features: own submissions, receipt-code deletion, tickets (consent step and account deletion exist since T2) | Planned (T9) |
| Signals (aggregates with uncertainty) | Planned (T10) |
| CLI submission with a ticket | Planned (T11) |
| Security review, release gate, results write-up | Planned (T12) |

The interview agent runs offline against simulated personas with a scripted mock model ([Try it offline](#try-it-offline)); it has **no real model provider, no submission and no interactive interviewee yet**, so no real interview can happen. Nothing is deployed, and no real person's data is processed anywhere.

## Non-goals, stated up front

- **Nothing is deployed, and nothing will be in this phase.** Everything is built and tested locally. The Fly.io files and the `flyio*.yml` workflows are generated and syntax-checked, never triggered: no `v*` tag, no Fly app, no GitHub secret. The repository is private; making it public is the owner's decision.
- **No Claude subscription tokens and no GitHub Copilot as a model backend.** See [Supported model access](#supported-model-access) for what was read and what could not be verified.
- **No real interviews and no real personal data.** Simulated personas only.
- No blockchain, tokens or DAO; no public publication of individual reviews (aggregates only above a minimum count); no real employment verification (an interface and a mock, real verification is an open problem).

## Supported model access

The project hosts no model: you bring the compute.

| Access | Supported | Notes |
|---|---|---|
| Anthropic API key | **Yes** (planned, T6) | Anthropic's Commercial Terms permit powering products with the API; whether bring-your-own-key distribution is addressed was not read. Employment-related uses carry Anthropic's high-risk requirements: see [legal considerations §1](docs/legal/CONSIDERATIONS.md#1-model-provider-terms-what-we-may-and-may-not-support) |
| OpenAI-compatible endpoint | **Yes** (planned, T6) | The terms are those of whoever hosts the endpoint. OpenAI's own terms could **not be read** (blocked) and are *Unverified* |
| Local models (Ollama) | **Yes** (planned, T6) | Ollama is MIT-licensed (read). The licence of the weights you download governs their use; none are bundled |
| Claude Free/Pro/Max subscription tokens | **No** | Anthropic's own documentation says third parties may not offer Claude.ai login or route requests through Free, Pro or Max credentials (read 2026-10-05) |
| GitHub Copilot as a backend | **No** | Not because it was found to be forbidden: GitHub's terms for this use **could not be verified** (2026-10-05) |
| Claude as an MCP host (mode A) | **Yes** (planned, T8) | You sign in to Claude with Anthropic's own client; we never see a Claude credential. The host sees your whole interview ([privacy at a glance](#privacy-at-a-glance)) |

Row-by-row evidence, with dates, sources and what could not be read: [`docs/legal/CONSIDERATIONS.md`](docs/legal/CONSIDERATIONS.md). Considerations, not legal advice.

## Privacy at a glance

Design intent ([full design](docs/privacy/DESIGN.md)); each item is **Planned** unless the [Status](#status) table says otherwise.

- The **record has no user id**. A separate **ledger** (keyed HMAC of account and employer, no content, purged after a window) enforces one submission per employer per account.
- You get a **receipt code** once; presenting it deletes the record without linking it to your account. A lost code cannot be recovered; deleting your account cannot reach your records.
- Aggregates are shown only with **at least K records** (default 5), **always with uncertainty**, and never as a composite ranking or per-person view. Single reviews are never published.
- No interview content or personal data in logs, traces or audit events.
- **Records are treated as personal data, not as anonymous** ([ADR-0018](docs/adr/0018-records-are-treated-as-personal-data.md)).
- **Who sees your interview:** with the CLI, your AI provider (or nobody, with a local model); with MCP, your AI host and its provider. Our servers receive only the record, never the transcript.
- **Honest limits:** an operator with the database, the signing key and live traffic can correlate accounts and records; small groups can be identified; we cannot verify that a submitter ever worked at the employer. See the [threat model](docs/security/THREAT-MODEL.md) and [open problems](docs/OPEN-PROBLEMS.md).
- **No real interviews** until a privacy policy, a lawful basis, working deletion and a lawyer's review exist ([legal §4](docs/legal/CONSIDERATIONS.md#4-why-real-interviews-are-out-of-scope)).

## Run it

Prerequisites: .NET SDK 10, Node 22+, pnpm, and a container engine (Docker or Podman).

```bash
# once: checks prerequisites, installs the pre-commit secret scan, initialises dotnet user-secrets
scripts/setup.sh            # Windows: scripts/setup.ps1   (add --check / -Check to only report)

# start the whole local stack: Postgres, authservice (pinned image), interview-service, web
dotnet run --project src/ExitInterviewAgent.AppHost
```

The AppHost prints the Aspire dashboard URL; `web` and `interview-service` get their ports from it. With no credentials at all the stack still starts with reduced features: `GET <interview-service>/health` lists what is degraded, and the startup banner prints the same list.

The MCP path (connecting Claude) is off until you give the AppHost two public https URLs; see [`scripts/README.md`](scripts/README.md#the-mcp-path-claude-connector-locally) and [ADR-012](docs/adr/0012-two-jwt-schemes-and-the-mcp-resource-server.md).

If `ghcr.io/konradcinkusz/authservice` cannot be pulled where you are, run without identity: `Identity__Enabled=false dotnet run --project src/ExitInterviewAgent.AppHost` (protected endpoints then answer 401 and say so). Details: [ADR-003](docs/adr/0003-identity-authservice-as-pinned-image.md).

## Try it offline

The CLI runs a whole interview with a simulated interviewee and the deterministic **mock model**: no network, no credentials, nothing submitted. The mock is a seam for tests and demos, **not a quality baseline**: it cannot show that a real model behaves well ([what the mock can and cannot show](docs/architecture/interview-agent.md#the-scripted-mock-model-what-it-can-and-cannot-show)).

```bash
dotnet run --project src/ExitInterviewAgent.Cli -- personas
dotnet run --project src/ExitInterviewAgent.Cli -- demo --persona talkative --seed 1
dotnet run --project src/ExitInterviewAgent.Cli -- demo --persona names-manager --seed 1 --out ./demo-out   # writes transcript.txt, record.json, report.txt
dotnet run --project src/ExitInterviewAgent.Cli -- demo --persona withdraws-consent                         # no transcript, no record
```

`demo` prints the masked transcript, the record JSON, the validation result and an invariant report; exit code 0 means every invariant held (1: one failed, 2: usage error). The same seed gives byte-identical output.
The personas are `talkative`, `terse`, `hostile`, `vague`, `names-manager`, `prompt-injection`, `withdraws-consent` and `contradictory` (synthetic; the employer is the fictional `widgetron-ltd`).

Self-contained single-file binaries (CI builds them for `linux-x64`, `win-x64` and `osx-arm64` as workflow artifacts and runs only the Linux one; nothing is released):

```bash
dotnet publish src/ExitInterviewAgent.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/linux-x64
out/linux-x64/exit-interview demo --persona talkative --seed 1
```

## Test it

```bash
dotnet build -warnaserror && dotnet test                    # service, kernel guards, records, PII detector, agent, personas, CLI, architecture tests
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
| `src/ExitInterviewAgent.Records`, `src/ExitInterviewAgent.Privacy` | the record schema, validation and quote fidelity ([record schema](docs/architecture/record-schema.md)); the deterministic PII detector ([PII detector](docs/privacy/pii-detector.md)) |
| `src/ExitInterviewAgent.Agent` | the interview agent core: protocol, state machine, roles, PII guard, quote step, tracing seam, scripted mock model ([interview agent](docs/architecture/interview-agent.md), [trace schema](docs/eval/TRACE-SCHEMA.md)) |
| `src/ExitInterviewAgent.Personas` | the simulated interviewees: data files, schema, seeded simulator |
| `src/ExitInterviewAgent.Cli` | `exit-interview`: the offline demo |
| `tests/` | xUnit projects mirroring the sources; Playwright journeys in `tests/e2e` |
| `flyio/` | generated Fly.io topology, secrets and cost analysis (not deployed) |
| `docs/` | [architecture and deviation register](docs/architecture/00-ARCHITECTURE.md), [ADRs](docs/adr/), [UI/UX and backlog](docs/ux/UI-UX.md), diagrams; [privacy design](docs/privacy/DESIGN.md), [threat model](docs/security/THREAT-MODEL.md), [legal considerations](docs/legal/CONSIDERATIONS.md), [open problems](docs/OPEN-PROBLEMS.md), [evaluation methodology](docs/eval/METHODOLOGY.md) |

How the brief's service layout (`interview-service` with a future `Signals` module, `authservice`, `cli`, `eval`, `web`) maps onto this tree is [ADR-002](docs/adr/0002-service-layout.md).

## Built to standards

The repository follows [`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards) (principles P1-P15). `.claude/settings.json` declares the marketplace and enables `architecture-core`. Every deviation is an ADR plus a dated row in the [register](docs/architecture/00-ARCHITECTURE.md#deviation-register). Contributing: [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Licence

[MIT](LICENSE), copyright Konrad Cinkusz.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
