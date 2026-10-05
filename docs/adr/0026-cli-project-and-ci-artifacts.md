# 0026. CLI project: offline demo now, self-contained binaries as workflow artifacts

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): ADR-0002 (service layout: `cli` is a .NET self-contained project), brief §2 (no deployment, no releases, no tags), brief §5 (mode B), `repo-baseline` (CI proves what the README claims)

## Context

ADR-0002 anticipated `src/ExitInterviewAgent.Cli` "referencing `Contracts` only". T4 needs a CLI that runs a whole interview offline with the mock model and a persona; T11 will add submission with a ticket.

## Decision

- **`src/ExitInterviewAgent.Cli`** (assembly and command `exit-interview`) with two commands: `demo --persona <id> [--seed <n>] [--out <dir>]` and `personas`. No network, no credentials, no submission. `demo` prints the masked transcript, the record, the validation result, an invariant report and a one-line run summary; exit code 0 when every invariant holds, 1 when one fails, 2 on a usage error. Arguments are parsed by hand: two commands do not justify a new dependency.
- **Deviation from ADR-0002's "`Contracts` only":** the CLI references `Agent` and `Personas`. The reason is the purpose of T4, not drift: the CLI is where the agent runs, and `Contracts` holds DTOs that cross a service boundary, which the demo does not use. When T11 adds submission it will reference `Contracts` for the ingest request, as ADR-0002 intended. Dependency direction is tested: nothing in `Agent`, `Personas`, `Records` or `Privacy` references the CLI.
- **Self-contained publish** is a CI job, not a release: one matrix job publishes single-file self-contained binaries for `linux-x64`, `win-x64` and `osx-arm64` and uploads them as **workflow artifacts** (no release, no tag, no secret, nothing deployed), then smoke-runs the Linux binary with `demo` and checks its output. It does not use trimming (the schema validator and JSON mapping use reflection; the size saving is not worth an untested trim configuration). The CLI is not a container, so no Dockerfile, `flyio` file or matrix row in `flyio.yml` is added.
- `--seed` fixes everything random (persona choices, simulated clock, interview id); the id is derived from persona and seed **for simulation only** (real interviews use `InterviewId.NewRandom`). Output is byte-identical for the same seed (a test).

## Consequences

- The offline demo is the first thing a reader can run (README, "Try it offline").
- The Windows and macOS binaries are built in CI but **not executed** there (the job runs on Linux); that is stated in the workflow and the README.
- Not covered: an interactive interviewee, provider selection and the submission step (T6, T11).
