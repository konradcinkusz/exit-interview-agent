# Release Gate

**Status:** GATE FAIL (blockers present)

**Updated:** 2026-10-05

---

## Gate Failures

### Model Identifiers in Git History

**Severity:** GATE FAIL – blocks public release  
**Finding:** Model identifiers (Sonnet 5.5, Haiku 4.5) present in commit trailers on main branch.

**Exact Commits Affected:**

| Commit Hash | Model | Subject |
|---|---|---|
| `21bd4fcbf52904db59d5bf11b58fa44cf90febd6` | Claude Haiku 4.5 | T12b: Correct defects in security review and owner follow-ups |
| `bf04edb1baa6e6fe3b35c530432fec8753421076` | Claude Sonnet 5.5 | T11: CLI submit and delete-receipt: ticketed submission, receipt handling (#13) |
| `a20090ef9df61580c74431c738302f30c29444c0` | Claude Sonnet 5.5 | T10: signals module: employer aggregates with uncertainty and disclosure control (#12) |
| `43efc1094072645c2946b1f6c9638b754b40f8eb` | Claude Sonnet 5.5 | T8: MCP server for mode A (official SDK, Streamable HTTP, stateless) (#11) |
| `ca16611a5aa395ba0589b3b2b5e03af17cf03a18` | Claude Sonnet 5.5 | T6: model providers (Anthropic key, OpenAI-compatible, Ollama), interactive interview CLI, provider telemetry (#8) |
| `4b4d6fa1fcde09dc3c3b05cc47238dbecc58e6e0` | Claude Sonnet 5.5 | T9: web panel: pages, two-factor sign-in, tickets, receipt deletion, nonce CSP, accessibility gate (#10) |
| `c40e65935b76c27db75bdd15d5f6c0b646fbeebc` | Claude Sonnet 5.5 | T7: evaluation harness: spec, scenarios as data, Layer 1/2, baseline gate, conformance report (#9) |
| `e6b60f4bdcb3f86b33585c67e23b20bc1313c06a` | Claude Sonnet 5.5 | T5: server-side submission: ledger, receipts, tickets, retention (#7) |
| `9bcb72a6758926aa4b0545c20a190b7c6ee6bf75` | Claude Sonnet 5.5 | T4: interview agent core, persona simulator and offline CLI demo (#6) |
| `360f2b64ba08ed5ddf9f6cf5daa67e12bb6b8803` | Claude Sonnet 5.5 | T2: two JWT schemes (web + MCP), RFC 9728 metadata, BFF refresh rotation, consent step, account-deletion semantics (#5) |
| `2a4f9bd89600cd571f276550a9805b43b7376cc5` | Claude Sonnet 5.5 | T1: record schema v1, validation library and PII detector (#4) |
| `2a20a1f2ca537e686029d0be9b966b31f6ea1c4d` | Claude Sonnet 5.5 | T3: documentation foundation (privacy design, threat model, legal considerations, open problems, eval methodology) (#2) |
| `65ad0e4cd81565428543f6f126a4c4316b92873e` | Claude Sonnet 5.5 | T0: scaffold the estate's default containerized application (#1) |

**Policy Violation:** [AGENTS.md](../../AGENTS.md) § "Rules that bite" states: "No secrets, no real personal data, **no model identifiers** in commits, code comments or docs."

**Root Cause:** Commits were made with `Co-Authored-By: Claude <model-name>` trailers in violation of the no-model-identifiers rule.

---

## Options for Owner

### Option A: Leave As-Is
**Cost:** None  
**Effect:** Repository remains public with model identifiers visible in git history. Public consumers and forks retain full commit history.

### Option B: Rewrite History Before Public Release
**Cost:** Significant – destructive operation  
**Process:** Filter-branch or git-filter-repo to rewrite commits removing model identifiers from trailers, leaving only `Co-Authored-By: Claude <noreply@anthropic.com>`. Force-push main. Users who have cloned must re-clone. Tag-based releases point to commits with clean history.

**Risk:** Other branches (claude/*) remain with identifiers; upstream merges would reintroduce them if history is not rewritten across all refs.

---

## Decisions Pending

The owner must decide which option to take **before** public release. This gates the project from the open-source pipeline.

- [ ] Option A: Proceed with identifiers in history
- [ ] Option B: Rewrite history; owner confirms force-push is acceptable

---

## Passing Gates (Completed)

- ✅ **Security review:** Completed (PR #15, merged to main, commit 21bd4fc)
  - Braces advisory corrected to GHSA-vfj7-8cjw-p6xm
  - Endpoint authorization matrix rebuilt with verified rate limits
  - License audit completed (Anthropic SDK reference removed)
  - Workflows verified (action pins, gitleaks v8.28.0, CODEQL_UPLOAD=never)
  - Threat model aligned with test file citations
  - Verdict removed (no unsupported readiness claim)

- ✅ **Documentation:** All relative Markdown links resolve (`check-doc-links.py` passes)

- ✅ **OWNER-FOLLOWUPS:** Verified and actionable (docs/OWNER-FOLLOWUPS.md, merged to main)
  - authservice docker run command from AppHost verified
  - Copyright wording matches LICENSE
  - Image pinning location corrected (Program.cs const, not .WithImage)
  - Cleanup sections for post-merge branches and upstream proposals documented

---

## Next Steps After Gate Decision

1. Owner decides Option A or B
2. If Option B: rewrite history, force-push, confirm all refs clean
3. Proceed to deployment readiness (accessibility pass, 2FA e2e test, MCP host test, CLI TLS test)
4. Prepare legal review and deployment topology documentation
