# Open problems

What this project does **not** solve, why each matters, what it does about it now, and what would close it. A listed problem is
an acknowledged decision; an unlisted one is drift ([`security-review` §3](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SECURITY-REVIEW.md)).
Status vocabulary: [ADR-0017](adr/0017-documentation-layout-and-claim-status.md). Related: [threat model](security/THREAT-MODEL.md),
[privacy design](privacy/DESIGN.md), [legal considerations](legal/CONSIDERATIONS.md).

| # | Problem | Severity |
|---|---|---|
| OP-1 | Real employment verification | High |
| OP-2 | Employer registry and identity | High |
| OP-3 | Band granularity vs small groups | High |
| OP-4 | Sample bias toward technical users | Medium |
| OP-5 | Model bias | Medium |
| OP-6 | Multilingual interviews (Polish and English first) | Medium |
| OP-7 | CLI login without public-client support in authservice | Medium |
| OP-8 | MCP sampling support | Low |
| OP-9 | Only Claude is supported through MCP | Low |
| OP-10 | Per-host client registration | Low |
| OP-11 | Judge calibration labels | Medium |
| OP-12 | Receipt codes: access without a list of records | Medium |

## OP-1. Real employment verification

- **Why it matters.** Without proof that the author worked at the employer, the system cannot tell a real ex-employee from a script,
  a competitor or the employer itself ([threat model T-10](security/THREAT-MODEL.md)). Every aggregate is "claimed by accounts".
- **What we do now.** `EmploymentVerifier` is an interface with a mock (brief §2, T5); one submission per (account, employer) via the
  ledger; rate and size limits; uncertainty on every number; outputs must say "claimed", not "verified".
- **What would close it.** A verification method that does not reintroduce a person↔employer link in our database: for example,
  a third-party attestation that issues an unlinkable, employer-scoped credential (blind-signature style) or a work-email challenge whose
  result is not stored. Each has its own privacy cost and legal review; none is chosen. Whichever is chosen is an ADR and a threat-model revision.

## OP-2. Employer registry and identity

- **Why it matters.** "Employer" is a field of the record (brief §3.1). Free-text names split one employer into many (defeating K) or let
  a user create a one-person "employer" to expose someone; legal entities, brands and subsidiaries differ.
- **What we do now.** Nothing in code: the model is Planned (T5). *Proposal:* a closed, operator-curated employer id list with canonical
  names; unknown employers are not accepted in v1.
- **What would close it.** A curated registry with a documented policy for merges, minimum size and removal (an employer that asks to be
  removed), plus a size floor below which an employer's signals are never shown ([OP-3](#op-3-band-granularity-vs-small-groups)).

## OP-3. Tenure and role band granularity vs small groups

- **Why it matters.** Finer bands make a record more informative and a small group easier to identify ([T-01](security/THREAT-MODEL.md)); K = 5
  per employer says nothing about K per band.
- **What we do now.** The brief fixes n ≥ K (default 5). *Proposals* ([privacy design §5.5](privacy/DESIGN.md#55-aggregates-k-threshold-uncertainty-no-ranking-planned-t10)):
  K per displayed cell, no cross-products, batched publication, coarse bands. The band sets themselves are not defined (T1).
- **What would close it.** Band sets chosen against a modelled smallest-realistic-group, a documented size floor for employers, and (if the
  project later wants stronger guarantees) a formal approach such as noise addition, evaluated rather than assumed. Evidence needed:
  simulated re-identification tests on synthetic populations (eval-adjacent work for T7/T10).

## OP-4. Sample bias toward technical users

- **Why it matters.** Users who run an MCP host or a CLI with an API key are mostly technical; aggregates will over-represent software
  employers and under-represent people for whom this tool is out of reach. The signals are not a representative view of any employer.
- **What we do now.** The web path (mode C) runs no model and the web app is aimed at account management; interviews need a model access
  route, so the bias is structural. Documentation says aggregates reflect *users of this tool*.
- **What would close it.** A hosted, operator-paid interview path (explicitly outside the current brief: "the project hosts no model") and a
  published description of who the respondents are; until then, no claim of representativeness.

## OP-5. Model bias

- **Why it matters.** The interviewer, extractor and judge are language models: they may probe some groups of people or some topics differently,
  mis-rate accented or non-native writing, or mask names unevenly across languages and cultures (a PII guard tuned on English names misses others).
- **What we do now.** Nothing measured yet. The eval harness ([METHODOLOGY](eval/METHODOLOGY.md)) defines metrics per persona class; PII leakage
  is measured with a planted-token set (T7).
- **What would close it.** Persona sets that vary language, register and name origin on the same underlying story, with leakage and fidelity
  reported per variant and compared; a stated limit when a language has no coverage. Requires writing the persona corpus (T7) and judging by humans.

## OP-6. Multilingual interviews (Polish and English first)

- **Why it matters.** The target users include Polish speakers; PII detection, quote fidelity, masking and judge rubrics are language-dependent,
  and a record's quotes are verbatim in the original language.
- **What we do now.** Docs are English (brief §9.9). The record schema, personas and PII detector are Planned (T1/T7); no language handling exists.
- **What would close it.** A declared language per interview, language-specific PII rules and tests (declension makes name masking harder in
  Polish), personas in Polish and English, and a conformance report per language. Until then the supported-language statement is "English
  tested, Polish untested".

## OP-7. CLI login without public-client support in authservice

- **Why it matters.** authservice registers only confidential clients (secret ≥ 32 bytes, no `none`, no device grant, no DCR; authservice ADR-0005),
  and a distributed CLI cannot hold a secret. The ticket workaround adds the redemption correlation point ([T-09](security/THREAT-MODEL.md)).
- **What we do now.** Submission tickets minted by the web panel ([privacy design §5.4](privacy/DESIGN.md#54-submission-tickets-for-the-cli-planned-t5t9t11)); no change to authservice (brief §4).
- **What would close it.** Public-client + PKCE (or the device-authorization grant) in authservice, which means amending its ADR-0005 by its owner;
  then the CLI can authenticate directly, and the ticket row (the correlation point) disappears. The CLI would still send a record under the user's `sub`
  to compute the ledger entry, so *some* identity must reach the ingest at submission, at the moment of submission.

## OP-8. MCP sampling support

- **Why it matters.** Sampling would let our server ask the user's host model to run parts of the protocol, but client support is uneven.
- **What we do now.** The brief says not to rely on it unless verified and recorded in an ADR ([brief §4](architecture/PROJECT-BRIEF.md)). We do not use it; support
  in the host was **not verified** in this session.
- **What would close it.** Read the host's published documentation for the supported MCP features, record the result and date in an ADR, and
  add it only if it brings a privacy or quality benefit.

## OP-9. Only Claude is supported through MCP

- **Why it matters.** The privacy of mode A depends on one host's terms and retention (T-07); users of other assistants are excluded.
- **What we do now.** Claude only, redirect URI `https://claude.ai/api/mcp/auth_callback` (brief §3.2).
- **What would close it.** Per-host verification of the MCP authorization flow and data terms, then an ADR per host.

## OP-10. Per-host client registration

- **Why it matters.** authservice registers MCP clients statically from configuration (its ADR-0005: no dynamic registration); each host needs its own
  client id, secret, redirect URIs, scopes and resource, held by the operator.
- **What we do now.** Documented in authservice's `DEPLOYMENT.md` ("Registering an MCP client"); wiring is Planned (T2/T8).
- **What would close it.** A documented operator runbook per host and a startup check that fails loudly on a missing client; dynamic registration
  would need authservice to change its stance.

## OP-11. Judge calibration labels

- **Why it matters.** An LLM judge that was never compared with people is a measuring stick nobody has checked
  ([METHODOLOGY §7](eval/METHODOLOGY.md); the reference repository's own labels were AI-written and are described there as a rehearsal).
- **What we do now.** Judge scores are reported and trended; they gate nothing (Planned, T7).
- **What would close it.** ≥ 40 human labels over ≥ 8 scenarios under a named human handle, κ ≥ 0.6 (starting thresholds), recorded in the repository.

## OP-12. Receipt codes: access without a list of records

- **Why it matters.** A person cannot list their own records (there is no link), so "access" rights depend on a code the person must have kept
  ([legal §2](legal/CONSIDERATIONS.md)). A lost code means the record cannot be deleted by its author.
- **What we do now.** Show the code once with a clear warning; nothing else (Planned, T5/T9).
- **What would close it.** An *optional*, client-side-only receipt store (the CLI writes encrypted receipts to local disk; the web app offers a
  downloadable receipt file) so the server never learns the link, and a lawyer's view on whether that satisfies the rights it is meant to serve.
