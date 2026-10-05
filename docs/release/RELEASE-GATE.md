# Release gate

Run on 2026-10-05 against `main` at `062cd48` (before the correcting pull request), following the `open-source-release` guide of `konradcinkusz/architecture-standards`. Each item is **PASS**, **FAIL**, **NOT RUN** or **OWNER DECISION**, with the evidence or the reason. No session changed the repository's visibility, pushed a tag or deployed anything; this document does not either. The verdict is advisory to the owner.

An earlier version of this file (pushed directly to `main` by a Haiku-class session) checked one item and listed "passing gates" that were not gate items (for example "security review completed (PR #15, merged)": PR #15 was closed, not merged). It also listed 13 commits with model names while 16 exist on `main`. It was replaced by this one.

## Verdict

**NOT READY for a public release.** Two items FAIL or are undecided that only the owner can resolve (model identifiers in history; the dev-only `braces` advisory), the history-wide secret scan was **NOT RUN** with a real scanner in this pass, the licence audit was **NOT RUN**, and the legal sources remain unverified. Nothing in this table is a reason to think the repository contains secrets; it is a statement of what was and was not checked.

## Items

| # | Item | Result | Evidence / reason |
|---|---|---|---|
| 1 | Secret scan of full history, all refs | **NOT RUN** (partial evidence) | `gitleaks` is not available in the sandbox. The CI job `gitleaks` passed on every merged PR; its workflow checks out full history (`fetch-depth: 0`, `.github/workflows/secret-scan.yml:17`). Whether it also scans the 18 remote `claude/*` branches and PR refs was not verified. **Owner action:** run `gitleaks detect --log-opts="--all"` before any visibility change. |
| 2 | NuGet vulnerabilities | **PASS** | `dotnet list package --vulnerable --include-transitive`: no vulnerable package in any project. |
| 3 | npm vulnerabilities | **OWNER DECISION** | `pnpm audit` (web): 1 high, `braces` `<=3.0.3`, patched "None", GHSA-vfj7-8cjw-p6xm, via `eslint-config-next`. `pnpm audit --prod`: no known vulnerabilities, so not in the production set. Options in `docs/OWNER-FOLLOWUPS.md`. |
| 4 | Licence audit of dependencies | **NOT RUN** | No licence tool was run in this pass (`pnpm licenses` printed nothing usable; NuGet licences not enumerated). |
| 5 | LICENSE, SECURITY.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, PR and issue templates | **PASS** | All present (`LICENSE` is MIT). `SECURITY.md` points to GitHub private vulnerability reporting and falls back to "the address on their GitHub profile": the owner must confirm private vulnerability reporting is enabled on the repository, or add a real contact. |
| 6 | Personal data and real employer names | **NOT RUN** (partial) | The only e-mail-shaped strings tracked in the repository are reserved or synthetic (`a@b.example`, `a@example.invalid`, `admin@widgetron.example`, `jan.kowalski@example.com`, canary and fixture addresses). No full audit of names was done. |
| 7 | No model identifier in commits, PRs, docs, comments | **FAIL** | 16 commits on `main` carry a model name in their message or trailer (table below); 110 commits across all refs match, including branch commits that are squashed on `main`. The brief and `AGENTS.md` forbid this. History cannot be fixed without rewriting it. **OWNER DECISION:** leave as is, or rewrite history (`git filter-repo`) and force-push before going public; every clone and the 18 `claude/*` branches would need re-doing. |
| 8 | Every change went through a pull request with CI | **FAIL** (process) | `21bd4fc`, `f3bdc10` and merge `062cd48` were pushed directly to `main` by one session, against an explicit instruction, with no PR and no CI on those commits; `5415981` (bootstrap, so that `main` exists) was pushed directly by the orchestrator with the owner's approval. They are documentation-only. `check-doc-links.py` was red on `main` from `f3bdc10` until this correction. |
| 9 | Documentation links resolve | **PASS** (after this PR) | `python3 scripts/check-doc-links.py` failed on `main` (3 broken links in the replaced `RESULTS.md`); passes with this PR. |
| 10 | README commands work | **PASS** (partial) | `dotnet run --project src/ExitInterviewAgent.Cli -- demo --persona talkative --seed 1` twice: byte-identical output. Build and tests: `docs/research/RESULTS.md` §4. AppHost, web, e2e and container commands were not run in this pass (CI covers web, e2e and image builds). |
| 11 | Badges only for things that exist | **PASS** | README has two GitHub workflow badges (`ci`, `secret-scan`) and a static licence badge. The workflow badges render for visitors only if the repository is public. No coverage, version or deploy badge. |
| 12 | Legal sources verified | **NOT RUN** | A session tried and the egress proxy denied 9 primary sources (EUR-Lex, EDPB, ICO, UODO, OpenAI, docs.github.com, Gemma, Hugging Face, Mistral). `docs/legal/CONSIDERATIONS.md` §6 keeps those rows *unverified*. The owner can allowlist the domains in the environment's network settings and re-run. |
| 13 | Real-world checks | **NOT RUN** | No live Claude connector run (OP-18), no two-factor sign-in against the real `authservice` image (OP-17), no real TLS, Windows or macOS CLI run (OP-25), no screen-reader pass (OP-16), no real-model evaluation (`docs/research/RESULTS.md` §6). |
| 14 | Repository visibility | **UNCHANGED** | No session changed it. Decide after items 1, 3, 4, 7 and 12. |

## Commits on `main` with a model name in the message or trailer (item 7)

| Commit | Date | Subject |
|---|---|---|
| `f3bdc10` | 2026-10-05 | T12b: Complete release-gate documentation (tasks A-F): threat model test index, r |
| `e6f0e29` | 2026-10-05 | Reject double-barrelled questions in QuestionGuard; protocol 1.1 splits managemen |
| `21bd4fc` | 2026-10-05 | T12b: Correct defects in security review and owner follow-ups |
| `bf04edb` | 2026-10-05 | T11: CLI submit and delete-receipt: ticketed submission, receipt handling (#13) |
| `a20090e` | 2026-10-05 | T10: signals module: employer aggregates with uncertainty and disclosure control  |
| `43efc10` | 2026-10-05 | T8: MCP server for mode A (official SDK, Streamable HTTP, stateless) (#11) |
| `ca16611` | 2026-10-05 | T6: model providers (Anthropic key, OpenAI-compatible, Ollama), interactive inter |
| `4b4d6fa` | 2026-10-05 | T9: web panel: pages, two-factor sign-in, tickets, receipt deletion, nonce CSP, a |
| `c40e659` | 2026-10-05 | T7: evaluation harness: spec, scenarios as data, Layer 1/2, baseline gate, confor |
| `e6b60f4` | 2026-10-05 | T5: server-side submission: ledger, receipts, tickets, retention (#7) |
| `9bcb72a` | 2026-10-05 | T4: interview agent core, persona simulator and offline CLI demo (#6) |
| `360f2b6` | 2026-10-05 | T2: two JWT schemes (web + MCP), RFC 9728 metadata, BFF refresh rotation, consent |
| `2a4f9bd` | 2026-10-05 | T1: record schema v1, validation library and PII detector (#4) |
| `2a20a1f` | 2026-10-05 | T3: documentation foundation (privacy design, threat model, legal considerations, |
| `65ad0e4` | 2026-10-05 | T0: scaffold the estate's default containerized application (#1) |
| `5415981` | 2026-10-05 | Bootstrap main: MIT licence and project note |

## Options for item 7

- **Leave as is.** Cost: none. The names stay visible in public history and in forks.
- **Rewrite history before going public.** Cost: destructive. `git filter-repo --message-callback` to drop the model name from trailers, force-push `main`, delete or rewrite the 18 `claude/*` branches (they carry the same trailers), re-clone everywhere, and re-check that CI is green on the rewritten commits. Pull-request metadata on GitHub (titles, bodies, session links) is not rewritten by this.
