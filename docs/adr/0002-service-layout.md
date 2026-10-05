# 0002. How the scaffolded service maps to the brief's service layout

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: P3 (service per bounded context, database per service);
  `INIT-GENERIC-TEMPLATE.md` §12 ("no second service", "no sample domain model");
  `PROJECT-BRIEF.md` §4 (Services).

## Context

The brief names `authservice`, `interview-service` (with a `Signals` module), `web`, `cli` and `eval`.
The template scaffolds exactly one service that owns its database and forbids inventing a domain model
or a second service. `cli` and `eval` are not services, and the standards do not call for placeholder
projects for them.

## Decision

| Brief | In the repository now | Notes |
|---|---|---|
| `interview-service` | `src/ExitInterviewAgent.InterviewService` (the single scaffolded service) | owns `interviewdb`; `Program.cs` is a manifest; one thin slice (`GET /api/v1/me`); empty `InterviewDbContext` with a baseline migration |
| `Signals` module | **not created yet** | arrives as a separate project in the same service: `ExitInterviewAgent.Signals`, its own schema and `DbContext`, no shared domain types with the interview side, boundary enforced by an architecture test. Extraction into a service is a future ADR (brief §4) |
| `authservice` | pinned image in the AppHost and `flyio/authservice.fly.toml` | separate instance, own database and signing key, never copied (ADR-003) |
| `web` | `web/app` | Next.js with the BFF; the only thing the browser talks to |
| `cli` | **not created yet** | a .NET self-contained project `src/ExitInterviewAgent.Cli` added by T4/T11, referencing `Contracts` only |
| `eval` | **not created yet** | a project `src/ExitInterviewAgent.Eval` added by T7, with its own test project |

No placeholder projects are reserved: an empty project is a promise nobody has checked. Test projects
mirror source projects as `tests/<Project>.Tests`. The shared kernel (`ExitInterviewAgent.ServiceDefaults`)
and `ExitInterviewAgent.Contracts` stay free of domain types; record schemas live where the brief puts
them (T1 decides the project and records it in its own ADR).

## Consequences

The template's "no second service" rule is not violated: `signals-service` is a module, `cli` and `eval`
are solution projects, and `authservice` is a consumed artifact. Adding a project means: a folder under
`src/` or `tests/`, `dotnet sln add`, package versions in `Directory.Packages.props` only, and, if it ships
as a container, a Dockerfile plus a `flyio/*.fly.toml` and a matrix row in `flyio.yml`.
