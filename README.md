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

Polish guide (product, usage, privacy, quality; PDF built on demand): [`docs/papers/przewodnik.pl.tex`](docs/papers/przewodnik.pl.tex), via the *Build Guide PDF* workflow or the build steps in its header.

Kept honest: **Implemented** means on `main`; everything else is a plan owned by a task in the [brief's backlog](docs/architecture/PROJECT-BRIEF.md#10-backlog-and-dependency-graph-orchestrator-assigns-sessions-may-add-rows-via-pr).

| Area | State |
|---|---|
| Scaffold: Aspire AppHost, shared kernel, one service with its own database, Next.js portal with BFF, containers, CI, secret scan | **Implemented** (T0) |
| Identity: `authservice` as a pinned image, RS256-only JWT validation against its JWKS, `GET /api/v1/me` | **Implemented** (T0, [ADR-0003](docs/adr/0003-identity-authservice-as-pinned-image.md)) |
| Documentation foundation: [privacy design](docs/privacy/DESIGN.md), [threat model](docs/security/THREAT-MODEL.md), [legal considerations](docs/legal/CONSIDERATIONS.md), [open problems](docs/OPEN-PROBLEMS.md), [evaluation methodology](docs/eval/METHODOLOGY.md) | **Implemented** (T3, documents only: they describe a design) |
| Record schema v1, validation library, deterministic PII detector | **Implemented** (T1, [ADR-0007](docs/adr/0007-record-context-bands.md)..[0011](docs/adr/0011-no-per-person-identifier-in-the-record.md); [record schema](docs/architecture/record-schema.md)) |
| Second JWT scheme for MCP (scope-enforced, RFC 9728 metadata, authservice client wired in the AppHost), BFF refresh rotation and consent step, account-deletion semantics, security headers | **Implemented** (T2, [ADR-0012](docs/adr/0012-two-jwt-schemes-and-the-mcp-resource-server.md)..[0014](docs/adr/0014-account-deletion-semantics-and-no-pii-in-telemetry.md)). Not run here: Claude completing the flow, the `v0.3.4` image |
| Interview agent core (protocol, state machine, roles, PII guard, quote step, tracing seam), scripted mock model, eight simulated personas, offline CLI demo | **Implemented** (T4, [ADR-0022](docs/adr/0022-interview-agent-core.md) to [0026](docs/adr/0026-cli-project-and-ci-artifacts.md); design in [interview-agent.md](docs/architecture/interview-agent.md)). The mock is a test seam, not a quality baseline |
| Server-side submission: validation, PII re-scan, one-per-employer ledger (rotatable keyed HMAC), receipt-code deletion, CLI submission tickets, retention purge, `EmploymentVerifier` mock (verifies nothing: [OP-1](docs/OPEN-PROBLEMS.md)). The [flow and table layout](docs/architecture/submission-flow.md) say what each table can and cannot link | **Implemented** (T5, [ADR-0027](docs/adr/0027-store-time-buckets-and-one-transaction.md)..[0031](docs/adr/0031-submission-pipeline-and-employment-verifier-seam.md)). The MCP tool (T8) that calls it is not built (the web screens are T9, the CLI client T11); the PostgreSQL tests need `TEST_POSTGRES_CONNECTION` (CI sets it) |
| Model providers behind `IChatClient` (Anthropic API key, OpenAI-compatible endpoint, Ollama), interactive `interview` CLI, disclosure, retries and budgets, PII-free OpenTelemetry | **Implemented** (T6, [ADR-0032](docs/adr/0032-provider-packages-and-adapters.md) to [0036](docs/adr/0036-cli-interview-and-providers-commands.md); design in [providers.md](docs/architecture/providers.md)). **Not run live**: no provider was called, see [limits](#run-it-with-your-own-model) |
| Polish and deepening in the interview (ADR-0075, Y1–Y4): `--language pl\|en\|auto`, a Polish protocol (1.2), a language switch during the interview, deeper neutral follow-ups when someone reports a serious matter (bullying, harassment and similar), and draft tiles at the end of every `interview` (`--no-tiles` to skip). [Plan, tasks and what was measured](docs/architecture/interview-v2.md) | **Implemented** (PRs #34–#37). **No real-model evaluation**: the Polish wording, the deepening questions and the tile texts with a real model are unmeasured; the serious-account cues are a word list, not a classifier |
| Evaluation harness: [behaviour spec](docs/eval/SPEC.md), 30 scenarios as data in six classes, in-process runner with trace capture, Layer 1 deterministic assertions (twelve constraints on every run), a pinned Layer 2 judge and a classifier experiment, a committed baseline and CI gate, a conformance report, a mutation proof | **Implemented** (T7, [ADR-0037](docs/adr/0037-eval-harness-architecture.md) to [0041](docs/adr/0041-mutation-proof-and-independent-rules.md); [tour](docs/eval/README.md), numbers in [METHODOLOGY](docs/eval/METHODOLOGY.md)). Mock profile only: **no real model has been evaluated and Layer 2 has not scored anything** (`skipped:no-credential`); the judge labels are author-labelled, not human |
| MCP server (mode A): Streamable HTTP, stateless, official SDK; prompt `conduct_exit_interview`, three versioned resources, tools `validate_interview_record` and `submit_interview_record`; Origin, protocol-version and size guard; content canary over the MCP path. [Design](docs/architecture/mcp.md), [operator runbook](docs/guides/connect-claude.md) | **Implemented** (T8, [ADR-0042](docs/adr/0042-mcp-sdk-and-streamable-http-stateless.md)..[0046](docs/adr/0046-mcp-not-found-and-error-vocabulary.md)). **Not run: a real Claude client** (the connector flow, prompt discoverability and host behaviour are unverified, [OP-18](docs/OPEN-PROBLEMS.md)). In this mode the host and its provider see the whole conversation; only the record reaches the server. It is the weakest of the three modes for privacy and fidelity |
| Web panel: landing, sign-in with two-factor, sign-up and email verification, consent, connect-your-AI-client, CLI ticket (shown once), deletion by receipt code, account (data export, deletion), privacy page; nonce CSP, no-store, same-origin check; axe-core gate. There is no "my submissions" list, by design | **Implemented** (T9, [ADR-0047](docs/adr/0047-nonce-csp-and-style-policy.md)..[0051](docs/adr/0051-message-catalog-accessibility-gate-and-stub-contract.md); [UI and UX](docs/ux/UI-UX.md)). Tested against a stub backend, not the real services ([OP-17](docs/OPEN-PROBLEMS.md)); English only; no manual accessibility pass yet ([OP-16](docs/OPEN-PROBLEMS.md)) |
| Signals: employer aggregates with uncertainty. Per displayed cell k (default 5), single-band cuts that are published whole or not at all, batched snapshots (a day by default), deletions at the next batch, an interval that is not falsely precise at small n, coverage next to every rating, no score, no ranking. Two read endpoints (account policy), removable synthetic demo data. [How, and what k does not protect against](docs/privacy/AGGREGATION.md); [module](docs/architecture/signals.md) | **Implemented** (T10, [ADR-0052](docs/adr/0052-signals-module-boundary-input-port-and-store.md)..[0056](docs/adr/0056-signals-api-caching-rate-limits-and-demo-data.md)). The PostgreSQL tests need `TEST_POSTGRES_CONNECTION` (CI sets it) |
| Signals in the web panel: `/signals` (alphabetical list, "this tool does not rank employers") and `/signals/<employer>` (six topics, each mean with its interval and n on one line, reliability, coverage, distribution, verification, three single-band cuts; "not enough responses" shows no number; a hidden breakdown names no band). The pages render what the API returned and derive nothing; no sort, no score, no comparison. [UI and UX](docs/ux/UI-UX.md#signals-screens), [copy contract](docs/privacy/AGGREGATION.md#8-api-contract-and-ui-copy-contract) | **Implemented** (T10b, [ADR-0067](docs/adr/0067-signals-pages-render-the-api-and-derive-nothing.md)..[0071](docs/adr/0071-signals-browser-suite-absence-tests-and-mutation-proof.md)). Tested against the stub that mirrors the contract, **not against the running service** ([OP-28](docs/OPEN-PROBLEMS.md)); no screen-reader pass ([OP-16](docs/OPEN-PROBLEMS.md)); comprehension untested ([OP-26](docs/OPEN-PROBLEMS.md)) |
| CLI submission with a one-time ticket (`submit`), receipt code shown once, deletion by receipt code (`delete-receipt`); local re-check, exact preview, typed confirmation; no flag carries a secret | **Implemented** (T11, [ADR-0057](docs/adr/0057-cli-submit-and-delete-receipt-commands.md)..[0061](docs/adr/0061-cli-receipt-handling.md); [design](docs/architecture/cli-submission.md)). Tested against a contract-mirroring fake and in-process against the real service; **not run against a deployment** or over real TLS ([OP-23](docs/OPEN-PROBLEMS.md)..[OP-25](docs/OPEN-PROBLEMS.md)) |
| Draft texts from the record: `tiles` (up to six tiles, one per kind: a fact list built by code and model-written texts that pass a code-side guard; text, HTML and JSON output) | **Implemented** (X1–X4, [ADR-0074](docs/adr/0074-draft-tiles-from-the-record.md); [design and what was measured](docs/architecture/draft-tiles.md)). **No real model has written a tile**: the tests use the scripted mock and a fake transport, and the mock's wording is mechanical, not a quality measure |
| Security review, release gate, results write-up | Planned (T12) |

The interview agent runs offline against simulated personas with a scripted mock model ([Try it offline](#try-it-offline)); it can also run with your own model ([Run it with your own model](#run-it-with-your-own-model)), but it has **no submission yet**. Nothing is deployed, and this project processes no real person's data anywhere: it has no server that receives a transcript.

## Non-goals, stated up front

- **Nothing is deployed, and nothing will be in this phase.** Everything is built and tested locally. The Fly.io files and the `flyio*.yml` workflows are generated and syntax-checked, never triggered: no `v*` tag, no Fly app, no GitHub secret. The repository is private; making it public is the owner's decision.
- **No Claude subscription tokens and no GitHub Copilot as a model backend.** See [Supported model access](#supported-model-access) for what was read and what could not be verified.
- **No real interviews are offered or supported, and no real personal data is processed by this project.** The shipped personas are simulated. The CLI can technically hold a conversation with you; read [the limits](#run-it-with-your-own-model) before typing anything real.
- No blockchain, tokens or DAO; no public publication of individual reviews (aggregates only above a minimum count); no real employment verification (an interface and a mock, real verification is an open problem).

## Supported model access

The project hosts no model: you bring the compute.

| Access | Supported | Notes |
|---|---|---|
| Anthropic API key | **Yes** (T6; not run live) | Anthropic's Commercial Terms permit powering products with the API; whether bring-your-own-key distribution is addressed was not read. Employment-related uses carry Anthropic's high-risk requirements: see [legal considerations §1](docs/legal/CONSIDERATIONS.md#1-model-provider-terms-what-we-may-and-may-not-support) |
| OpenAI-compatible endpoint | **Yes** (T6; not run live) | The terms are those of whoever hosts the endpoint. OpenAI's own terms could **not be read** (blocked) and are *Unverified* |
| Local models (Ollama) | **Yes** (T6; not run live) | Ollama is MIT-licensed (read). The licence of the weights you download governs their use; none are bundled |
| Claude Free/Pro/Max subscription tokens | **No** | Anthropic's own documentation says third parties may not offer Claude.ai login or route requests through Free, Pro or Max credentials (read 2026-10-05) |
| GitHub Copilot as a backend | **No** | Not because it was found to be forbidden: GitHub's terms for this use **could not be verified** (2026-10-05) |
| Claude as an MCP host (mode A) | **Yes** (implemented, T8; never run against a real Claude client, [OP-18](docs/OPEN-PROBLEMS.md)) | You sign in to Claude with Anthropic's own client; we never see a Claude credential. The host sees your whole interview ([privacy at a glance](#privacy-at-a-glance)) |

Row-by-row evidence, with dates, sources and what could not be read: [`docs/legal/CONSIDERATIONS.md`](docs/legal/CONSIDERATIONS.md). Considerations, not legal advice.

## Privacy at a glance

Design intent ([full design](docs/privacy/DESIGN.md)); each item is **Planned** unless the [Status](#status) table says otherwise.

- The **record has no user id**. A separate **ledger** (keyed HMAC of account and employer, no content, purged after a window) enforces one submission per employer per account.
- You get a **receipt code** once; presenting it deletes the record without linking it to your account. A lost code cannot be recovered; deleting your account cannot reach your records.
- Aggregates are shown only with **at least K ratings in every displayed cell** (default 5), published in **batches** (deletions appear at the next one), **always with uncertainty**, and never as a composite ranking or per-person view. Single reviews are never published. K is a convention, not a guarantee: [what it does not protect against](docs/privacy/AGGREGATION.md#7-what-k-does-not-protect-against).
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

## Run it with your own model

The project hosts no model. `exit-interview interview` runs a real interview in your terminal with **your API key** (Anthropic, or any OpenAI-compatible endpoint) or **a local model** (Ollama). The transcript stays in memory on your machine; only your model provider sees it. Nothing is submitted unless you run `submit` and type its confirmation word ([submit and delete](#submit-and-delete-with-the-cli)).

**Before you start, read this:**

- **Your provider sees the whole conversation** (every question and every answer), under *your* account's terms, retention and training rules. This project has not verified those for you. The CLI says so and asks you to type `yes` before the first call to an external provider (`--yes-i-understand` for scripts). A model on your own machine gets a shorter notice and no prompt.
- **This is not a way to run real interviews.** No privacy policy, lawful basis, working deletion or lawyer's review exists yet ([legal §4](docs/legal/CONSIDERATIONS.md#4-why-real-interviews-are-out-of-scope)). Use invented answers and a fictional employer while the project is in this state. Nothing here is legal advice.
- **Ctrl-C or Ctrl-D stops the interview and discards everything**: no record, no transcript, nothing written.
- **Keys come from environment variables only.** There is no `--api-key` flag, and a config file with a key in it is refused. A key is never printed, logged, traced or put in an error.
- **Not supported, on purpose:** Claude Free/Pro/Max subscription credentials (Anthropic's documentation says third parties may not route requests through them; naming `CLAUDE_CODE_OAUTH_TOKEN` as a key source is refused) and GitHub Copilot (its terms for this use **could not be verified**; that is why there is no adapter, not a finding that it is prohibited). Limit: no recognisable format for a subscription token is documented, so one pasted into `ANTHROPIC_API_KEY` is not recognised here and is left to the API to reject.

```bash
# What can run, with no network call:
dotnet run --project src/ExitInterviewAgent.Cli -- providers

# Anthropic API (your key, in the environment; pick a model id from your own account):
export ANTHROPIC_API_KEY=...        # from the Anthropic Console, never a Claude subscription token
export ANTHROPIC_WORKSPACE_ID=... # only if the API answers HTTP 400 'not scoped to a workspace': the id of the workspace to use (Windows PowerShell: $env:ANTHROPIC_API_KEY = "...")
dotnet run --project src/ExitInterviewAgent.Cli -- providers ping --provider anthropic --model <model-id>      # one minimal live request, only when you run it
dotnet run --project src/ExitInterviewAgent.Cli -- interview --provider anthropic --model <model-id> --tenure 1y_3y --employer acme-example --out ./interview-out

# Any OpenAI-compatible endpoint (hosted, or a local gateway; the base URL includes /v1):
export OPENAI_API_KEY=...           # optional for a gateway you point at yourself
dotnet run --project src/ExitInterviewAgent.Cli -- interview --provider openai-compatible --base-url https://gateway.example/v1 --model <model-id> --tenure 1y_3y

# A local model with Ollama (no key; nothing leaves your machine through this program):
ollama pull <model>                 # the licence of the weights you pull governs their use
dotnet run --project src/ExitInterviewAgent.Cli -- interview --provider ollama --model <model> --num-ctx 8192 --tenure 1y_3y

# From a script (confirms the notice without a prompt; use only if you have read it):
dotnet run --project src/ExitInterviewAgent.Cli -- interview --provider anthropic --model <model-id> --tenure 1y_3y --yes-i-understand

# Polish interview, no draft texts at the end (--no-tiles), written to ./wywiad:
dotnet run --project src/ExitInterviewAgent.Cli -- interview --provider anthropic --model <model-id> --tenure 1y_3y --language pl --no-tiles --out ./wywiad
```

Windows PowerShell (the same Polish interview, written to `.\wywiad`; the variables last for this session only):

```powershell
$env:ANTHROPIC_API_KEY = "..."        # from the Anthropic Console, never a Claude subscription token
$env:ANTHROPIC_WORKSPACE_ID = "..."   # only if the API answers HTTP 400 'not scoped to a workspace'
.\exit-interview.exe interview --provider anthropic --model <model-id> --language pl --out .\wywiad
```

`--out <dir>` writes `record.json` only if a record was produced and validated; `--save-transcript` (with `--out`) also writes `transcript.txt`. Without `--out` nothing is written. The record is printed with its validation result either way. Tenure is asked for if `--tenure` is missing (`lt_6m`, `6m_1y`, `1y_3y`, `3y_5y`, `5y_10y`, `gt_10y`).

Settings come from flags, then environment variables (`EXIT_INTERVIEW_PROVIDER`, `EXIT_INTERVIEW_MODEL`, `EXIT_INTERVIEW_BASE_URL`, `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `OLLAMA_HOST`, and more), then an optional config file with no secrets in it. The full table, the trust boundary, failure behaviour and how to add a provider: [`docs/architecture/providers.md`](docs/architecture/providers.md).

Cost and limits: there is a hard per-interview budget (tokens and calls, derived from the protocol's own budget; `--max-tokens` lowers it) and retries with backoff on rate limits and server errors. **No price is built in** (a price list cannot be verified from here): give `--price-in` and `--price-out` (per million tokens) and `--max-cost` if you want a cost figure or ceiling. OpenTelemetry export is **off** unless you set `OTEL_TRACES_EXPORTER` / `OTEL_METRICS_EXPORTER` or an OTLP endpoint; prompts and replies are never exported and there is no switch for it.

**What was and was not verified:** the adapters, retries, refusals, disclosure, the Ctrl-C/EOF behaviour and the leak canary (key, headers, prompt text, error bodies) are tested against fakes in CI. **No call to a real provider has been made** (no key and a restricted network where this was built); the optional live smoke tests (`Category=Live`) skip unless you set a key and `EXIT_INTERVIEW_LIVE_MODEL`, and never run in CI. **Nothing here measures how well a real model interviews**: that is the evaluation harness's job (T7) and is not done.

**Language and tiles at the end of `interview` (Y4).** `--language pl|en|auto` sets the interview's language (`auto` follows your system: Polish if it is Polish, else English). It moves during the interview when a reply of at least five words is written mostly in the other language, or when you ask for it ("po polsku", "in English"); the next question follows, with one sentence to confirm the switch. `interview.language` in the record is the language most of your answers were written in. The switch counts Polish diacritics and common words in each language, not a model: it is tested on short examples and is **not measured with a real model**. A completed interview also ends with draft texts (tiles), made by the same provider from the same budget, shown below the record and, with `--out`, written as new files to `tiles/tiles.json` and `tiles/tiles.html`. `--no-tiles` skips them. The disclosure says so before you confirm. A second run into the same `--out` folder is refused before the interview, so nothing is overwritten.

**Draft texts with `tiles`.** `tiles` turns a validated `record.json` (not the transcript) into up to six short draft texts: a fact list built by code from your ratings, and model-written texts that pass a code-side guard. A text that fails the guard is dropped and counted, never edited. Only the record goes to an external provider, after the same typed `yes` as `interview`; `mock` and a local model send nothing. Nothing is published, submitted or stored, except the two new files `--out` writes (`tiles.json` and `tiles.html`, never overwritten). The texts are proposals: read and change each one before you use it. **You are responsible for what you publish**, and an opinion about a real employer can have legal consequences; the program verifies no facts. **No real model has written a tile**, so the wording and the cost are unmeasured. Exit codes: 0 ok (dropped texts are counted), 2 usage or configuration, 3 not confirmed, 4 the record failed the local check, 5 provider failure, 130 cancelled. Design and what was measured: [draft-tiles.md](docs/architecture/draft-tiles.md).

**Deeper questions when a matter is serious (Y2, ADR-0075).** When an answer reports something serious (a word list in English and Polish: bullying or mobbing, harassment, discrimination, threats, retaliation, unsafe conditions, unpaid wages and similar), the interviewer asks up to four neutral, fact-seeking follow-ups on that topic: what happened, roughly when and how often, who by role only (no names are recorded), how the employer responded, and how it ended. Each may open with one sentence that reflects your own words, with no judgement and no advice. After the first follow-up it says once that you can skip or stop at any time. Limits: the list is lexical, so it misses paraphrases and many Polish inflections, and it can fire on a word used in a harmless sense. The wording of these questions with a real model is **not measured**.

**Before you publish anything.** A draft about a real employer, especially one that uses a word such as "mobbing" or "harassment", can create legal exposure for the person who publishes it, depending on where you live and what the text says (defamation, data protection and employment law can all apply). This program does not check any fact, is not legal advice, and cannot remove that risk. The tiles never fill in the company name (they carry `[COMPANY]` or `[FIRMA]`), but a detailed account can identify people or the employer anyway. Read every text, change it, and consider not publishing it at all. Platform rules on length and content change; check them before you post.

```bash
# Offline, nothing sent (scripted mock; its wording is mechanical, not a quality measure):
dotnet run --project src/ExitInterviewAgent.Cli -- tiles --record ./demo-out/record.json --provider mock --model scripted --out ./tiles-out

# Your own model: only the record is sent, after you type "yes"; add --format json or html to change standard output:
dotnet run --project src/ExitInterviewAgent.Cli -- tiles --record ./interview-out/record.json --provider anthropic --model <model-id>
```

## Submit and delete with the CLI

Submitting is optional, separate, and never automatic. You need a record file (`interview --out`), the address of a service that accepts it (**there is no default**; nothing is
deployed, so the commands below use a placeholder), and a **one-time ticket** minted on the web panel's `/cli` page (sign in, "Create a ticket"; it is shown once and works for 15 to 20 minutes).
The CLI has no login and no OAuth ([brief §4](docs/architecture/PROJECT-BRIEF.md)). Full design, outcomes and limits: [`docs/architecture/cli-submission.md`](docs/architecture/cli-submission.md).

```bash
# 1. Interview with your own model; the record is written only because you asked (--out):
dotnet run --project src/ExitInterviewAgent.Cli -- interview --provider ollama --model <model> --tenure 1y_3y --employer acme-example --out ./interview-out

# 2. Submit it. The CLI re-checks the record, shows exactly what leaves your machine and the record itself, and waits for you to type "submit".
#    Only then does it ask for the ticket (hidden prompt; or EXIT_INTERVIEW_TICKET; or one line of standard input). There is no --ticket flag.
export EXIT_INTERVIEW_SERVER_URL=https://<the deployment you mean>     # https required (http only for localhost); or pass --server
dotnet run --project src/ExitInterviewAgent.Cli -- submit --record ./interview-out/record.json --save-receipt ./receipt.txt

# 3. The receipt code was shown once (and saved to ./receipt.txt, mode 0600, never overwriting). It is the only way to delete the record; nobody can look it up for you.
#    The code is read from a hidden prompt, EXIT_INTERVIEW_RECEIPT_CODE or standard input, never from an argument:
dotnet run --project src/ExitInterviewAgent.Cli -- delete-receipt < ./receipt.txt
# -> "If a record with this receipt code existed, it is deleted now."  (the service answers the same for every well-formed code)
```

The same without a model, using the offline demo's record (a simulated interview; this is how the flow was exercised by hand against a local stub of the service):

```bash
dotnet run --project src/ExitInterviewAgent.Cli -- demo --persona talkative --seed 1 --out ./demo-out
EXIT_INTERVIEW_SERVER_URL=http://127.0.0.1:5080 dotnet run --project src/ExitInterviewAgent.Cli -- submit --record ./demo-out/record.json
```

What it does and does not do: only the record and the ticket leave your machine (no transcript, no names, no provider details); redirects are never followed; nothing is retried after a byte was
sent; the ticket and the receipt code never appear in logs, files or errors (canary-tested), and the receipt code is on screen once. `submit --yes` skips the confirmation and exists for tests and scripts
you control. Exit codes: 0 submitted, 2 usage, 3 not confirmed or no ticket, 4 the record failed the local check, 5 the server refused, 6 network failure or unknown outcome, 130 cancelled.
The redemption instant still links your web account to the record for the server's operator ([threat model T-09](docs/security/THREAT-MODEL.md)); that is a stated, accepted limit.

## Test it

```bash
dotnet build -warnaserror && dotnet test                    # service, kernel guards, records, PII detector, agent, personas, providers, CLI, architecture tests
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
| `src/ExitInterviewAgent.Providers` | model providers behind `IChatClient`: Anthropic (official SDK), OpenAI-compatible, Ollama; transport policy, budget, disclosure, telemetry ([providers](docs/architecture/providers.md)) |
| `src/ExitInterviewAgent.Cli` | `exit-interview`: the offline demo, the interactive interview with your own model, `providers` |
| `tests/` | xUnit projects mirroring the sources; Playwright journeys in `tests/e2e` |
| `flyio/` | generated Fly.io topology, secrets and cost analysis (not deployed) |
| `docs/` | [architecture and deviation register](docs/architecture/00-ARCHITECTURE.md), [ADRs](docs/adr/), [UI/UX and backlog](docs/ux/UI-UX.md), diagrams; [privacy design](docs/privacy/DESIGN.md), [threat model](docs/security/THREAT-MODEL.md), [legal considerations](docs/legal/CONSIDERATIONS.md), [open problems](docs/OPEN-PROBLEMS.md), [evaluation methodology](docs/eval/METHODOLOGY.md) |

How the brief's service layout (`interview-service` with a future `Signals` module, `authservice`, `cli`, `eval`, `web`) maps onto this tree is [ADR-002](docs/adr/0002-service-layout.md).

## Built to standards

The repository follows [`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards) (principles P1-P15). `.claude/settings.json` declares the marketplace and enables `architecture-core`. Every deviation is an ADR plus a dated row in the [register](docs/architecture/00-ARCHITECTURE.md#deviation-register). Contributing: [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Licence

[MIT](LICENSE), copyright Konrad Cinkusz.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
