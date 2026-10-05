# Architecture

This repository is measured against the estate's constitution,
[`00-REFERENCE-ARCHITECTURE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md)
(P1-P15), which is referenced here, not copied. Project facts (goal, non-goals, owner decisions) are in
[`PROJECT-BRIEF.md`](PROJECT-BRIEF.md); where the two disagree on a *rule*, the standards win and the
deviation goes in the register below. Decisions are recorded in [`../adr/`](../adr/).

## Shape today

One composition root, one shared kernel, one service that owns its database, one product surface, and
one consumed identity service. See [`../diagrams/system.mmd`](../diagrams/system.mmd).

| Project | Role | Direct NuGet / npm dependencies |
|---|---|---|
| `src/ExitInterviewAgent.AppHost` | P1: composition root, development only | 2 |
| `src/ExitInterviewAgent.ServiceDefaults` | P2: the shared kernel (plumbing only) | 12 |
| `src/ExitInterviewAgent.Contracts` | DTOs and stable rejection codes that cross a boundary (submission, tickets) | 0 |
| `src/ExitInterviewAgent.InterviewService` | the one service; owns `interviewdb`; submission, ledger, receipts, tickets, retention ([submission-flow](submission-flow.md)) | 1 (+ kernel, contracts, records, privacy) |
| `src/ExitInterviewAgent.Records` | the interview record: immutable model, schema validation, canonical form, quote fidelity ([record-schema](record-schema.md)) | 1 (JSON-Schema validator, ADR-0008) |
| `src/ExitInterviewAgent.Privacy` | deterministic PII detector and masker ([pii-detector](../privacy/pii-detector.md)) | 0 |
| `src/ExitInterviewAgent.Agent` | the interview agent core: protocol, state machine, roles, PII guard, quote step, tracing seam, scripted mock model ([interview-agent](interview-agent.md)) | 3 (model abstractions, logging abstractions, JSON-Schema validator) |
| `src/ExitInterviewAgent.Personas` | seeded, data-driven interviewee simulators (ADR-0024) | 1 (JSON-Schema validator) |
| `src/ExitInterviewAgent.Providers` | model providers behind `IChatClient`: Anthropic SDK, OpenAI-compatible, Ollama; transport policy, budget, disclosure, provider telemetry ([providers](providers.md), ADR-0032..0035) | 4 (Anthropic SDK, Microsoft's OpenAI adapter, model abstractions, logging abstractions) |
| `src/ExitInterviewAgent.Cli` | `exit-interview`: offline demo (ADR-0026), interactive interview and `providers` (ADR-0036) | 3 (OpenTelemetry SDK, console exporter, OTLP exporter) |
| `src/ExitInterviewAgent.Eval` | the evaluation harness, library and `exit-interview-eval` tool: scenarios as data, runner, Layer 1 and Layer 2, gate, baseline, report (ADR-0037..0041, [eval README](../eval/README.md)) | 2 (YAML reader, JSON-Schema validator) |
| `schemas/` | published, versioned schemas: the record (v1 immutable once released, ADR-0009), the extractor output, the persona | n/a |
| `tests/ExitInterviewAgent.Records.Tests`, `tests/ExitInterviewAgent.Privacy.Tests` | xUnit; golden fixtures; architecture tests; PII evaluation corpus | 3 each |
| `tests/ExitInterviewAgent.InterviewService.Tests` | xUnit; InMemory; architecture tests | 4 |
| `tests/ExitInterviewAgent.Agent.Tests`, `tests/ExitInterviewAgent.Personas.Tests`, `tests/ExitInterviewAgent.Cli.Tests`, `tests/ExitInterviewAgent.Providers.Tests` | xUnit; state machine, per-rule and per-persona end-to-end tests, the canary trace test (extended to every provider client), fake-HTTP adapter tests on a fake clock, architecture tests | 3 each |
| `tests/ExitInterviewAgent.Eval.Tests` | xUnit; corpus validation, statistics, fault injection, Layer 1, the broken-variant mutation tests, judge, calibration, gate, report, architecture (anti-goals) | 3 |
| `web/app` | Next.js product surface + BFF | 4 runtime, 7 dev |
| `tests/e2e` | Playwright journeys against the production artifact | 1 runtime, 2 dev |

The dependency counts come from `grep -c '<PackageReference' <csproj>` and the key counts of
`dependencies` / `devDependencies` in each `package.json`; update them in the pull request that changes
them (REPO-BASELINE §4b).

## Guides loaded for this scaffold

`INIT-GENERIC-TEMPLATE`, `00-REFERENCE-ARCHITECTURE`, `REPO-BASELINE`, `FLY-IO-DEPLOYMENT`, `FRONTEND-BFF`,
`SERVICE-API-PATTERNS`, `IDENTITY-AND-ACCOUNTS`, `SHARED-SERVICE-REUSE`, `TESTING-STRATEGY`,
`E2E-ACCEPTANCE-TESTING`, `README-BADGES`. The submission task (T5, ADR-0027..0031) loaded the reference architecture (P3, P4, P5, P8, P11, P13),
`SERVICE-API-PATTERNS`, `SECURITY-REVIEW`, `TESTING-STRATEGY`, `METRIC-ETHICS`, `IDENTITY-AND-ACCOUNTS` and `DEMO-DATA-AND-SEEDING`. The identity task (T2, ADR-0012..0014) loaded `IDENTITY-AND-ACCOUNTS`,
`SHARED-SERVICE-REUSE`, `FRONTEND-BFF`, `SERVICE-API-PATTERNS`, `SECURITY-REVIEW`, `TESTING-STRATEGY` and the reference
architecture (P5, P8, P11). The interview-agent task (T4, ADR-0022..0026) loaded `AI-EVALS`, `METRIC-ETHICS`, `TESTING-STRATEGY`,
`DEMO-DATA-AND-SEEDING`, `SERVICE-API-PATTERNS` and `SECURITY-REVIEW`. The providers task (T6, ADR-0032..0036) loaded the reference architecture (P5, P8, P10, P15), `AI-EVALS`, `SECURITY-REVIEW`, `TESTING-STRATEGY` and `SERVICE-API-PATTERNS`. (Not loaded here, loaded by the task that needs them:
The eval-harness task (T7, ADR-0037..0041) loaded `AI-EVALS`, `METRIC-ETHICS`, `TESTING-STRATEGY`, `DEMO-DATA-AND-SEEDING`, `research-documentation` and P13 and P15 of the reference architecture. (Not loaded here, loaded by the task that needs them:
`open-source-release`.)

## Where each principle lives

| Principle | Where |
|---|---|
| P1 AppHost is the composition root | `src/ExitInterviewAgent.AppHost/Program.cs` (`WithReference`, `WaitFor`, `WithHttpHealthCheck`; no secret literal) |
| P2 kernel is plumbing | `ExitInterviewAgent.ServiceDefaults`; ceiling in `scripts/check-kernel-size.sh` (CI); boundary in `tests/**/Architecture/KernelBoundaryTests.cs` |
| P3 database per service | `interviewdb` (this service), `authdb` (authservice); roles per database in `flyio/postgres.fly.toml`. The ledger is a table of `interviewdb`, not a separate database: [OP-13](../OPEN-PROBLEMS.md#op-13-storage-level-correlation-between-the-ledger-and-the-records) |
| P4 migrate, never ensure | `MigrationExtensions` (hosted service after Kestrel); migrations `InitialBaseline` and `SubmissionStore` (a PostgreSQL test compares the migrated schema with the model and the documented columns); deviation ADR-006 |
| P5 one signing key, config via environment | authservice holds the only key (ADR-003); the kernel's `AuthenticationExtensions` and the service's `McpAuthenticationExtensions` validate RS256 only (two schemes, ADR-012); scanner in hook and CI |
| P6 container per service | `src/ExitInterviewAgent.InterviewService/Dockerfile`, `web/app/Dockerfile` |
| P7 Fly topology | `flyio/*.fly.toml`, `flyio/INFRASTRUCTURE-ANALYSIS.md` (generated, not deployed) |
| P8 optional dependencies degrade | `IntegrationStatus`: `/health` lists identity, mcp-auth, database, telemetry-export, ledger-key and employment-verifier (an unavailable verifier degrades submissions to level `unchecked`, ADR-0031); startup banner prints the same |
| P11 anti-corruption at the edge | two token dialects become one principal (`sub`, `client_id`, `scope`) in `McpAuthenticationExtensions` (ADR-012); web, MCP and ticket entry points all call one `SubmissionService` (ADR-0031) |
| P9 `Program.cs` is a manifest | `InterviewService/Program.cs` calls into `Infrastructure/ServiceCollectionExtensions.cs` |
| P12 tag-driven CI/CD | `.github/workflows/flyio.yml` (never triggered) |
| P13 test at the layer with the logic | service tests (InMemory), PostgreSQL tests for what InMemory cannot enforce (unique indexes, atomic delete, migrations; skipped and reported as not run without `TEST_POSTGRES_CONNECTION`), Vitest for BFF logic, Playwright for the journey |
| P14 docs in the repo | this directory; README claims are checked by `scripts/check-doc-links.py` and review |
| P15 observability | OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; probes filtered from traces; no scopes exported. The CLI exports only when asked (`OTEL_TRACES_EXPORTER`, `OTEL_METRICS_EXPORTER`, an OTLP endpoint), only the agent and provider sources, never content (ADR-0035) |
| P10 extensibility through interface + registration | a model provider is an `IChatClient` plus a `ProviderCatalog` row and one `case` in `ProviderChatClients.Create` ([providers](providers.md#how-to-add-a-provider)) |

## Deviation register

Every row carries a date and a reason. An acknowledged deviation is a decision; an unacknowledged one is drift.

| Date | Deviation | Principle or guide | Reason | Record |
|---|---|---|---|---|
| 2026-10-05 | Dependabot declared, `open-pull-requests-limit: 0` | `REPO-BASELINE` §1 | no maintainer to triage on day one; audit covers vulnerabilities; trigger: a triaging maintainer or going public | ADR-005 |
| 2026-10-05 | PostgreSQL and InMemory only, no SqlServer | P4 | no requirement; one migrations set | ADR-006 |
| 2026-10-05 | CodeQL results kept as run artifacts, not uploaded to code scanning | `REPO-BASELINE` §1 | private repo; GHAS not assumed; flip `CODEQL_UPLOAD` when public | ADR-004 |
| 2026-10-05 | No PDF overview track (`docs/papers/`, `build-overview-pdf.yml`) | `INIT-GENERIC-TEMPLATE` §9 (optional) | nothing hands anyone a PDF yet; the results write-up is T12 | none needed |
| 2026-10-05 | The CLI references `Agent` and `Personas`, not `Contracts` only | ADR-0002 (`cli` row) | the CLI is where the agent runs; `Contracts` holds DTOs that cross a service boundary and the demo crosses none; T11 adds the `Contracts` reference for submission | ADR-0026 |
| 2026-10-05 | The Ollama client is hand-written, not `OllamaSharp` (the NuGet-recommended package) | brief §4 ("verify the package choice") | one endpoint; avoids a second dependency tree on an older abstractions line; the Microsoft package is deprecated; trigger: tools, thinking mode or model management | ADR-0032 |
| 2026-10-05 | The Agent project gained a failure-code seam (`IModelFailure`, `ModelCallFailedException.IsFatal`) and the runner rethrows fatal model failures | ADR-0023 (T6 adds providers without touching the agent) | a wrong key must stop an interview at the first call, not after a fallback-worded one; the seam carries a code, never a message | ADR-0034 |
| 2026-10-05 | `GET /health` is readiness (503 until the schema is applied) and carries the integration list | P4/P8 | one request answers "what is live?"; Fly checks `/health` with a 60 s grace period | this table |

## Known limits of the scaffold (not deviations)

- The BFF rotates refresh tokens (single-flight, per process) and gates on consent (ADR-013); two-factor sign-in is unsupported (501).
- `interview-service` has the submission slice since T5 (submit, tickets, receipt deletion, retention; no signals, no read of stored records
  by any endpoint), `GET /api/v1/me`, and the MCP mount point (`/mcp`, scope-guarded, no transport until T8; a Development-only submit probe
  stands in for the tool).
- The in-process limiters and the InMemory write gate are single-instance mechanisms; a second replica needs shared state (threat model T-18).
- The web CSP allows inline scripts and styles (Next emits inline bootstrap scripts; no per-request nonces yet): a weaker CSP than a
  nonce-based one, same-origin otherwise (`web/app/lib/security-headers.ts`). Trigger: nonce support when the portal gets user-rendered content.
- The edge gate is Next 16's `proxy.ts` (formerly `middleware.ts`); it is not the BFF catch-all under
  `app/api/proxy`.
