# 0001. Stack, licence and scope of this phase

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: reference architecture P1-P15 (the stack is the standard's documented one);
  `INIT-GENERIC-TEMPLATE.md` §12 (frameworks the standards evidence); `PROJECT-BRIEF.md` §2-§3.

## Context

The owner decided the licence and the local-only scope (brief §2, §3.3-3.4). The standards document
.NET Aspire and Next.js as the estate's stack and say a different choice is a recorded decision, not a
substitution. The sibling identity service `authservice` targets `net10.0` (checked:
`grep TargetFramework` over its `src/*/*.csproj`), and a consumer that validates its tokens gains
nothing from a lower TFM.

## Decision

- **Backend:** .NET 10 (`net10.0`, SDK pinned by `global.json`), ASP.NET Core minimal APIs, EF Core 10 with
  Npgsql, OpenTelemetry. **Composition root:** .NET Aspire AppHost, Aspire SDK 13.6.0 (the newest stable
  on nuget.org when queried on 2026-10-05). **Frontend:** Next.js 16 with the App Router and a BFF in
  route handlers, one pnpm workspace (`web/`). **Tests:** xUnit, Vitest, Playwright.
- **Licence:** MIT, copyright "Konrad Cinkusz" (already present in `LICENSE`).
- **Scope of this phase:** build and test locally only. No deployment of any kind: no `v*` tag, no Fly
  app, no GitHub secret. Deployment artefacts the scaffold generates (Dockerfiles, `flyio/*.fly.toml`,
  `flyio*.yml` workflows) are kept and syntax-checked but never triggered. The repository is not made
  public by any session.
- Decisions are recorded as ADRs in this directory; the numbering is chronological and not reused.

## Consequences

Contributors need the .NET 10 SDK, Node 22+, pnpm and a container engine (`scripts/setup.sh --check`
reports what is missing). Nothing in the repository can be mistaken for a running service. Deployment is
a later, separately decided phase; ADR-004 lists what that phase must decide first.
