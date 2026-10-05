# exit-interview-agent — project brief and operating contract

Binding for every session working on this repository. Written 2026-10-05 by the orchestrator
session on the owner's instructions. Where this file and the standards disagree on a *rule*,
the standards win and the deviation goes in an ADR. Where they disagree on a *project fact*
(scope, decisions below), this file wins.

## 1. Goal

An open-source AI agent that runs structured exit interviews with former employees, produces
a structured, privacy-preserving record per interview, aggregates records into comparable
employer signals, and ships an evaluation harness scoring the QUALITY OF THE INTERVIEW (not
the employer, not the person) from OpenTelemetry traces. A tool and a portfolio piece, not a
company and not a public review platform. The project hosts no model: the user brings the
compute.

Repo description (GitHub "About"): "Open-source AI agent for structured exit interviews with
former employees. Bring your own model: use it via MCP from your own AI client, or run the CLI
with your API key or a local model. Privacy-preserving employer signals, verified employment,
and an eval harness scoring interview quality from OpenTelemetry traces. .NET Aspire."
Topics: ai-agents, llm, mcp, model-context-protocol, opentelemetry, llm-evaluation,
agent-evals, employer-reviews, exit-interview, hr-tech, privacy-by-design, gdpr, dotnet,
aspire, nextjs, open-source.

## 2. Non-goals

- No blockchain, tokens, DAO governance (immutability conflicts with GDPR erasure).
- No public publication of individual reviews; only aggregates above a minimum count.
- No real interviews from real people: simulated personas only. No real personal data ever.
- No real employment verification: define the `EmploymentVerifier` interface, ship a mock,
  document real verification as an open problem.
- Do NOT support Claude subscription/OAuth tokens or GitHub Copilot as a model backend.
  Anthropic prohibits subscription OAuth tokens (Free/Pro/Max) in third-party tools; Copilot
  terms for use as a backend were not verifiable. Say so in the README. Supported model
  access: API keys (Anthropic, OpenAI-compatible endpoints) and local models (Ollama).
- **No deployment of any kind** in this phase of the project. Everything is built and tested
  locally (Aspire AppHost, containers, `dotnet test`, Playwright against localhost). Do not
  push `v*` tags, do not create Fly apps, do not add secrets to GitHub. Deployment artefacts
  the scaffold generates (Dockerfiles, `fly.toml`, workflows) are kept, but never triggered.
- The repository is NOT made public by any session. The release gate is prepared and run;
  flipping visibility is the owner's decision.

## 3. Decisions already taken by the owner (do not re-open)

1. Portal login is an **account only**: it has no link to any employer. The employer is a
   field of the submitted record. Never model "this account belongs to employer X".
2. MCP path supports **Claude only** for now (redirect URI
   `https://claude.ai/api/mcp/auth_callback` serves claude.ai web, desktop, mobile). Other
   hosts (ChatGPT etc.) are documented as future work; each needs its own pre-registered
   client in authservice, which is operator-held configuration.
3. Local-only, no deployment (see §2).
4. Licence: **MIT**, copyright "Konrad Cinkusz".
5. All remaining decisions are the sessions' to take, each recorded as an ADR under
   `docs/adr/` with the principle it serves (or deviates from).

## 4. Decisions taken by the orchestrator (change only via ADR)

- **CLI login.** `authservice` registers only *confidential* OAuth clients (client secret
  >= 32 bytes, no `none` auth method, no device grant, no DCR, no client credentials, no
  introspection; see authservice ADR-0005 and `AuthorizationServerOptions.cs`). A published
  CLI binary cannot hold a secret, so the CLI does not use the MCP authorization server.
  Instead: the web panel (BFF, authenticated through authservice) mints a **submission
  ticket** — random, short-lived, single-use, NOT bound to any employer. The CLI sends
  record + ticket. Redeeming the ticket yields the account `sub` once, from which the ledger
  entry is computed; the ticket row is then deleted. This needs no change to authservice.
  Residual risk: the redemption instant is a correlation point (document in the threat
  model). A later ADR may revisit public-client support in authservice (its ADR-0005 would
  have to be amended by its owner).
- **MCP path.** `interview-service` exposes an MCP endpoint. Claude connects with OAuth 2.1
  against an authservice instance with `AuthorizationServer:Clients` configured. MCP access
  tokens are audience-bound to the MCP resource URI and refused by authservice's own API, so
  `interview-service` has **two JWT schemes**: the regular one (web/BFF; issuer/audience from
  authservice `Jwt:*`) and the MCP one (`iss` == authservice `Jwt:PublicBaseUrl`, `aud` ==
  the MCP endpoint's canonical URI, scope enforced by us). Verify against the authservice
  source (`/docs/DEPLOYMENT.md#registering-an-mcp-client`, ADR-0005), do not assume.
- **Services.** `authservice` (reused as pinned image, own DB and signing key, never copied),
  `interview-service` (ingest, validation, PII detection, ledger, tickets, deletion, thin MCP
  adapter, `EmploymentVerifier`, and the `Signals` module), `web` (Next.js + BFF), `cli`
  (.NET, self-contained), `eval` (project in the solution). `signals-service` starts as a
  module of `interview-service` with an enforced context boundary in code (separate project,
  own schema/DbContext, no shared domain types); extraction to a service is a future ADR.
- **Model access** behind `Microsoft.Extensions.AI` `IChatClient` (verify the package choice
  and versions on NuGet and justify in an ADR). Providers: Anthropic API key,
  OpenAI-compatible endpoint, Ollama, plus a deterministic mock model for offline/CI.
- **Do not rely on MCP sampling** unless client support is verified and recorded in an ADR.

## 5. Usage modes

A. **MCP server** — the user connects Claude; the host model runs the interview following
   the protocol the server exposes (prompts/resources) and submits the record via a tool.
   The server never sees the transcript.
B. **CLI `exit-interview`** — .NET, self-contained binaries. Runs the whole multi-agent
   interview (interviewer, prober, record extractor, PII guard) locally with the user's API
   key or a local model; transcript stays on the machine; submits only the record.
C. **Web app** — Next.js with BFF: account, consents, own submissions, deletion by receipt,
   submission tickets for the CLI, aggregates with uncertainty. It runs no model.

## 6. Record and privacy design

- Versioned JSON Schema record: per-topic rating (1-5 or null), verbatim supporting quotes
  from the transcript only, confidence, PII-masked flag, pseudonymous interview id. NO user
  id in the record. Topics: onboarding, management, growth, pay vs promises, culture,
  reason for leaving.
- **Submission ledger**: separate table, keyed HMAC of (sub, employer id) with a rotatable
  key; no content, no record id; purged after a configurable window. One submission per
  employer per account. Document that an operator with DB + key access could correlate;
  list as residual risk in the threat model.
- **Deletion by receipt code**: on submission the user gets a random code; the server keeps
  its hash; presenting the code deletes the record without linking it to the account.
- Aggregates only when n >= K (configurable, default 5), always with uncertainty; no single
  composite employer ranking; follow metric-ethics (anti-goals enforced by architecture,
  counter-metrics, confidence on every number, unit of evaluation is the artifact).
- No PII or interview content in logs, traces, or authservice audit events.
- Agent rules: no leading questions; ask for a concrete example when a claim is vague; never
  ask for or store names of individuals (detect and mask); respect consent withdrawal and
  stop on request.
- Every record from a client is untrusted input: schema validation, PII detection, rate
  limits, size limits.

## 7. Evaluation harness

Reuse the approach of `konradcinkusz/agent-eval-bench` (public; clone it read-only:
`GIT_LFS_SKIP_SMUDGE=1 git clone --depth 1 https://github.com/konradcinkusz/agent-eval-bench`).
It is the reference implementation of the `ai-evals` guide: spec-first contract, scenarios as
YAML validated by JSON Schema, in-process `ScenarioRunner`, one captured trace per scenario,
Layer 1 deterministic assertions on traces, Layer 2 calibrated LLM judge (`skipped:no-credential`
rather than silent green), CI gates (constraints 100%, behaviours vs baseline). Port the
approach, do not copy blindly; cite it.

Personas: talkative, terse, hostile, vague, tries to name a manager, prompt-injection attempt,
withdraws consent midway, contradictory answers. Metrics: topic coverage, leading-question
rate, follow-up-on-vague rate, transcript fidelity, PII leakage, edge-case handling, cost and
latency per interview. It doubles as a conformance suite (same interview, different models and
hosts, comparable report). Every number in any document must come from a command that
reproduces it (`research-documentation` guide).

## 8. Standards (binding)

Repo: https://github.com/konradcinkusz/architecture-standards . In a session it is attached
via `add_repo` (read) and cloned. **Read the standards; do not re-derive them.**

- Start with `docs/architecture/00-REFERENCE-ARCHITECTURE.md` (P1-P15 + checklist) and
  `docs/PLAYBOOK.md`; route to guides through `catalog/marketplace.catalog.json`.
- Load the guide for the domain you touch *before* writing that layer: identity-and-accounts,
  frontend-bff, service-api-patterns, shared-service-reuse, ai-evals, metric-ethics,
  testing-strategy, e2e-acceptance-testing, demo-data-and-seeding, security-review,
  fly-io-deployment, repo-baseline, open-source-release, readme-badges,
  research-documentation. Say in the PR which guides you loaded.
- The `architecture-core` plugin is NOT installed in sessions, so skills are not invocable:
  follow `docs/scaffold/INIT-GENERIC-TEMPLATE.md` by hand.
- authservice: https://github.com/konradcinkusz/authservice (attach with `add_repo`, read).
  Reuse as a separate instance with its own DB and signing key (RS256, JWKS), consumed as a
  pinned image `ghcr.io/konradcinkusz/authservice:<tag>` per `shared-service-reuse`. Never
  copy its code. Verify the latest published tag; if none is published, record that in an ADR
  and consume it the way the guide allows for local development.
- Any deviation from a principle needs an ADR citing the principle. No silent deviations.

## 9. Operating contract for every session (non-negotiable)

1. **Autonomy.** The owner has delegated all decisions. Do not ask the owner for approval or
   clarification; decide, write the ADR, continue. Only stop (and message the orchestrator)
   when a requirement conflicts with the privacy constraints in §6, or when something outside
   this repository must change.
2. **Atomic and reviewable.** One task = one branch `claude/<task-slug>` off the latest
   `main` = one PR. Small ordered commits. Never commit secrets or real personal data.
3. **Prove it before pushing.** Run the repo's own fast checks (build, format/lint, typecheck,
   the tests of what you changed). A change that turns CI red costs everyone. Reproduce a
   failure before fixing it.
4. **Open the PR ready for review** (not draft) against `main` with `mcp__github__*` tools
   (no `gh` CLI). Mirror `.github/pull_request_template.md` once the scaffold lands. The PR
   description lists: what, why, guides loaded, ADRs added, how it was verified, and anything
   deferred.
5. **Drive it to green and merge it.** Subscribe to the PR (`subscribe_pr_activity`), fix red
   CI and review findings, never skip/disable/quarantine a test, never push empty commits.
   When CI is green on the current head, there is no merge conflict and no unresolved review
   thread, **squash-merge it yourself** (`mcp__github__merge_pull_request`), delete the
   branch, then unsubscribe. Before opening and before merging, merge `origin/main` into your
   branch; other sessions work in parallel, so expect conflicts in solution files, workflow
   files and docs indexes and resolve them preserving both sides' intent.
6. **Report back.** After merging, `send_message` to the orchestrator session (id in your
   task prompt) with: PR link, what landed, ADRs added, open problems discovered, and the
   next task you recommend. Then either continue with the next atomic task in your assigned
   scope (new branch, new PR) or stop. If you finish with nothing left, say so in the message.
7. **Every number in docs/README comes from a reproducible command.** If it cannot be
   measured, say so. Do not invent claims about the market, the law, or provider terms; mark
   assumptions explicitly.
8. **No model identifiers** (names or versions of the model/assistant you are) in commits,
   PR titles/bodies, code comments or docs. Use the commit/PR attribution lines your session
   instructions give you.
9. **Docs in English.** Chat with the owner/orchestrator can be Polish or English.
10. Child sessions may spawn their own sub-sessions for independent atomic work (depth
    limit 8), but the parent stays responsible for the PR it owns.

## 10. Backlog and dependency graph (orchestrator assigns; sessions may add rows via PR)

| ID | Task | Needs |
|---|---|---|
| T0 | Scaffold from `INIT-GENERIC-TEMPLATE.md`, baseline, CI, ADR-001 (stack/licence), this brief committed under `docs/architecture/` | — |
| T1 | Record JSON Schema + .NET contracts + validation + PII-masking detector library | T0 |
| T2 | authservice instance in AppHost; two JWT schemes in interview-service; consents; account deletion hooks | T0 |
| T3 | Docs foundation: privacy design, threat model v0 (STRIDE), legal considerations ("considerations, not legal advice", provider-terms table), open problems, eval methodology skeleton | T0 |
| T4 | Persona simulator + interview agent core (interviewer/prober/extractor/PII guard) + mock model + offline CLI demo | T1 |
| T5 | interview-service ingest: validation, PII detection, rate/size limits, ledger HMAC, one-per-employer, receipt-code deletion, submission tickets, `EmploymentVerifier` + mock | T1, T2 |
| T6 | Model providers via `IChatClient` (Anthropic key, OpenAI-compatible, Ollama) + OTel tracing without PII | T4 |
| T7 | Eval harness: scenarios YAML + schema, trace assertions, calibrated judge, CI baseline gate, conformance report | T4, T6 |
| T8 | MCP adapter (prompts/resources/submit tool) + OAuth for Claude | T5 |
| T9 | Web panel (Next.js BFF): account, consents, submissions, deletion, tickets | T2, T5 |
| T10 | Signals module: aggregates n>=K with uncertainty, read model, web view | T5, T9 |
| T11 | CLI submission with ticket | T5, T4 |
| T12 | Security review (`security-review` guide), threat model final, results write-up (`research-documentation`), README badges (real ones only), `open-source-release` gate run | all |

Phases map: T0 = phase 0; T1,T4 = 1; T2,T5 = 2; T6 = 3; T7 = 4; T8,T9,T10,T11 = 5; T12 = 6.
