# Working in this repository (for AI agents and humans)

Read first, in this order: [`docs/architecture/PROJECT-BRIEF.md`](docs/architecture/PROJECT-BRIEF.md) (binding: goal,
non-goals, owner decisions, operating contract), then [`docs/architecture/00-ARCHITECTURE.md`](docs/architecture/00-ARCHITECTURE.md)
(where each principle lives, the deviation register). The standards live in
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards): read the one
for the layer you touch before writing it; do not re-derive them.

## Commands

```bash
scripts/setup.sh --check                           # what is missing on this machine
dotnet build -warnaserror && dotnet test           # backend, kernel guards, architecture tests
dotnet format --verify-no-changes                  # formatting gate (drop the flag to fix)
scripts/check-kernel-size.sh                       # P2 ceiling
cd web && pnpm lint && pnpm typecheck && pnpm test && pnpm build
cd tests/e2e && pnpm test                          # needs the web build first
scripts/scan-secrets.sh                            # gitleaks over history; --staged for the hook
python3 scripts/check-doc-links.py                 # every relative Markdown link resolves
dotnet run --project src/ExitInterviewAgent.AppHost
```

## Rules that bite

- **Nothing is deployed.** Never push a `v*` tag, create a Fly app, or add a GitHub secret. `flyio*.yml` are generated, not run.
- **No secrets, no real personal data, no model identifiers** in commits, code comments or docs. Dev secrets are generated and documented as dev-only.
- **The kernel stays a kernel.** `ExitInterviewAgent.ServiceDefaults` is plumbing: no entity, DTO, enum or seed data. CI enforces a size ceiling and an architecture test.
- **One signing key, held by authservice.** This repository validates RS256 tokens against a JWKS and never mints. authservice is consumed as a pinned image, never copied.
- **Migrate, never ensure** (outside InMemory). Migrations are generated with `dotnet ef` (tool pinned in `dotnet-tools.json`) into `Persistence/Migrations`.
- **Optional integrations degrade and are visible**: register them with `AddIntegration` so `/health` and the startup banner list them.
- Package versions live only in `Directory.Packages.props`; web dependencies are pinned in `web/app/package.json`.
- A test that cannot fail is worse than none. A gate you could not run is reported as not run.
- Tooling gotcha: do not `pkill -f <name>` from a shell whose command line contains that name; kill by PID.

## Adding things

- **A .NET project:** folder under `src/` or `tests/`, `dotnet sln add`, `ProjectReference`s, versions in `Directory.Packages.props`. Tests mirror source as `tests/<Project>.Tests`. If it ships as a container: Dockerfile next to it, `flyio/<name>.fly.toml` (declare `dockerfile` and `context`), a matrix row in `flyio.yml`.
- **An endpoint:** add it in the service's `Endpoints/`, mount it on the right group in `Infrastructure/ServiceCollectionExtensions.cs` (public vs authenticated), clamp every list with `ApiExtensions.ClampPage`, and test it.
- **An ADR:** copy `docs/adr/0000-template.md`, take the next number, cite the principle, add a register row if it deviates.
