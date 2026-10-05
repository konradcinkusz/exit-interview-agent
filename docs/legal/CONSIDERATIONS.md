# Legal considerations

**Considerations, not legal advice.** Nothing here is a legal opinion or a compliance claim. It lists questions a lawyer
must answer before any real interview is run, records what could and could not be verified, and explains product decisions
that follow from the risks. Status vocabulary for external statements ([ADR-0017](../adr/0017-documentation-layout-and-claim-status.md)):

- **Verified**: the primary document was fetched and the operative text read; the row says what was read and when.
- **Reported by secondary source**: only a third party, a search snippet or a summary of the primary page said it.
- **Unverified**: not read, or could not be reached. No position is attributed to the provider or regulator.

**Verification date for every row: 2026-10-05.** Method: Anthropic pages and two GitHub raw files were fetched with `curl`
through the session's egress proxy, stripped to text, and the quoted sentences located with `grep`. Where a row says
*summary*, a summarising tool, not the raw text, was the reader, so the row is not promoted to Verified. **The environment's
egress policy blocked** `eur-lex.europa.eu`, `edpb.europa.eu`, `ico.org.uk`, `uodo.gov.pl`, `isap.sejm.gov.pl`,
`digital-strategy.ec.europa.eu`, `openai.com` (and its subdomains), `docs.github.com`, `huggingface.co`, `ai.google.dev` and
`privacy.claude.com`; no statute, regulator or those providers' terms could be read. Those rows are **Unverified**, and
re-verification is listed as a task for whoever has access (§6).

Companions: [privacy design](../privacy/DESIGN.md), [threat model](../security/THREAT-MODEL.md),
[open problems](../OPEN-PROBLEMS.md), [brief §2](../architecture/PROJECT-BRIEF.md).

## 1. Model-provider terms (what we may and may not support)

The project hosts no model; users bring their own access ([brief §1](../architecture/PROJECT-BRIEF.md)). Supported: API keys
(Anthropic, OpenAI-compatible endpoints) and local models (Ollama). Not supported: Claude subscription OAuth tokens and
GitHub Copilot as a backend. The table is the evidence.

| # | Question | Source (primary URL) | What was read | Status | Date |
|---|---|---|---|---|---|
| 1 | May a third-party app route requests through Claude Free/Pro/Max subscription credentials or Claude.ai login? | <https://code.claude.com/docs/en/legal-and-compliance>, "Authentication and credential use" | Raw page text. Operative sentences: "Anthropic does not permit third-party developers to offer Claude.ai login into their own applications, or to route requests through Free, Pro, or Max plan credentials on behalf of their users." and "developers may not collect, store, or intermediate Claude.ai credentials or session tokens". It directs such products to "API key authentication through Claude Console or a supported cloud provider". | **Verified** | 2026-10-05 |
| 1b | Same, for the Agent SDK | <https://code.claude.com/docs/en/agent-sdk/overview> | Raw page text: "Unless previously approved, Anthropic does not allow third party developers to offer claude.ai login or rate limits for their products, including agents built on the Claude Agent SDK. Use the API key authentication methods described in the Quickstart instead." | **Verified** | 2026-10-05 |
| 1c | Who authenticates when a customer offers Claude Code in their product? | same legal-and-compliance page | Raw text: "Customers may not pay for, resell, or intermediate Claude usage on their end users' behalf. Each end user must authenticate with their own Anthropic API key, Claude subscription plan credentials, or 3P inference provider credential" (this passage concerns the *unmodified Claude Code binary*, which this project is not). | **Verified** (as to wording; applicability to this project is our reading) | 2026-10-05 |
| 1d | Consumer Terms on automated access and credential sharing | <https://www.anthropic.com/legal/consumer-terms> ("Effective October 8, 2025" on the page) | Raw text: prohibits accessing the services "through automated or non-human means, whether through a bot, script, or otherwise" **"Except when you are accessing our Services via an Anthropic API Key or where we otherwise explicitly permit it"**; and "You may not share your Account login information, Anthropic API key, or Account credentials with anyone else." | **Verified** | 2026-10-05 |
| 2 | May a product use the Anthropic API under the Commercial Terms to serve its own users? | <https://www.anthropic.com/legal/commercial-terms> ("Effective June 17, 2025") | Raw text, A.1: permission "to use the Services, including to power products and services Customer makes available to its own customers and end users"; B: "Anthropic may not train models on Customer Content from Services."; D.2 incorporates the Usage Policy; D.3: "It is Customer's responsibility to evaluate whether Outputs are appropriate for Customer's use case, including where human review is appropriate". **Not read as addressed:** a *bring-your-own-key* distribution model specifically; nothing read forbids it, and nothing read blesses it. | **Verified** (wording); BYOK-specific position **Unverified** | 2026-10-05 |
| 2b | Usage Policy, employment-related and "high-risk" uses | <https://www.anthropic.com/legal/aup> ("Effective September 15, 2025") | Raw text: the list of high-risk use cases includes "Employment and housing: Use cases related to decisions about the employability of individuals, resume screening, hiring tools, or other employment determinations…"; for high-risk uses "a qualified professional in that field must review the content or decision prior to dissemination or finalization" and "you must disclose to them that you are using AI to help produce your advice, decisions, or recommendations". Our use (aggregate employer signals from former employees, no decisions about the interviewee) is **our assessment** to be outside that list, which a lawyer should confirm; any employer-facing drift toward decisions about individuals would move it inside. | **Verified** (wording); applicability is an **assessment** | 2026-10-05 |
| 2c | API data retention | <https://platform.claude.com/docs/en/build-with-claude/api-and-data-retention> | Read raw by a sub-agent: "Retained data is never used for model training without your express permission." and conversation content "is not retained by default" except Covered Models "which require 30-day retention"; flagged sessions "up to 2 years". Standard commercial retention is on `privacy.claude.com` (blocked). The sentence set was **not re-read by the author**. | **Reported by secondary source** (sub-agent reading; not re-verified by the author) | 2026-10-05 |
| 3 | OpenAI-compatible endpoints: whose terms apply, and may keys be supplied by end users? | OpenAI's own terms (`openai.com`, blocked) | **Not read.** A search snippet said the OpenAI Services Agreement bars buying, selling or transferring API keys and sharing account credentials; this is a snippet, not the document. That "an OpenAI-compatible endpoint is governed by the terms of whoever hosts it" is our inference, not a quoted provider position. | **Unverified** (snippet: **Reported by secondary source**) | 2026-10-05 |
| 4a | Ollama licence | <https://raw.githubusercontent.com/ollama/ollama/main/LICENSE> | Raw text begins "MIT License / Copyright (c) Ollama". Licence of the runtime only; **it says nothing about model weights.** | **Verified** | 2026-10-05 |
| 4b | A common model family's acceptable-use restrictions: Llama 3.1 | <https://raw.githubusercontent.com/meta-llama/llama-models/main/models/llama3_1/USE_POLICY.md> | Raw text prohibits "Engage in, promote, incite, or facilitate discrimination or other unlawful or harmful conduct in the provision of employment, employment benefits, credit, housing, other economic benefits, or other essential goods and services". **Llama 3.1 only**; other Llama versions and the Community Licence text itself were not read by the author. | **Verified** (this file only) | 2026-10-05 |
| 4c | Gemma, Qwen, Mistral model licences | `ai.google.dev`, `huggingface.co` (blocked); GitHub raw paths for Qwen returned 404 | **Not read.** A snippet reported that Gemma's use restrictions flow to downstream recipients. The `mistral-inference` code repository is Apache-2.0 per a summary, which is a *code* licence, not the weight licence. | **Unverified** | 2026-10-05 |
| 5 | GitHub Copilot credentials/tokens as a backend of a custom program | GitHub Generative AI Services Terms (`github.com/customer-terms/…`, 403 to `curl`); GitHub Terms of Service §J (`docs.github.com`, blocked) | **Not read by the author.** A summarising tool reported: the Copilot SDK requires a Copilot subscription "unless you are using BYOK" (bring your own key) and does not address reusing subscription credentials in a separate application; the Generative AI Services Terms say GitHub will not use Inputs or Outputs to train models absent documented instructions and do not mention API credentials; the older product-specific terms "have been deprecated effective 5 March 2026". None of this is verbatim. Terms §J (API terms) was never read. | **Unverified** (summary only) | 2026-10-05 |

**Consequences for the product (what we can say, and what we cannot):**

- *Claude subscription tokens are not supported.* This is a Verified position (rows 1, 1b): the provider's own pages say
  third parties may not offer Claude.ai login or route requests through Free/Pro/Max credentials. The brief's wording "Anthropic
  prohibits subscription OAuth tokens in third-party tools" is consistent with those pages. **A precise wording proposal for
  the README is in §7.**
- *GitHub Copilot is not supported because the terms could not be verified*, not because they were read and found to forbid it
  (row 5). The README must say "could not be verified", never "prohibited".
- *Anthropic API keys are supported*, with the caveats of row 2 (BYOK specifically unread) and row 2b (the use must stay
  outside decisions about individuals).
- *Mode A (MCP) never touches subscription credentials:* the user signs in to Claude through Anthropic's own client and
  that client connects to our server over OAuth with our authservice; we receive an authservice token, not a Claude credential.
  Whether Anthropic's terms allow *connectors* in an employment-related conversation was **not read** (Unverified).
- *Local models* are supported; the licence of the specific weights the user downloads governs their use. Row 4b shows a
  family whose acceptable-use policy mentions employment; **the project does not bundle or redistribute any weights**.
- Anthropic's branding rules (from the Agent SDK page, "Claude Agent" or "{YourAgentName} Powered by Claude"; the product must
  not appear to be Claude Code) were read by a sub-agent only (Reported by secondary source); we do not use those names in the
  product name.

## 2. GDPR considerations

**None of the provisions below were read in this environment (EUR-Lex was blocked): every statement about the text of the
Regulation is Unverified and is a pointer for counsel, not a claim.** Canonical location to read: Regulation (EU) 2016/679 on
EUR-Lex (`https://eur-lex.europa.eu/eli/reg/2016/679/oj`, **not fetched**). Article numbers are from general knowledge and must
be checked.

| Topic | Consideration | Status |
|---|---|---|
| **Is the record personal data?** | It has no user id, but carries quotes, an employer and bands. The test is whether a person is identifiable by means reasonably likely to be used (Art. 4(1) and Recital 26, as commonly cited). The design assumes **yes** ([ADR-0018](../adr/0018-records-are-treated-as-personal-data.md)). | Unverified (provisions); decision internal |
| **Roles** | Operator as controller; user's AI provider/host as own controller or processor; third parties named in text as data subjects ([privacy design §7](../privacy/DESIGN.md#7-gdpr-roles-as-considerations-not-conclusions)). Open source software without an operator is not an operator. | Unverified |
| **Lawful basis options** | Consent (Art. 6(1)(a)) is the natural candidate for the interviewee; legitimate interests (Art. 6(1)(f)) is more usual for processing about third parties and would need a balancing test; neither is chosen. Withdrawal of consent must be as easy as giving it, and the design's unlinked record makes withdrawal operable only via the receipt code (below). | Unverified; **decision for counsel** |
| **Special categories** | Free text may reveal health, union membership, political or religious opinions, etc. (Art. 9). Detection and masking will have false negatives; the agent must not solicit these; quote display off by default ([threat model T-02](../security/THREAT-MODEL.md)). | Unverified (provision) |
| **Data subject rights vs unlinkable records** | **An honest tension.** Access, rectification and erasure (Art. 15-17) presuppose finding the person's data. The design deliberately cannot find a person's records. The mechanism is the **receipt code**: the person proves they hold the code, the system deletes the record. Art. 11 (processing that does not require identification) and Art. 12(2) are commonly read as relevant here: a controller that cannot identify the person need not collect extra data to do so, but must act when the person supplies information that enables identification. Whether a receipt code satisfies that, and whether *access* (as opposed to erasure) can be honoured at all, is **an open question for counsel**; the product cannot offer "list my records" without breaking unlinkability ([OPEN-PROBLEMS](../OPEN-PROBLEMS.md)). Account deletion cannot erase records (nothing links them): the UI must say so. | Unverified; **open** |
| **DPIA** | Several criteria commonly used to decide whether a DPIA is required (sensitive/at-work context, vulnerable data subjects such as employees, innovative technology, large-scale or systematic processing) plausibly apply (Art. 35; EDPB/WP29 criteria). **Assume a DPIA is required** before any real data; a template is a precondition for §4, not a deliverable of T3. | Unverified; **assume required** |
| **International transfers** | The user's choice of provider and key determines where a transcript goes (Art. 44-49). That choice is the user's, not the operator's, but the product must tell the user which provider receives the transcript before it is sent ([privacy design §7](../privacy/DESIGN.md#7-gdpr-roles-as-considerations-not-conclusions)). Operator-side transfers (hosting) do not exist yet because nothing is deployed. | Unverified |
| **Retention** | Storage limitation requires a stated period; the proposed values are in [privacy design §6](../privacy/DESIGN.md#6-retention). The records have no automatic expiry in the brief; the brief now requires an operator-configured maximum record age (**Decided**, [ADR-0019](../adr/0019-brief-amendments-from-the-t3-legal-privacy-review.md); default 24 months is an assumption for counsel). | Proposal |
| **Third parties named in text** | Info duties toward people who are named (Art. 14) are hard to meet when the controller cannot contact them; masking names at ingest is the mitigation, with the exception provisions for disproportionate effort for counsel to assess. | Unverified |
| **Automated decisions** | The product makes no decision about the interviewee (Art. 22 not engaged on its face); signals are aggregates about employers. | Assessment |

## 3. Defamation and employer reaction

- A single review is a statement of fact or opinion about an identifiable organisation, often made by one person from memory. The
  organisation may respond with legal threats whether or not the statement is accurate. A person who is named (a manager) may too.
- **Therefore single reviews are never published** ([brief §2](../architecture/PROJECT-BRIEF.md)): the product publishes aggregates above K,
  with uncertainty, never quotes or individual ratings, and says "claimed by accounts" until employment is verifiable
  ([threat model T-10](../security/THREAT-MODEL.md)). That removes the main publication-based exposure; it does not remove the
  exposure of a database that holds the records.
- An operator may also face disclosure demands (T-19). The design leaves little to disclose, but not nothing.
- Statutory defamation and personality-rights provisions (Poland, as an example, commonly cites Civil Code arts. 23-24 and Penal Code
  art. 212) and the position of platform operators in the EU were **not read** here: **Unverified; counsel**.

## 4. Why real interviews are out of scope

Real interviews are out of scope ([brief §2](../architecture/PROJECT-BRIEF.md)) until **all** of the following exist, written and
reviewed, because each is a precondition rather than a polish item:

1. a **privacy policy** and terms the user can read before the first question, with versioned consent in authservice;
2. a documented **lawful basis** and a completed **DPIA** (§2);
3. **working deletion**: receipt-code deletion implemented and tested end to end, including backups ageing out and aggregate
   recomputation ([privacy design §5.3](../privacy/DESIGN.md#53-deletion-by-receipt-code-planned-t5-server-t9-web));
4. a **lawyer's review** of this schema, the roles, the retention table and the user-facing copy, in each jurisdiction where an
   operator would run an instance and where interviewees live;
5. an **operator** who accepts the controller role and the incident-response duties (nothing is deployed and no session may deploy);
6. a **security review** (T12) with the findings in the [threat model](../security/THREAT-MODEL.md) closed or accepted by the owner.

Until then: simulated personas only, and no real personal data anywhere in the repository (brief §2, [CONTRIBUTING](../../CONTRIBUTING.md)).

## 5. EU AI Act: an assessment, not a conclusion

**Sources not read.** The Regulation (EU) 2024/1689 (`https://eur-lex.europa.eu/eli/reg/2024/1689/oj`, not fetched) and the Commission's AI Act pages
were blocked. Everything below is an **assessment from general knowledge, Unverified**, and exists to tell a lawyer where to look.

- **Employment-related high-risk category.** Annex III lists, under employment and worker management, systems used for
  recruitment/selection, for decisions on working conditions, promotion or termination, for task allocation based on behaviour or
  traits, and for monitoring and evaluating performance. An exit-interview agent that produces *aggregate employer signals from
  former employees* does not, on its face, make or support any of those decisions *about the interviewee*. **Assessment: probably not
  within Annex III(4) as designed.**
- **Where it could drift in.** An employer-facing view that ranks or scores *individuals* ("who is a flight risk", "which manager is
  the problem"), a per-person record, or use of interview output in HR decisions about current staff would change the assessment.
  The architecture's anti-goals (no per-person view, no composite ranking; [privacy design §5.5](../privacy/DESIGN.md#55-aggregates-k-threshold-uncertainty-no-ranking-planned-t10)) are the structural guard.
- **Emotion recognition in the workplace.** The Act is generally described as prohibiting emotion-recognition systems in workplace
  and education settings (Art. 5(1)(f)). The agent must **not infer the interviewee's emotional state**; this also follows the
  repository's own rule that heuristics about human state are report-only and outside scoring
  ([metric-ethics §4](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md)). **Decided ([ADR-0019](../adr/0019-brief-amendments-from-the-t3-legal-privacy-review.md), T1):** the extractor schema has no sentiment/emotion field.
- **Transparency to the person talking to the AI (Art. 50).** A system that interacts directly with people is generally described as
  needing to tell them they are talking to an AI unless obvious. Our interviewer must say it is an AI at the start of every
  interview (modes A/B/C copy). **Decided (ADR-0019; T4/T8/T9 to implement):** the disclosure is recorded as `aiDisclosed` and a constraint scenario asserts it is made.
- **AI literacy and dates.** Obligations on providers/deployers and the application dates of the high-risk rules have, per search results
  only (**Reported by secondary source**, nothing opened), been amended by an "AI Omnibus" regulation in 2026; the reported Annex III date
  was 2 December 2027. **Do not rely on this**; check the Official Journal text.
- **Provider/deployer roles.** The project ships open-source software; an operator or a user running an agent could be a "deployer".
  Not analysed.
- **Limits of this assessment.** It does not consider national law, sector rules, or the interaction with the providers' own
  high-risk-use requirements (row 2b above).

## 6. Re-verification tasks (for a person or session with network access)

1. Read EUR-Lex GDPR (Art. 4, 6, 9, 11, 12, 14, 15-17, 22, 26, 28, 35, 44-49; Recital 26) and the AI Act (Annex III(4), Art. 5, 6, 50;
   the 2026 omnibus text) and replace §2 and §5 statements with quoted, dated rows.
2. Read OpenAI's Services Agreement and usage policies; Gemma/Qwen/Mistral weight licences; GitHub Generative AI Services Terms and Terms of
   Service §J; Anthropic's standard retention page. Promote or leave each row.
3. Re-read the Anthropic rows in this document if more than a few months old; these terms change.
4. Obtain a lawyer's opinion on §2's open tension and on [ADR-0018](../adr/0018-records-are-treated-as-personal-data.md).

## 7. Proposed changes to the brief and README wording (adopted by ADR-0019)

These were proposals when written. The orchestrator, on the owner's delegated authority, adopted (a)-(f) in
[ADR-0019](../adr/0019-brief-amendments-from-the-t3-legal-privacy-review.md) and the brief now carries them. They remain
design decisions, not implemented behaviour and not legal conclusions.

1. **Brief §2, second bullet, wording.** Replace "Anthropic prohibits subscription OAuth tokens (Free/Pro/Max) in third-party tools; Copilot
   terms for use as a backend were not verifiable" with: "Anthropic's own documentation states third parties may not offer Claude.ai
   login or route requests through Free/Pro/Max credentials (read 2026-10-05); the terms for using GitHub Copilot as a backend could not
   be verified (2026-10-05)". Adopted: brief §2 now uses this wording; so does the README.
2. **Brief §6, ledger/records.** Add an explicit requirement that record timestamps are coarse (week) and ledger timestamps coarser
   (day or none) so timing is not a join key (T-08 (c)).
3. **Brief §6, aggregates.** State that K applies per displayed cell, not just per employer, and that publication is batched (T-01).
4. **Brief §6, agent rules.** Add: the interviewer discloses that it is an AI at the start; the extractor schema has no emotion/sentiment field (§5).
5. **Brief §6, retention.** Add a maximum record age to be set by the operator (§2 retention).
6. **README/UI.** Never describe records as "anonymous" ([ADR-0018](../adr/0018-records-are-treated-as-personal-data.md)).
