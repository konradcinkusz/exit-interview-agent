# Draft tiles: neutral texts the interviewee may publish

Status: **planned** (ADR-0074). Nothing in this page is implemented except the contract in
`src/ExitInterviewAgent.Agent/Tiles/TileContracts.cs`. Update this page as each task below lands.

## What it is

After a CLI interview the person has a validated record (six topics, ratings, short quotes). The `tiles` command turns that
record into a handful of **tiles**: short, neutral, factual draft texts the person can copy and publish themselves if they
want to (a LinkedIn note, a review site, a message to the team). Nothing is published by the program. Nothing is sent to a
server. The model sees the record, never the transcript.

## Scope and non-goals

In scope: the `tiles` CLI command, a local HTML/text view, the writer role, the code-side guard, offline tests on the mock
model, documentation.

Out of scope, decided with the owner: a hosted service that spends the owner's API key; any payment; publishing on the
person's behalf; employer-facing output; the MCP and portal paths. The hosted variant would host transcripts and needs its own
threat model and legal review (see ADR-0018); it is a separate project, not a follow-up of this one.

## Design

- **Input: the record only.** The record is already PII-masked and validated. Quotes are the only free text in it. The
  transcript is never an input.
- **Six tiles at most**, one per `TileKind`: `Facts` (built by code: ratings and confidence per covered topic, nothing the
  model wrote), `Overview`, `WhatWorked`, `WhatCouldImprove`, `ForTheNextPerson`, `ShortNote` (<= 280 characters).
- **Model-written tiles come from an `ITileWriter`** (role marker `ROLE: tilewriter`), whose output is untrusted JSON parsed
  against `schemas/tile-writer-output.v1.schema.json`, then built into `CandidateTile`s by our code.
- **Every candidate passes `ITileGuard`** before it is shown: length, banned terms, PII (fail closed), grounding, quote
  copying (see the contract file for the limits and drop codes). A failing tile is dropped, never edited; the user is told
  how many were dropped and why (codes only). One retry of the writer per run, carrying only the drop codes.
- **Grounding rule.** A tile may only draw on topics that are `covered` in the record. A tile that names a `no_data` topic is
  dropped as `ungrounded`. Ratings are described in words consistent with the number (1-2 negative, 3 mixed, 4-5 positive);
  a tile may not be more positive or more negative than the rating it cites.
- **Neutral tone** (writer prompt and banned-term list, both reviewed in the writer task): first person is allowed, no
  names, no employer name (the record only holds an opaque reference), no accusations of illegal or unethical conduct, no
  health or protected-characteristic content, no superlatives, no speculation about motives. These are rules the program
  enforces where it can (banned terms) and asks of the model where it cannot; the guard is a floor, not a guarantee.
- **Disclosure.** With an external provider the record (not the transcript) goes to that provider; the command says so and
  asks for the same confirmation as `interview`. With `mock` or Ollama nothing leaves the machine.
- **The person is responsible for what they publish.** The output carries a fixed notice saying so, that the program does not
  verify facts, and that publishing about an employer can have legal consequences. This is a notice, not legal advice.
- **Telemetry:** metadata only (counts, drop codes, model id, token counts), same rule as the interview (ADR-0025).

## Output

`exit-interview tiles --record <record.json> [--provider <p> --model <m> ...] [--out <dir>] [--format text|html|json]`

- Terminal: the tiles as numbered blocks, the dropped codes, the notice. Exit codes follow the `interview` command where they
  apply (2 usage, 5 provider failure, 3 declined disclosure).
- `--out`: `tiles.json` (the `TileSet`, camelCase), `tiles.html` (self-contained, no scripts from the network, one card per
  tile, a copy button using no external library, the notice at the top).

## Tasks

Wave 0 (this PR): contract, plan, ADR. Wave 1 tasks are independent and can run in parallel; each touches different files.

| ID | Wave | Title | Files it owns | Depends on |
|---|---|---|---|---|
| X1 | 1 | Guard: length, banned terms, PII, grounding, quote copying | `src/ExitInterviewAgent.Agent/Tiles/TileGuard.cs`, `tests/ExitInterviewAgent.Agent.Tests/Tiles/TileGuardTests.cs` | contract |
| X2 | 1 | Writer role, prompt, parser, schema, scripted mock, generator | `Tiles/TileWriter.cs`, `Tiles/TilePrompts.cs`, `Tiles/TileGenerator.cs`, `Tiles/FactsTile.cs`, `schemas/tile-writer-output.v1.schema.json`, mock additions in `Mock/`, tests in `tests/ExitInterviewAgent.Agent.Tests/Tiles/` | contract |
| X3 | 1 | Renderers: text and self-contained HTML | `src/ExitInterviewAgent.Cli/Tiles/TileRenderer.cs`, `tests/ExitInterviewAgent.Cli.Tests/Tiles/` | contract |
| X4 | 2 | `tiles` command: flags, disclosure, wiring, `--out`, end-to-end offline test | `src/ExitInterviewAgent.Cli/TilesCommand.cs`, a one-line dispatch in `CliApp.cs`, usage text, `tests/ExitInterviewAgent.Cli.Tests/` | X1, X2, X3 |
| X5 | 3 | Docs: this page's status, README, guide exercise, ADR-0074 status, `RELEASING`/CLI mentions | `docs/`, `README.md`, `docs/papers/` | X4 |

Deferred, not part of this plan: eval-harness constraints for tiles (they change `docs/eval/SPEC.md` and the baseline and
need their own review), real-model quality and cost measurement (needs the owner's key), Polish and other languages beyond
what the writer prompt does with the record's language.

## Definition of done per task

`dotnet build -warnaserror`, `dotnet test` for the touched projects, `dotnet format --verify-no-changes`, no AI model
identifier in any file, commit or PR text, no network in tests, tests written first for guard rules. A PR per task, merged by
the session when all CI checks are green.
