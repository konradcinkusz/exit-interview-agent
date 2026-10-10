# Architecture Decision Records

This directory holds all architecture decisions made during the project, indexed by domain. Every ADR is marked as `accepted` (a principle or decision adopted into the brief/architecture), `proposed` (awaiting decision), or `deprecated` (superseded).

**Total: 69 ADRs** (numbered 0001–0076). Gaps: numbers 0015–0016, 0020–0021 and 0064–0066 have no files (brak plików o tych numerach); nothing in the repository says they are reserved. Counted with `ls docs/adr/[0-9]*.md | grep -vc 0000` (the template is excluded).

---

## 1. Foundation and Stack (0001–0005)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0001](0001-stack-licence-and-scope.md) | Stack, licence and scope | accepted | MIT licence; project scope covers the backend service, CLI, web portal, and evaluation harness; no client library |
| [0002](0002-service-layout.md) | Service layout | accepted | Backend service organized in layers (API, business logic, persistence); shared defaults library; infrastructure code in `src/` and `flyio/` |
| [0003](0003-identity-authservice-as-pinned-image.md) | Identity: authservice as pinned image | accepted | Identity service consumed as a pinned Docker image (v0.3.4), never from this repository; signing key held by authservice only |
| [0004](0004-deployment-topology-and-registry.md) | Deployment topology and registry | accepted | Services deployed as Fly.io apps; secrets in Fly.io; image registry at `ghcr.io/konradcinkusz`; single production instance per service |
| [0005](0005-dependency-automation-declared-off.md) | Dependency automation declared off | accepted | Dependabot is disabled; vulnerability scanning by manual review and CI audits; lockfiles committed (no drift between dev and CI) |

---

## 2. Record Schema and Validation (0006–0011)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0006](0006-persistence-providers.md) | Persistence providers | accepted | PostgreSQL in production; InMemory for testing; EF Core migrations into typed, assembled repositories |
| [0007](0007-record-context-bands.md) | Record context: bands | accepted | Tenure, role and level bands (narrow bands to stay above k-anonymity thresholds); topics fixed (six categories); no hierarchies |
| [0008](0008-validation-library-and-json-schema-package.md) | Validation library and JSON Schema package | accepted | Custom validation library in `RecordSchema.Validation`; schema published as JSON Schema in `web/public/schema.json` |
| [0009](0009-record-schema-versioning.md) | Record schema versioning | accepted | Version pinned in the schema and in the API; v1 is immutable; v2+ require migration and database versioning |
| [0010](0010-pii-detector-deterministic-heuristics.md) | PII detector: deterministic heuristics | accepted | Regex-based detection (names, email, phone, SSN, credit card); no ML; high precision, accepts false negatives; fast and testable |
| [0011](0011-no-per-person-identifier-in-the-record.md) | No per-person identifier in the record | accepted | Records carry no `sub`, name or reference that links to an account; unlinked by design; submission is a separate step |

---

## 3. Authentication and Authorization (0012–0014)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0012](0012-two-jwt-schemes-and-the-mcp-resource-server.md) | Two JWT schemes and the MCP resource server | accepted | Web uses RS256 at `/api/*` with web audience; MCP uses RS256 at `/mcp` with resource-server audience; cross-scheme replay prevented by policy |
| [0013](0013-bff-session-refresh-rotation-and-consent-gate.md) | BFF session: refresh rotation and consent gate | accepted | Refresh token rotates on every use (single-flight); replay revokes the family at authservice; logout revokes all sessions; consent step before submission |
| [0014](0014-account-deletion-semantics-and-no-pii-in-telemetry.md) | Account deletion semantics and no PII in telemetry | accepted | Email never reaches handlers (scrubbed by principal); telemetry logs and traces have no PII or content; canary test enforces absence |

---

## 4. Privacy and Design Stance (0017–0019)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0017](0017-documentation-layout-and-claim-status.md) | Documentation layout and claim status | accepted | Principle P14; every claim has a status (Implemented, Planned, Decided, Proposal, Assumption); markdown links are relative |
| [0018](0018-records-are-treated-as-personal-data.md) | Records are treated as personal data | accepted | Principle P16; records are GDPR personal data by design stance; deletion by receipt code; no linking without the ledger key |
| [0019](0019-brief-amendments-from-the-t3-legal-privacy-review.md) | Brief amendments from the T3 legal/privacy review | accepted | Amendments adopted from legal review; no real interviews until deployment readiness; real employment verification is an open problem |

---

## 5. Interview Agent and CLI Core (0022–0026)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0022](0022-interview-agent-core.md) | Interview agent core | accepted | State machine owns flow; model generates text only (no write tools); submission is a separate, user-confirmed step |
| [0023](0023-model-seam-and-scripted-mock.md) | Model seam and scripted mock | accepted | `IChatClient` interface; mock implementation uses scripted replies; pluggable provider factory for testing |
| [0024](0024-persona-simulator.md) | Persona simulator | accepted | Eight personas (talkative, vague, contradictory, terse, hostile, injection, withdrawal, etc.); test corpus; Layer 1 constraint scenarios |
| [0025](0025-trace-schema-metadata-only.md) | Trace schema: metadata only | accepted | OpenTelemetry traces contain no content (quotes, employer, account); only counts, durations, exit codes; canary-testable absence |
| [0026](0026-cli-project-and-ci-artifacts.md) | CLI project and CI artifacts | accepted | Separate `CliApp` project; binary not released in this phase (no signing or provenance yet); SDK builds only for tests |

---

## 6. Submission Pipeline (0027–0031)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0027](0027-store-time-buckets-and-one-transaction.md) | Store time buckets and one transaction | accepted | Records and ledger entries written in one transaction; coarse timestamps (week bucket in ledger); ledger purges after window (365 days default) |
| [0028](0028-submission-ledger-hmac-rotation-and-window.md) | Submission ledger: HMAC rotation and window | accepted | Keyed HMAC-SHA-256 over (sub, employer); key outside the database and rotatable; ledger has no record/receipt link; purged after window |
| [0029](0029-receipt-deletion-semantics.md) | Receipt deletion semantics | accepted | 256-bit random codes (hash stored), constant-time compare, identical `204` response for unknown/deleted/valid, rate-limited (6/min per IP, 60/min global) |
| [0030](0030-submission-tickets-for-the-cli.md) | Submission tickets for the CLI | accepted | Random 256-bit ticket, only hash and `sub` stored, 15-minute TTL, at most 3 live and 10 mints/hour per account; atomic delete at redemption |
| [0031](0031-submission-pipeline-and-employment-verifier-seam.md) | Submission pipeline and employment verifier seam | accepted | Pluggable verifier; mock returns `Verified` for all; real verification is an open problem; one submission per (account, employer) in ledger window |

---

## 7. Model Providers (0032–0036)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0032](0032-provider-packages-and-adapters.md) | Provider packages and adapters | accepted | Separate packages for Anthropic, OpenAI-compatible, Ollama; adapters implement `IModelProvider`; telemetry at the boundary |
| [0033](0033-provider-configuration-credentials-and-disclosure.md) | Provider configuration, credentials and disclosure | accepted | CLI warns before first call which provider is chosen; user confirms with typed `yes`; terms table in legal docs; local Ollama supported |
| [0034](0034-resilience-budget-and-failure-semantics.md) | Resilience budget and failure semantics | accepted | Interview has turn budget (tokens, retries, timeouts); provider errors surface to the user; CLI offers a fallback provider |
| [0035](0035-provider-telemetry-and-export.md) | Provider telemetry and export | accepted | No prompt/completion text in logs or metrics; canary-tested absence; switches disabled by default; safe defaults |
| [0036](0036-cli-interview-and-providers-commands.md) | CLI interview and providers commands | accepted | `cli interview <url>` (mode B); `cli providers` lists available providers; provider chosen at runtime; no config file in this phase |

---

## 8. Evaluation Harness (0037–0041)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0037](0037-eval-harness-architecture.md) | Eval harness architecture | accepted | Layer 1 (deterministic assertions, 12 hard constraints); Layer 2 (LLM judge, not gated); separate test project; 27 scenarios across 6 classes (the count in `evals/scenarios/`; this ADR was written with 26) |
| [0038](0038-model-profiles-and-provider-registration.md) | Model profiles and provider registration | accepted | Profiles defined per provider (mock, claude, gpt4, ollama); mock runs offline; real profiles require credentials and explicit auth |
| [0039](0039-layer-2-judge-and-calibration-policy.md) | Layer 2 judge and calibration policy | accepted | Judge scores are advisory until calibrated against human labels; rubric and prompt SHA-256 pinned; skipped when no credential (`skipped:no-credential`) |
| [0040](0040-baseline-gates-and-regeneration-rule.md) | Baseline gates and regeneration rule | accepted | Layer 1 constraints are gated; metrics must not regress beyond tolerance; baseline in `evals/baseline.json`; regenerate by ADR note |
| [0041](0041-mutation-proof-and-independent-rules.md) | Mutation proof and independent rules | accepted | 12 real-code mutations caught by constraints (100% detection); each constraint independent (no overlap); false-negative mutations detected |

---

## 9. MCP (Mode A) Protocol (0042–0046)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0042](0042-mcp-sdk-and-streamable-http-stateless.md) | MCP SDK and Streamable HTTP stateless | accepted | Official MCP SDK (`ModelContextProtocol`); Streamable HTTP (stateless, no session); one `submit_interview_record` and `delete_receipt` tools |
| [0043](0043-mcp-transport-guard-and-closed-vocabulary.md) | MCP transport guard and closed vocabulary | accepted | Body scrubber replaces unknown method, tool, prompt, resource names with constants before SDK parsing; no content leakage in logs/metrics |
| [0044](0044-mcp-tool-prompt-resource-contract.md) | MCP tool, prompt and resource contract | accepted | Tool schema is the record schema (no free-text transcript field); prompts static, versioned, reviewed; resource URI is the MCP `aud` |
| [0045](0045-mode-a-host-fidelity-and-opening-note.md) | Mode A host fidelity and opening note | accepted | Prompt instructs host to show the record before `submit_interview_record` and obtain explicit yes; host behaviour not measured (no live Claude tested) |
| [0046](0046-mcp-not-found-and-error-vocabulary.md) | MCP not found and error vocabulary | accepted | `UNKNOWN_FIELD` for extra record fields; structured error responses; no detailed context in error messages |

---

## 10. Web Security and BFF (0047–0051)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0047](0047-nonce-csp-and-style-policy.md) | Nonce CSP and style policy | accepted | Per-request nonce; `script-src 'self' 'nonce-…'` (no unsafe-inline in production); `style-src 'self'` (no unsafe-inline); cross-origin headers added |
| [0048](0048-one-time-secrets-no-store-and-csrf.md) | One-time secrets: no store and CSRF | accepted | Tickets and receipt codes held in page memory only (not storage/cookie); `no-store` cache header; `SameSite=Strict` cookies; same-origin check on state-changing routes |
| [0049](0049-anonymous-receipt-route-and-the-header-contract.md) | Anonymous receipt route and the header contract | accepted | Deletion endpoint is anonymous (no auth); receipt code in `X-Receipt-Code` header (not URL, not query string); rate-limited by IP |
| [0050](0050-two-factor-sign-in-and-account-flows-through-the-bff.md) | Two-factor sign-in and account flows through the BFF | accepted | TOTP or recovery code; challenge in HttpOnly cookie; email verification through BFF; ticket minting requires a live session |
| [0051](0051-message-catalog-accessibility-gate-and-stub-contract.md) | Message catalog, accessibility gate and stub contract | accepted | Stub identity service for testing; accessibility requirements in message catalog; no hardcoded strings (future i18n) |

---

## 11. Signals Module (T10) (0052–0056)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0052](0052-signals-module-boundary-input-port-and-store.md) | Signals module: boundary, input port and store | accepted | Separate `Signals` project; input port (publisher); PostgreSQL store with k-anonymity checks; published aggregates are immutable snapshots |
| [0053](0053-disclosure-control-clean-partitions-and-k-per-cell.md) | Disclosure control: clean partitions and k per cell | accepted | k ≥ K (default 5) per displayed cell; single-band cuts only; clean partitions (every band empty or ≥ K); suppresses small groups entirely; tested exhaustively |
| [0054](0054-statistics-regularised-t-interval-and-reliability.md) | Statistics: regularised t-interval and reliability | accepted | 95% regularised t-interval (not falsely precise at small n); reliability label on each cell; coverage in the same object (counter-metric) |
| [0055](0055-publication-batches-snapshot-and-deletion-semantics.md) | Publication batches: snapshot and deletion semantics | accepted | Snapshot once per day (configurable); snapshot holds only period start; deletions reach aggregates at next batch; API says when data will publish |
| [0056](0056-signals-api-caching-rate-limits-and-demo-data.md) | Signals API: caching, rate limits and demo data | accepted | ETag/If-None-Match on employer and topic lists; 30/min per account rate limit; demo fixture data for testing; list endpoints clamped to 50 items |

---

## 12. CLI Implementation (0057–0061)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0057](0057-cli-submit-and-delete-receipt-commands.md) | CLI submit and delete-receipt commands | accepted | `cli interview <url>` runs the agent; `cli submit <file>` posts a record file; `cli delete-receipt <code>` deletes by receipt code |
| [0058](0058-cli-http-client-hygiene.md) | CLI HTTP client hygiene | accepted | Minimal User-Agent; no extra headers; ticket in header only (not URL); timeout and retry budgets; TLS verification always on |
| [0059](0059-cli-secrets-handling.md) | CLI secrets handling | accepted | API key from environment or user-secrets; never in config file; deleted on logout; cleared from memory when not needed |
| [0060](0060-local-recheck-and-exact-preview.md) | Local recheck and exact preview | accepted | `--local` flag to run without submission; `--preview` to show the record without sending; no server round-trip in preview mode |
| [0061](0061-cli-receipt-handling.md) | CLI receipt handling | accepted | Receipt code shown once at submission; stored in a local registry (user deletable); deletion by `cli delete-receipt` sends header-only request |

---

## 13. Web UI: Signals Pages (0067–0071)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0067](0067-signals-pages-render-the-api-and-derive-nothing.md) | Signals pages: render the API and derive nothing | accepted | Pages render what the API returned; no sorting, ranking or comparison; test asserts absence of affordances for derived views |
| [0068](0068-signals-through-the-bff-caching-validators-and-429.md) | Signals through the BFF: caching, validators and 429 | accepted | BFF proxies to interview-service; ETag validation; `Retry-After` on rate limit; browser shows error message from API |
| [0069](0069-one-answer-for-nothing-to-show-in-the-ui.md) | One answer for nothing-to-show in the UI | accepted | Same page for unknown employer, no data, and suppressed data; page never says which; prevents information leakage via response |
| [0070](0070-linking-the-connect-runbook-from-the-connect-page.md) | Linking the connect runbook from the connect page | accepted | "Connect your AI client" page links to the MCP connection guide; guide covers two URLs, OAuth, secrets and troubleshooting |
| [0071](0071-signals-browser-suite-absence-tests-and-mutation-proof.md) | Signals browser suite: absence tests and mutation proof | accepted | Playwright tests assert no ranking, sorting, comparison or analytics; injected XSS canary proves sanitization; mutations caught by assertions |

## 14. Hardening (0062–0063)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0062](0062-double-barrelled-questions-protocol-1-1-and-guard.md) | Double-barrelled questions: protocol 1.1 and a guard rule | accepted | Protocol 1.1 splits the management and culture questions; `QuestionGuard` rejects double-barrelled questions |
| [0063](0063-pii-detector-rule-cost-and-over-masking.md) | PII detector: rule cost and over-masking | accepted | The obfuscated-email rule is anchored on its marker (linear cost); over-masking after a closing tag and on topic nouns is fixed |

---

---

## 15. Documentation, Build Output, Releasing, Tiles and Interview v2 (0072–0075)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0072](0072-polish-latex-guide-built-on-demand.md) | Polish LaTeX guide to the product, built on demand | accepted | Guide is LaTeX source in `docs/papers/` on the house preamble; PDF is build output from a `workflow_dispatch`-only workflow, never committed; drift from `docs/` accepted |
| [0073](0073-cli-release-workflow-manual-draft-unsigned.md) | CLI release workflow is manual, draft by default, and unsigned | accepted | `release-cli.yml` builds three self-contained binaries plus SHA-256 sums into a draft GitHub Release; no tag until the owner publishes; unsigned and Windows/macOS unrun, stated in the notes |
| [0074](0074-draft-tiles-from-the-record.md) | Draft tiles are generated from the record, locally, never from the transcript | accepted | `exit-interview tiles` turns a validated record into neutral draft texts with the user's own provider; model output is untrusted and passes a deterministic guard; hosted paid variant deferred |
| [0075](0075-polish-responsive-interview-and-platform-tiles.md) | Polish, a responsive interviewer, and platform tiles from the transcript | accepted | Polish protocol and language switch; deterministic serious-account signal starts a bounded deepening phase; Glassdoor, Google and Reddit tiles generated at the end of every interview; amends ADR-0074 on tile input and banned terms |

---

## Legend

- **Accepted:** Adopted as binding architecture or principle
- **Proposed:** Under discussion; not yet adopted
- **Deprecated:** Superseded by a later ADR; see the newer one
- **Status:** All ADRs in this tree are `accepted` unless otherwise marked

---

## Reading Guide

Start with the **Foundation** (0001–0005), then pick your domain:

- **Record design & privacy:** 0006–0011, 0017–0019
- **Submission and ledger:** 0027–0031
- **Authentication:** 0012–0014
- **Interview agent & CLI:** 0022–0026, 0032–0036, 0057–0061
- **Evaluation:** 0037–0041
- **MCP (mode A):** 0042–0046
- **Web security & BFF:** 0047–0051
- **Signals aggregation:** 0052–0056
- **Signals web UI:** 0067–0071
- **Hardening:** 0062–0063
- **Documentation and the guide PDF:** 0017, 0072
- **Releasing the CLI:** 0026, 0073

---

## 16. Web app: interview sessions (0076)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0076](0076-web-interview-sessions-in-memory-service-key.md) | Web interview sessions: in memory, service key | accepted | Sessions live in process memory, bound to the account, one open per account, 30-minute idle and result windows, credits behind two seams, the service key from the environment; no interview text in logs or spans |
| [0078](0078-cost-controls-for-the-hosted-interview.md) | Cost controls for the hosted interview | accepted | Per-account and per-address limits on starts and replies (429, Retry-After), the emergency switch on /health, the verified-email gate (403, fail closed), a global daily start cap (503 to midnight UTC), untagged spend metrics; amends ADR-0014 by keeping a boolean verified flag; production waits on an authservice claim |


Every ADR references its principle (P1–P16, from `PROJECT-BRIEF.md`), related standards guides, and related ADRs. ADRs are immutable once accepted; amendments are noted in later ADRs or in the brief.

## 17. Web app: payments and credits (0077)

| ID | Title | Status | Summary |
|---|---|---|---|
| [0077](0077-credits-ledger-and-payment-provider.md) | Credits ledger and payment provider | accepted | Append-only credit ledger with database-enforced uniqueness per payment event and per session; provider seam with Stripe over HTTP and a fake sharing the production classifier; signature over the raw body; fail-closed configuration; a start takes one credit and a failed session returns it; the Stripe wire format is not yet checked against the provider |
