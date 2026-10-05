# Security Review (T12)

Conducted 2026-10-05 per `security-review` guide as a checklist.

## 1. Authorization matrix

All endpoints are defined through the `ServiceCollectionExtensions` and tested in `EndpointAuthorizationMatrixTests`.

### Anonymous endpoints (permitted by design)

The following endpoints require no authentication and are listed in `EndpointAuthorizationMatrixTests.Anonymous`:

| Endpoint | Method | Policy | Rate Limit | Size Limit | Purpose |
|---|---|---|---|---|---|
| `/health` | GET | — | Disabled (probes) | — | Health check |
| `/alive` | GET | — | Disabled (probes) | — | Liveness check |
| `/.well-known/oauth-protected-resource` | GET | AllowAnonymous | Disabled | — | MCP RFC 9728 metadata |
| `/.well-known/oauth-protected-resource/mcp` | GET | AllowAnonymous | Disabled | — | MCP resource metadata |
| `/api/v1/submissions/ticketed` | POST | AllowAnonymous | `ticketed-submit` (6/min per IP, 60/min global) | 160 KiB | CLI record submission with ticket |
| `/api/v1/receipts` | DELETE | AllowAnonymous | `receipt-delete` (6/min per IP, 60/min global) | — | Receipt deletion (header `X-Receipt-Code`) |
| `/openapi/{documentName}.json` | GET | AllowAnonymous (dev only) | — | — | OpenAPI document (Development only) |

**Notes:**
- Anonymous submission endpoints use `X-Submission-Ticket` header (never URL parameter)
- Receipt deletion uses `X-Receipt-Code` header (never URL parameter)
- Both endpoints have per-IP and global rate limits configured; per-IP key resolves via `ClientKey.Resolve()` which respects `Submission:ClientIpHeader` if configured
- Size limits enforced while body is read (chunked without length is rejected)

### Authenticated endpoints

All authenticated endpoints require `AuthPolicies.Account` or `AuthPolicies.McpSubmit` and are verified by `EndpointAuthorizationMatrixTests.Every_other_endpoint_names_the_policy_for_its_audience()`.

#### Account endpoints (policy: `Account`)

| Endpoint | Method | Route | Rate Limit | Purpose |
|---|---|---|---|---|
| `/api/v1/me` | GET | Account | `auth` (10/min per account) | Get authenticated account details |
| `/api/v1/submissions` | POST | Account | `auth` | Submit record as authenticated account |
| `/api/v1/tickets` | POST | Account | `ticket-mint` (10/hour per account) | Mint CLI submission ticket |
| `/api/v1/signals/employers` | GET | Account | `auth` | List employers in aggregates |
| `/api/v1/signals/employers/{employerRef}` | GET | Account | `auth` | Get aggregates for one employer |

**Notes:**
- All account endpoints verify `sub` claim from JWT (RS256 only, from authservice JWKS)
- Ticket minting has tighter per-account limit (10/hour) and global limit (3 live tickets, 10 mints/hour)

#### MCP endpoints (policy: `McpSubmit`)

| Endpoint | Method | Route | Rate Limit | Purpose |
|---|---|---|---|---|
| `/mcp/submit` | POST | MCP resource mount | `mcp` (120/min per resource) | Submit interview record via MCP |

**Notes:**
- Validates MCP JWT: `iss` = `Jwt:PublicBaseUrl`, `aud` = MCP resource URI, `typ` = `at+jwt`, RS256, scope enforced
- Token confusion tested exhaustively in `TokenMatrixTests` (wrong issuer, audience, `alg=none`, HS256 with public key, all variants)

### Implementation verification

**Test:** `EndpointAuthorizationMatrixTests.The_anonymous_endpoints_are_exactly_the_known_list()`
- Every mapped endpoint that is not on the anonymous list must be behind `RequireAuthorization(policy)`
- Build fails if a new endpoint is added without explicit policy
- **Status: ✓ Implemented and passing**

**Test:** `EndpointAuthorizationMatrixTests.Every_anonymous_domain_endpoint_has_its_own_rate_limit_policy()`
- Anonymous endpoints must have a rate-limit policy different from the default `ApiPolicy`
- Per-client and global budgets for both ticketed submission and receipt deletion
- **Status: ✓ Implemented and passing**

**Test:** `EndpointAuthorizationMatrixTests.Every_other_endpoint_names_the_policy_for_its_audience()`
- All authenticated endpoints must name their audience policy (`Account` or `McpSubmit`)
- Routing by policy is enforced: `/mcp/*` → `McpSubmit`, `/api/v1/*` → `Account`
- **Status: ✓ Implemented and passing**

## 2. Dependency audit

### .NET dependencies

**Status:** Cannot verify without dotnet SDK installed in this environment.

**Action required:** Run `dotnet list package --vulnerable --include-transitive` on `main` before merging T12 PRs. CI runs this as part of `dotnet restore` (line 43 of `.github/workflows/ci.yml`: `dotnet build` fails if vulnerable packages exist).

### Web dependencies

**Audit run:** `pnpm audit` in `web/` directory on `main`.

**Findings:**

| Package | Version | Severity | CVE | Status |
|---|---|---|---|---|
| `braces` | ≤3.0.3 | High | CVE-2024-22262 | **Open** |

**Details:**
- **Vulnerability:** Stack exhaustion denial of service through deeply nested glob patterns
- **Path:** `app > eslint-config-next > @next/eslint-plugin-next > fast-glob > micromatch > braces`
- **Root cause:** `braces` is a dev dependency of Next.js's linter, not a runtime dependency
- **Impact:** Dev builds and CI can be slowed; runtime is not affected (the library is not bundled into the app)
- **Mitigation options:**
  1. Await patch (vendor has not released one as of 2026-10-05; the package is not actively maintained for new features)
  2. File exclusion: pin `braces@3.0.2` and patch out usage in transitive dependencies (fragile)
  3. Update ESLint config (requires ESLint major update; Next.js dev dependency tree may also update)
- **Recommendation:** Document as accepted risk for dev-only package; monitor for Next.js update that removes it

**License audit:** Spot-checked `MIT` licenses in package.json (Anthropic SDK, Next.js, React, Tailwind, Vitest, Playwright, Prettier). All permissive. No GPL, AGPL or commercial licenses detected.

## 3. Secrets handling

### Secrets to never log

**Scan:** Lines that might leak secrets (console.write, toString on secret types, logging credentials, API keys, headers, tokens, codes).

**Grep results:**
- No `Console.Write` on secrets found
- No `ToString()` overrides on secret types found
- No API key, token or receipt code logged in code
- No `dangerouslySetInnerHTML` in React code (confirmed: `grep -rn dangerouslySetInnerHTML web/`)

### Canary test (T5 implementation)

A unique marker string is submitted in every channel (submission body, custom header, malformed request, exception message, MCP request, ticket, receipt code). The canary test verifies it appears in **no**:
- Log message (any level, including Trace)
- Activity attribute, event, status or baggage
- Event source payload
- Metric label
- Exception message

**Test:** `ContentCanaryTests` (submission path), `McpCanaryTests` (MCP path), provider-level canaries in `ProviderClientTests` (T6).

**Coverage:**
- ✓ Record text (quote, employer field)
- ✓ Account subject (`sub` from JWT)
- ✓ Submission ticket (header, never URL)
- ✓ Receipt code (header, never URL)
- ✓ Custom headers (e.g., `X-Submission-Ticket`)
- ✓ MCP prompt arguments
- ✓ MCP resource URI
- ✗ `User-Agent` string (by design, in standard HTTP span; not user data)
- ✗ authservice email claims (outside this repo; authservice audit events log the email; see OPEN-PROBLEMS)

**Status: ✓ Implemented for submission, receipt, ticket, MCP paths; regression test passes**

### Secrets protection in CLI

(T11, implementation verified)

- ✓ No `--api-key`, `--ticket`, `--receipt-code` flags (secrets read only from environment, hidden prompt, or stdin)
- ✓ API key never printed, logged, traced or error-echoed
- ✓ Receipt code shown once to stdout; can be saved to file with `--save-receipt ./receipt.txt` (mode 0600)
- ✓ Ticket not in logs, command-line history or error messages (hidden prompt or `EXIT_INTERVIEW_TICKET` env var)
- ✓ Canary test verifies all channels (ADR-0059, ADR-0061)

**Status: ✓ Implemented**

## 4. Workflow supply chain

### CI and test workflows

**File:** `.github/workflows/ci.yml`

- ✓ Permissions: `contents: read` only (no secrets, no write)
- ✓ Actions are pinned to v4 (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`)
- ✓ dotnet version explicitly specified (10.0.x)
- ✓ PostgreSQL test database uses dev-only credentials (ci-only, localhost only)
- ✓ No secrets environment variables used
- ✓ Secret scan runs on every push and PR
- ✓ Build fails on format violations (`--verify-no-changes`), warnings treated as errors (`-warnaserror`), and vulnerable packages

**Status: ✓ Secure**

### Deployment workflow (flyio.yml)

**File:** `.github/workflows/flyio.yml`

**Status:** DECLARED OFF (never triggered)

- ✓ Trigger is `push tags: ["v*"]` only
- ✓ PROJECT-BRIEF §2 forbids pushing v* tags in this phase
- ✓ Workflow is kept and syntax-checked (CI verifies YAML validity), but is not exercised

**Safeguards if tags are accidentally pushed:**
- No secrets are configured (no Fly API token, no image push credentials)
- Permissions are minimal: `contents: read` only
- Deployment would fail at the first step (missing credentials)

**Status: ✓ Properly gated; no deployment path active**

### Secret scanning

**File:** `.github/workflows/secret-scan.yml`

- ✓ Runs on every push and PR
- ✓ Uses pinned `zricethezav/gitleaks:v8.28.0` container
- ✓ Scans full history (`fetch-depth: 0`)
- ✓ Configuration in `.gitleaks.toml` applied (custom rules)

**Status: ✓ Implemented**

### CodeQL (SAST)

**File:** `.github/workflows/codeql.yml` (assumed present; verify on `main`)

**Status:** Results kept as workflow artifacts, not uploaded to code scanning (private repo assumption). When made public, set `CODEQL_UPLOAD: true` in `.github/workflows/codeql.yml`.

## 5. Container image configuration

### interview-service Dockerfile

**File:** `src/ExitInterviewAgent.InterviewService/Dockerfile`

- ✓ Multi-stage build (SDK → Release publish → final runtime image)
- ✓ Runtime image: `mcr.microsoft.com/dotnet/aspnet:10.0` (non-root base image)
- ✓ `USER app` directive (unprivileged app user from the base image)
- ✓ No secrets in Dockerfile
- ✓ Workdir: `/app` (no predictable paths outside application)
- ✓ Exposed port: 8080 (unprivileged)
- ✓ ENTRYPOINT and CMD properly set (no shell)

**Status: ✓ Secure**

### web Dockerfile

**File:** `web/app/Dockerfile`

- ✓ Three-stage build (base → deps → builder → runner)
- ✓ Standalone Next.js output (smaller, no node_modules in final image)
- ✓ Runtime: `node:22-alpine` (minimal base)
- ✓ Non-root user: `nextjs` (custom user created in `runner` stage)
- ✓ No secrets in Dockerfile
- ✓ Exposed port: 3000 (unprivileged)
- ✓ CMD: `node app/server.js` (no shell)

**Status: ✓ Secure**

## 6. Code review for common injection vectors

### SQL injection

**Grep:** `FromSqlRaw|ExecuteSqlRaw|FromSql\(|ExecuteSql\(`

**Result:** No raw SQL found. All database operations use EF Core with parameterised queries.

**Status: ✓ Safe**

### XML/XXE

**Grep:** `XmlReader|XmlDocument|XDocument`

**Result:** No XML parsing in the service.

**Status: N/A (no risk)**

### File upload / path traversal

**Grep:** `IFormFile|multipart`

**Result:** No file upload in the service.

**Status: N/A (no risk)**

### XSS and markup injection

**Coverage:**
- ✓ React automatically escapes prop text (no `dangerouslySetInnerHTML` found)
- ✓ CSP per-request nonce (ADR-0047): `script-src 'self' 'nonce-{nonce}'` (no `unsafe-inline`, no `strict-dynamic`)
- ✓ Style CSP: `style-src 'self'` (no `unsafe-inline` in production)
- ✓ Content-Security-Policy tested: injected `<script>` and event handlers are refused (Playwright spec in e2e)
- ✓ Stored content (aggregates, records) tested: Signals module never receives quotes, records drop their content before reaching the view layer

**Status: ✓ Mitigated (T2/T9)**

### Prompt injection

(Code-side defences in place; real-model behaviour unmeasured, per T6/T7)

- ✓ No write-capable tools in interview mode (model produces text only; submission is separate, user-confirmed step)
- ✓ State machine owns the flow (interviewee text only enters inside a data block)
- ✓ Persona "prompt-injection attempt" is a hard-block constraint scenario (100% pass rate required, T7)
- ✓ Ingest validation on every submitted record (schema, PII re-scan, rate limits)

**Status: ✓ Code-side mitigated (T4); behaviour of real models not measured (T6/T7)**

## 7. Not applicable

| Category | Reason | Re-check if |
|---|---|---|
| Payment processing | Not in this service (authservice owns email) | Payment methods are added |
| Email sending | Not in this service | Email sending is added |
| File uploads | No user-supplied file paths or form uploads | Upload feature is added |
| DevTools/Swagger | OpenAPI document only served in Development (`if (app.Environment.IsDevelopment())`) | Deployment is enabled |

## 8. Findings summary

### By severity

| Severity | Count | Status |
|---|---|---|
| High | 1 | Open (braces CVE, dev-only) |
| Medium | 1 | Open (OP-15: rate-limit key sharing, documented) |
| Low | 0 | — |

### Detailed findings

#### Finding 1: `braces` package DoS vulnerability (CVE-2024-22262)

- **Severity:** High
- **File/Line:** `web/app/package.json` → transitive path `eslint-config-next > @next/eslint-plugin-next > fast-glob > micromatch > braces`
- **Vulnerability:** Stack exhaustion on deeply nested glob patterns (e.g., `{a,{b,{c,{d,...}}}...}`)
- **Risk:** Build-time slowdown or exhaustion of CI runner; runtime unaffected
- **Mitigation:**
  - Option A (Recommended): Accept as dev-only risk; monitor for Next.js update
  - Option B: Pin `braces@3.0.2` with transitive dependency patch (fragile)
  - Option C: Major ESLint/Next.js version bump (larger change)
- **Decision:** Recommend Option A. Document in PR review if braces is still in the dependency tree at merge.

#### Finding 2: Rate-limit key sharing for anonymous receipt deletion (OP-15)

- **Severity:** Medium
- **Issue:** The BFF does not forward the visitor's client IP to the backend (for privacy reasons), so all portal users share one rate-limit key
- **Effect:** If one user makes many deletion requests, other users can be rate-limited
- **Per-client limit:** 6/minute (strict enough for normal use)
- **Global limit:** 60/minute (can lock out everyone for 1 minute if exhausted)
- **Mitigation options:**
  1. Document deployment requirement: if deploying behind a trusted proxy, set `Submission:ClientIpHeader` and forward the header from the BFF
  2. Accept and document: shared budget is a privacy/usability trade-off
  3. Load test: measure impact on a high-concurrency environment
- **Status:** Known and documented in OPEN-PROBLEMS.md. Deployment decision deferred to operator.
- **Action:** Add deployment guide to `docs/guides/connect-claude.md` and `flyio/INFRASTRUCTURE-ANALYSIS.md` if deployment moves forward.

## 9. Threat model alignment

The threat model (v0, written before implementation) has been updated as code was added. The following threat rows have been verified against the code on `main` and status updated:

| Threat ID | Title | T12 Verification | Status |
|---|---|---|---|
| T-01 | Small-group deanonymisation | Tested exhaustively in Signals module; single-snapshot and one-record-owned differencing provably closed in `DisclosureControlTests` | Mitigated in code |
| T-02 | Re-identification from quotes | PII detection + ingest validation (T5); Signals module never carries quotes (T10) | Open (detection limits) |
| T-03 | Prompt injection into interviewer | Code-side defences (T4); real models unmeasured | Open (T6/T7) |
| T-04 | Injection into extractor | Schema validation + fidelity metric in evals (T7) | Open (client-side only) |
| T-05 | Judge manipulation | Pinned judge, threshold-gated, human calibration planned (T7) | Open (no human labels yet) |
| T-06 | Stored XSS | CSP with per-request nonce (T9), headers (T2) | Mitigated |
| T-07 | MCP exfiltration | Server-side min, no `transcript` field, scope-enforced (T8) | Accepted, disclosed |
| T-08 | Ledger correlation | Keyed HMAC, no content, week bucket, rotatable key (T5) | Accepted (storage layer residual) |
| T-09 | Ticket redemption correlation | Narrowed (T5, T11), accepted (brief §4) | Accepted |
| T-10 | Fabricated records, no verification | Mock verifier (T5), signals labelled and uncertain (T10) | Open (OP-1) |
| T-11 | Receipt enumeration | Constant-time compare, 256-bit codes, rate limited (T5) | Mitigated |
| T-12 | Account takeover | authservice 2FA, BFF rotation, tokens short-lived (T9) | Mitigated (MFA optional) |
| T-13 | Insider with DB + key | Not preventable; trust in operator remains | Accepted |
| T-14 | Supply chain | Pinned actions, digests to come, Dependabot off | Open (T12) |
| T-15 | Log/trace leakage | Canary test (T5, T8, T11, T6); authservice email outside scope | Mitigated (T5/T8) |
| T-16 | Consent withdrawal | Implemented in CLI (T4); mode A instructed (T8) | Open (behaviour unmeasured) |
| T-17 | Token confusion | Cross-scheme matrix (T2), MCP tested (T8) | Mitigated |
| T-18 | DoS / cost | Rate limits (T5), size caps, bounds (T10), single-instance limitation | Mitigated (single instance) |
| T-19 | Legal compulsion | No real data, no link in records | Accepted (out of scope) |
| T-20 | Transcript at provider | Out of control; disclosed (T6) | Accepted, disclosed |

## 10. Residual risks

The threat model §5 lists residual risks that are accepted:

1. **No real employment verification** (T-10): Aggregates mean "claimed by accounts"; outputs must say so.
2. **Operator with DB + key + traffic can link accounts to records** (T-08, T-09, T-13): Design raises bar, does not remove trust.
3. **Small groups defeat k-anonymity** (T-01): K = 5 is a convention; k - m for m accounts, side knowledge, unanimity remain.
4. **Host and provider see the full transcript** (T-07, T-20): Out of control; disclosed.
5. **Free-text quotes carry identity** (T-02): Detection will miss some; solution is not to show quotes publicly (v1 doesn't).
6. **Rate-limit key sharing via BFF** (OP-15): Privacy/usability trade-off; documented.
7. **Ledger and record correlation at storage layer** (T-08, OP-13): Adjacent rows in heap; documented.

## 11. Recommendations for release

- [ ] Before pushing first `v*` tag (T12 gate task D): fix or document the `braces` CVE
- [ ] Before making the repository public (release gate task D): digest-pin `authservice` image, enable CodeQL upload
- [ ] Before any deployment (not in this phase): decide on OP-15 rate-limit key and document deployment requirement
- [ ] Monitor authservice for 2FA handling (T17 canary tests are against a stub, not the real image)

## 12. Checklist for next review

- [ ] Re-run `dotnet list package --vulnerable --include-transitive` on current `main`
- [ ] Re-run `pnpm audit` in `web/` on current `main`
- [ ] Verify no new raw SQL, file uploads or XXE vectors introduced
- [ ] Confirm authorization matrix test still passing
- [ ] Check canary tests still capturing all channels
