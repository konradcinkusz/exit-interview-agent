# Owner Follow-ups (T12)

Prepared 2026-10-05 for the owner to action after T12 merges. Each item lists **why** and **exact steps**; no task is ready to ship until it's completed.

## Before any public visibility (release gate task D)

### GitHub repository settings

- [ ] Set repository description (GitHub "About" section) to:
  > Open-source AI agent for structured exit interviews with former employees. Bring your own model: use it via MCP from your own AI client, or run the CLI with your API key or a local model. Privacy-preserving employer signals, verified employment, and an eval harness scoring interview quality from OpenTelemetry traces. .NET Aspire.

- [ ] Set repository topics (GitHub "Topics" section) to:
  `ai-agents`, `llm`, `mcp`, `model-context-protocol`, `opentelemetry`, `llm-evaluation`, `agent-evals`, `employer-reviews`, `exit-interview`, `hr-tech`, `privacy-by-design`, `gdpr`, `dotnet`, `aspire`, `nextjs`, `open-source`

### Dependency audit

- [ ] Run `dotnet list package --vulnerable --include-transitive` on the current `main` branch and verify no high/critical vulnerabilities remain
  - Expected: Nothing, or only the `braces` dev-only package (see SECURITY-REVIEW.md Finding 1)
  - If braces is still present: either wait for Next.js to update it, or update your ESLint major version

- [ ] Run `pnpm audit` in `web/` and verify no high/critical vulnerabilities
  - Expected: Only `braces` or nothing

### Secret audit (run before making public)

**Why:** gitleaks in CI scans refs you push; it does not scan references that exist in GitHub before you push. Making a private repo public exposes all its history to public search.

- [ ] Install gitleaks (if not available): download from https://github.com/gitleaks/gitleaks/releases or `brew install gitleaks`

- [ ] Run the command exactly as it appears in `.github/workflows/secret-scan.yml`:
  ```bash
  docker run --rm -v "$PWD:/repo" -w /repo zricethezav/gitleaks:v8.28.0 \
    detect --source /repo --config /repo/.gitleaks.toml --redact --no-banner
  ```
  - Expected: No findings, or only `[REDACTED]` placeholder findings
  - If failures: investigate each, fix in a new commit, and run again

- [ ] Verify the gitleaks hook runs locally (it is installed by `scripts/setup.sh`):
  ```bash
  git commit --allow-empty -m "test"
  # Should print gitleaks output (likely no findings on an empty commit)
  git reset --soft HEAD~1
  ```

### Licence audit

- [ ] Review `LICENSE` file: verify copyright line reads exactly:
  > Copyright (c) 2026 Konrad Cinkusz

- [ ] Review main `package.json` and `Directory.Packages.props` for any GPL, AGPL or proprietary licences
  - `grep -i "GPL\|AGPL\|proprietary"` over the files
  - Expected: Nothing found

### README claims verification

Run each command in the README's "Try it" and "Run it" sections locally and verify it works as documented:

- [ ] `scripts/setup.sh --check` completes without errors
- [ ] `dotnet run --project src/ExitInterviewAgent.AppHost` starts the system
- [ ] `dotnet run --project src/ExitInterviewAgent.Cli -- personas` lists the 8 personas
- [ ] `dotnet run --project src/ExitInterviewAgent.Cli -- demo --persona talkative --seed 1` produces identical output on two runs
- [ ] One provider test:
  ```bash
  dotnet run --project src/ExitInterviewAgent.Cli -- providers
  # Should print supported providers (no network call)
  ```
- [ ] Web test suite passes:
  ```bash
  cd web && pnpm install --frozen-lockfile && pnpm lint && pnpm typecheck && pnpm test && pnpm build
  # All should pass
  ```
- [ ] e2e test suite passes:
  ```bash
  cd tests/e2e && pnpm install --frozen-lockfile && npx playwright install --with-deps chromium && pnpm test
  # All should pass against the built web app from the previous step
  ```

### Workflow lint and setup

- [ ] Verify `.github/workflows/ci.yml` syntax (GitHub Actions can lint this for you; alternatively: download `act` and run `act -l`)

- [ ] Confirm `.github/workflows/flyio.yml` has the comment at the top saying it is NEVER TRIGGERED in this phase

### Image pinning (authservice)

**Why:** `ghcr.io/konradcinkusz/authservice:v0.3.4` is a mutable tag. When the repo is public and you later deploy, the tag could be updated by the authservice project, changing behaviour silently.

Before any deployment (Task G, not yet):
- [ ] Pin authservice by digest instead of tag. 
  - Current location: `src/ExitInterviewAgent.AppHost/Program.cs` lines 39-40 (const `AuthserviceImage` and `AuthserviceTag`)
  - Steps:
    1. Pull the current tag: `docker pull ghcr.io/konradcinkusz/authservice:v0.3.4`
    2. Inspect it: `docker inspect ghcr.io/konradcinkusz/authservice:v0.3.4 --format='{{index .RepoDigests 0}}'`
    3. Update `Program.cs` line 39 to use the full digest: `const string AuthserviceImage = "ghcr.io/konradcinkusz/authservice@sha256:abc123..."`; or update line 40 to remove the tag (it will be in the digest)
    4. Test: `dotnet run --project src/ExitInterviewAgent.AppHost` should still start authservice
    5. Commit this change before any deployment

### CodeQL configuration (for public repo)

**Why:** CI keeps CodeQL results as artifacts (not uploaded to GitHub code scanning) while the repo is private. When you make it public, set the flag to upload them.

- [ ] In `.github/workflows/codeql.yml`, find the line with `CODEQL_UPLOAD` and set it to `true`
  - This enables results to show in GitHub's code scanning tab
  - Requires GHAS to be enabled (default for public repos under organizations)

### Documentation: verify all links resolve

- [ ] Run: `python3 scripts/check-doc-links.py`
  - Expected: Exit code 0, no output
  - If failures: fix relative links in markdown files

### Documentation: verify badges are real

**Why:** README badges query public GitHub API; they show empty for private repos. Badges for things that don't exist (e.g., a "deploy" badge with no Fly app) should be removed.

- [ ] Review `README.md` badges (top of file):
  - `ci` badge: queries workflows; points to the latest `.github/workflows/ci.yml`; real, keep it
  - `secret-scan` badge: real, keep it
  - `License` badge: real, keep it
  - Any others: verify they point to things that exist (e.g., a "Deploy" badge only if you have a Fly app; a "Version" badge only if you've pushed tags)

## Before going live with interviews (legal and ops)

These are blocking for mode B (CLI with your own model) and especially mode C (web portal with users).

### Legal counsel review

**Why:** The README and the codebase make privacy claims. A lawyer should verify they are sound and compliant with your jurisdiction and the model provider's terms.

- [ ] Have a lawyer read and approve:
  - `README.md` "Privacy at a glance" section
  - `docs/privacy/DESIGN.md` (the full privacy design)
  - `docs/security/THREAT-MODEL.md` (especially residual risks §5)
  - The opening text in MCP mode (T8, ADR-0045): "The full conversation is not stored on our servers, but your AI host's terms and retention apply."
  - Any privacy notice or policy you post on the web portal
  - The record-retention policy (how long records are kept; default is 24 months, configurable)

- [ ] If you offer real interviews to real people:
  - Lawful basis for processing (consent, contract, legal obligation, etc.)
  - GDPR impact assessment for employment-related processing
  - Data retention and deletion rights procedures
  - Third-country transfers if model provider is outside EU

### Deployment architecture decision

**Why:** OP-15 (rate-limit key sharing) and single-instance assumptions in the code need an ops decision.

- [ ] Decide on rate-limiting for anonymous endpoints:
  - Option A (current, development): Accept that all portal users share one rate-limit budget (privacy cost, documented in OP-15)
  - Option B: Forward a trusted `X-Forwarded-For` header (or other IP header) from the BFF to the backend, and configure `Submission:ClientIpHeader` to read it
  - Document this in an operations runbook

- [ ] Decide on deployment topology:
  - Single instance (current assumptions apply: in-memory rate limiters, one publisher, one process lock for signals)
  - Multi-instance behind a load balancer: requires a task to move rate limiters and the signals publisher to a durable, shared store (Redis or similar)
  - Document this before deploying

### Signals module deployment

**Why:** The signals publisher runs once per process and is not horizontally scalable. Small clusters risk orphaned rows and silent failures.

- [ ] Audit `docs/architecture/signals.md` and `docs/privacy/AGGREGATION.md` for single-instance assumptions

- [ ] If deploying to multiple instances, either:
  - (Simple) Run signals publisher on one dedicated instance only
  - (Complex) Move the publisher to a message queue with single-flight semantics and a lock/lease backend (Postgres advisory lock, Redis, etc.)

## Before the first submission from a real person

### Accessibility pass

**Why:** The automated suite (axe-core) catches WCAG 2.0/2.1 A and AA violations; it cannot catch reading order, focus, or screen-reader UX.

(OP-16, not yet done)

- [ ] Have someone with a screen reader (NVDA, JAWS, or VoiceOver) walk through the flows:
  - Sign-up, email verification
  - Sign-in and two-factor
  - Consent review
  - Ticket mint and display
  - Record deletion by receipt
  - Account deletion
  - Signals list and detail

- [ ] Test keyboard-only navigation on all flows

- [ ] Test at 200% zoom and in forced-colours mode (Windows high contrast)

- [ ] Document any issues found and fixes applied

- [ ] Commit the accessibility pass sign-off (date, tester name/handle, flows tested) to the repository

### Two-factor sign-in end-to-end test

**Why:** T9 tests the web BFF against a contract-mirroring stub of authservice, not the real image. Real behaviour may differ.

(OP-17, not yet done)

- [ ] Start the real `ghcr.io/konradcinkusz/authservice:v0.3.4` container locally (environment variables from src/ExitInterviewAgent.AppHost/Program.cs:58-69):
  ```bash
  # Note: this is a minimal example for manual testing. For full setup (Postgres, MCP OAuth), see scripts/README.md
  docker run -p 5100:8080 \
    -e "DATABASE_PROVIDER=PostgreSQL" \
    -e "Jwt__PrivateKeyPem=<base64-encoded-rsa-private-key>" \
    -e "Jwt__Issuer=ExitInterviewAgent" \
    -e "Jwt__Audience=ExitInterviewAgent" \
    -e "ConnectionStrings__DefaultConnection=<postgres-connection-string>" \
    ghcr.io/konradcinkusz/authservice:v0.3.4
  ```
  - For a full integration test with Postgres, see `scripts/README.md` (authservice is normally started via the Aspire AppHost which provides all dependencies)

- [ ] Configure the AppHost to use it: set `Identity__Enabled=true` and the authservice connection

- [ ] Walk through:
  - Sign-up with email verification
  - Sign-in with password
  - Enable TOTP 2FA (authenticator app)
  - Sign-out and sign-in again, verify the 2FA challenge
  - Verify that wrong TOTP codes are rejected
  - Verify that recovery codes work after TOTP setup
  - Test password reset

- [ ] Commit the test results (date, platform, any issues found)

### MCP host test

**Why:** No real Claude client has ever connected to the server. Prompt discoverability, host adherence to instructions, and the stated privacy model need verification.

(OP-18, not yet done; requires two public HTTPS URLs and the real Claude)

- [ ] Set up two public HTTPS URLs (with TLS certificates):
  - `https://interview-service.example/` → your interview-service instance
  - `https://claude-redirect.example/` → a tunnel or reverse proxy for the OAuth callback

- [ ] Register the MCP client in authservice (`AuthorizationServer:Clients` section) with:
  - Client ID and secret (32+ bytes)
  - Redirect URI: `https://claude-redirect.example/callback` or equivalent
  - Scope: `interview:submit`

- [ ] In Claude (the real app, not a test), add your MCP server with these URLs

- [ ] Conduct a real interview via the MCP path (use a fictional employer and invented answers):
  - Verify the prompt and resource appear
  - Verify the interview completes
  - Verify the record is submitted and receipt code is shown
  - Verify you can delete the record with the receipt code via the CLI or web panel

- [ ] Document:
  - Date and time of the test
  - Claude version (app, desktop, web)
  - Model used
  - Any issues or unexpected behaviour
  - That you verified: (a) the prompt's opening text was shown, (b) the receipt code was displayed once, (c) the resource list was fetched, (d) no other connector received the transcript

### CLI submission over real TLS (OP-25)

**Why:** All CLI tests use loopback HTTP or in-process calls. Real TLS behaviour (certificate validation, redirects, timeouts) is not tested.

- [ ] Create a self-signed certificate (or use a test certificate from Let's Encrypt):
  ```bash
  openssl req -x509 -newkey rsa:2048 -keyout key.pem -out cert.pem -days 1 -nodes -subj "/CN=localhost"
  ```

- [ ] Run your interview-service with TLS (or behind a reverse proxy that terminates TLS)

- [ ] Run the CLI submission against it:
  ```bash
  dotnet run --project src/ExitInterviewAgent.Cli -- submit --record ./record.json --server https://localhost:5443
  # Or your real URL
  ```

- [ ] Verify:
  - Certificate validation fails on untrusted cert (expected)
  - Second run with `--yes` continues (to accept the certificate permanently, or accept once)
  - Submission succeeds
  - Receipt code is displayed

- [ ] Repeat on Windows and macOS (or arrange for someone else to test on those platforms)

## What you still need to decide

### High-priority decisions

| Item | Options | Blocker for |
|---|---|---|
| **Braces CVE (dev package)** | GHSA-vfj7-8cjw-p6xm: dev-only, no patch (verified via `pnpm audit --prod`); await Next.js update or accept risk | Public visibility claim; see SECURITY-REVIEW.md Finding 1 |
| **Employment verification** (OP-1) | Implement a verifier (email challenge, 3rd-party attestation, etc.); stay "claimed by accounts" | First real submission; legal review |
| **Employer registry** (OP-2) | Free-text employer field (current); curated list with merge policy | Production aggregates; risk of ID issues at K = 5 |
| **Rate-limit key sharing** (OP-15) | Forward client IP from BFF (ops decision); accept shared budget | Deployment behind a proxy |
| **Signals module deployment** (T18 residual) | Single instance (current); move to durable store | Multi-instance deployment |

### Lower-priority (doesn't block release)

| Item | Notes |
|---|---|
| Public-client support in authservice (OP-7) | CLI would get direct OAuth; removes ticket redemption correlation; requires authservice ADR and CLI refactor |
| Other MCP hosts (OP-9) | Each needs client registration in authservice and ops runbook; no change needed in code |
| Multilingual interviews (OP-6) | Polish personas and PII rules; deferred to later phase |
| Judge calibration (OP-11) | Human labels needed; ≥40 samples, κ ≥ 0.6; starts the T7 proper measurement |
| Manual accessibility pass (OP-16) | Screen reader test; document as "not yet done" if going live |

## Checklist: ready to make public

- [ ] All high/critical vulnerabilities resolved or accepted (braces documented)
- [ ] Gitleaks scan passes over full history
- [ ] Licence audit complete (all permissive)
- [ ] README commands tested and documented as working
- [ ] GitHub repository description, topics and links set
- [ ] All doc links resolve (`check-doc-links.py` passes)
- [ ] Badges are real (ci, secret-scan, licence; remove fake ones)
- [ ] Authservice pinned by digest (if and when you deploy)
- [ ] CodeQL results will upload (CODEQL_UPLOAD if public)

## Checklist: ready for first real interview

- [ ] Lawyer has reviewed privacy claims and threat model residuals
- [ ] Employment verification decision made (mock → real, or stay claimed)
- [ ] Deployment topology decided (single-instance or multi-instance)
- [ ] OP-15 rate-limiting decision made (forward header or accept shared budget)
- [ ] Two-factor sign-in tested against real authservice image
- [ ] MCP host (Claude) tested end-to-end with real interview
- [ ] CLI submission tested over real TLS, on Windows/macOS
- [ ] Accessibility pass complete (or documented as "deferred, known in OP-16")
- [ ] Retention and deletion procedures implemented and tested
- [ ] Data breach response plan drafted (if going live)

## Checklist: ready to deploy

(Not in scope of T12; only if you decide to deploy)

- [ ] All above checklists complete
- [ ] First aid on-call schedule (or equivalent)
- [ ] Observability set up (metrics, logs, tracing to somewhere durable)
- [ ] Backup and recovery tested
- [ ] Load test: verify single-instance assumptions or upgrade to multi-instance
- [ ] Disaster recovery tested
- [ ] Rate-limit tuning from production data (if possible with test traffic)

## Cleanup and upstream work

### Session branches (can be deleted after merge to main)

The following remote branches were created by build sessions T0-T12 and can be deleted once their PRs are merged to main. Deletion must be done via GitHub UI or `gh cli` (local session cannot delete remote branches):

```
origin/claude/project-brief
origin/claude/t0-scaffold
origin/claude/t1-record-schema
origin/claude/t2-auth-wiring
origin/claude/t3-docs-foundation
origin/claude/t3b-brief-amendments
origin/claude/t4-interview-agent
origin/claude/t5-ingest
origin/claude/t6-providers
origin/claude/t7-eval-harness
origin/claude/t8-mcp-adapter
origin/claude/t9-web-panel
origin/claude/t10-signals
origin/claude/t10b-signals-web
origin/claude/t11-cli-submit
origin/claude/t12-question-guard
origin/claude/t12-security-review
```

- [ ] After T12 PRs (question-guard and security-review) merge to main, delete all session branches via GitHub UI

### Upstream proposals for authservice

The following are proposals that would reduce operational burden and are out of scope for exit-interview-agent but belong in `konradcinkusz/authservice`:

- [ ] **Public-client support** (ADR, code change in authservice): allow CLIs to use OAuth without a client secret. Currently exits-interview-agent works around this with submission tickets. A public client in authservice with PKCE would simplify mode B. ADR link: https://github.com/konradcinkusz/authservice/blob/main/docs/adr/0005-confidential-client-mandate.md

- [ ] **Device grant flow** (RFC 8628): mode B (CLI) would benefit; currently uses tickets as a workaround. Consider as an alternative to public clients. Would need authservice changes.

- [ ] **Email verification templates customization**: currently authservice sends hardcoded emails. For this repo, templates should be customizable or externalized so the owner can white-label verification flow.

---

**Generated:** 2026-10-05 by T12 security review and hardening session.

For questions: read the referenced ADRs, threat model, and OPEN-PROBLEMS. This file is the checklist; the linked documents are the "why" and the "how".
