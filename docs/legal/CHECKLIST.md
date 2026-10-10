# Legal review checklist: hosted web app

> **Not legal advice.** This file lists the questions a lawyer must answer before the first paid interview. It does not answer them.
> Every item carries `status: NOT REVIEWED` until a named lawyer records an answer. No item may be marked reviewed by the project or by an AI session.
> Drafts it refers to: [privacy-policy.pl.md](privacy-policy.pl.md), [privacy-policy.en.md](privacy-policy.en.md), [terms.pl.md](terms.pl.md), [terms.en.md](terms.en.md), [consent-wording.md](consent-wording.md).
> Gate: [RELEASE-GATE item 16](../release/RELEASE-GATE.md). The gate stays NOT RUN until the review is recorded here.
> Written 2026-10-10. Sources for statements of law were **not** verified in this pass: the egress proxy blocked the primary sources (see [CONSIDERATIONS §6](CONSIDERATIONS.md#6-re-verification-tasks-for-a-person-or-session-with-network-access)). Article numbers below are pointers for the lawyer, not checked citations.

## How to use this file

1. The owner gives the lawyer the drafts and this file.
2. For each item, the lawyer writes the answer and the change they want under "Answer", then sets `status:` to `REVIEWED (name, date)` or `CHANGES REQUESTED`.
3. The owner applies the changes to the drafts and the web app (section B), then marks RELEASE-GATE item 16 PASS, with the reviewer's name and date.
4. Until then the web app must not take payment and must not run an interview with real people.

---

## A. Questions for the lawyer

### L1. Defamation and personal rights

- **Question:** Who is liable when a draft text (a review or forum post) written from a conversation contains a factual claim about a named or identifiable manager or colleague, and the user publishes it? Does the service's role in generating the draft create its own liability? Is the "drafts, not facts; you are responsible" notice (terms §2–3) enough, or does it need a stronger form, for example a warning before publication?
- **Why it matters here:** the tile feature exists to produce publishable text. Defamation (Polish civil law on personal rights, criminal defamation) is the principal open risk the owner named.
- **Related question:** should the service mask named people in tiles more strictly than in the record, and must it refuse to produce a tile that names a person?
- **status:** NOT REVIEWED
- **Answer:**

### L2. Platform rules and sponsored or fake reviews

- **Question:** Do the rules of Glassdoor, Google (business reviews) and Reddit prohibit posts written with an AI tool, posts from people who are not verified, or posts written for a service? Does a tile formatted to look like a review on one of these platforms create a risk of being treated as a fake or sponsored review?
- **Question:** Does the EU/Polish rule on fake and incentivised reviews (the 2019/2161 "omnibus" directive as implemented in Poland) apply to a service that drafts reviews for a user, even if the user publishes them? Does it apply to the service itself?
- **Question:** Should tiles avoid platform-specific formats altogether, or carry a disclosure ("written with an AI assistant from my interview")?
- **status:** NOT REVIEWED
- **Answer:**

### L3. Roles under GDPR: controller or processor

- **Question:** For account and payment data, is the operator a controller (plan §5 assumption)? For the conversation, which is held only in memory but passed to the model provider, is the operator a controller, a processor, or a mere conduit? Does the answer change if the operator stores nothing after the interview?
- **Question:** Is the model provider a processor under a data processing agreement, or a separate controller for API data under its own terms? What follows for the privacy policy (section 5) and for the consent wording (consent [A])?
- **Question:** Is a joint-controller arrangement possible, and if so is it needed?
- **status:** NOT REVIEWED
- **Answer:**

### L4. Special-category data (Article 9 GDPR) in exit interviews

- **Question:** An exit interview can contain health information, information about harassment, mobbing or discrimination, trade-union membership, sexual orientation, or allegations of crime (Article 10). What legal basis covers that content processed by the service? Is explicit consent in the consent screen enough, and does it need to be a separate consent?
- **Question:** Does the PII guard (masking names and similar) reduce the obligation, or does it only reduce risk? Should the interview refuse or redirect on such content?
- **Question:** The record and drafts may repeat special-category data verbatim in quotes. Is that acceptable in a draft the user publishes, and whose responsibility is it?
- **status:** NOT REVIEWED
- **Answer:**

### L5. Data processing agreement with the model provider

- **Question:** Under which terms does the provider process API data, for how long does it keep it, and does it use it for training? These are facts about the provider's current terms; the project has not verified them. The lawyer must read the terms that apply to the workspace the owner will use.
- **Question:** Which data processing agreement is required, and does the provider offer one for this kind of account? What must it contain (Article 28 GDPR)?
- **Question:** Transfer basis for processing outside the EEA, if any (standard contractual clauses or an adequacy decision, depending on where processing happens).
- **status:** NOT REVIEWED
- **Answer:**

### L6. VAT and the payment operator

- **Question:** VAT treatment of a one-time digital service sold to EU consumers from Poland (place of supply, one-stop shop if relevant), and whether the price must be shown gross.
- **Question:** Invoices: what must the service issue for each credit purchase, and who issues them, the operator or a payment intermediary?
- **Question:** Role and contract of the payment operator (Stripe is the planned provider in plan §4; it is not chosen as final). Does the operator need its own data processing terms?
- **status:** NOT REVIEWED
- **Answer:**

### L7. Information duty (Articles 13 and 14 GDPR) for people named in the conversation

- **Question:** The conversation may name managers and colleagues who never gave data to the service. Does the service owe them information (Article 14)? Is the exemption for disproportionate effort, or for information already held by the data subject, available? Does masking change the answer?
- **Question:** Should the user be told (in the consent screen or the policy) that third parties may be mentioned, and that the user must not give their data unless needed?
- **status:** NOT REVIEWED
- **Answer:**

### L8. Right of withdrawal for digital content (consumers)

- **Question:** Under Polish consumer law implementing the Consumer Rights Directive (2011/83/EU), can a consumer withdraw from a one-time digital service after the interview starts? Does an express request to start the service immediately, with acknowledgement of loss of the right, make the withdrawal right lapse? What must the checkout show?
- **Question:** Is the terms' rule "no credit refund if consent is withdrawn during the interview" (terms §7.5) compatible with consumer rights, and how should the refund rule read?
- **status:** NOT REVIEWED
- **Answer:**

### L9. Confidentiality agreements and labour law

- **Question:** A former employee may have signed a non-disclosure, confidentiality or non-disparagement clause. Does using the service to describe the employer, and publishing a draft, risk breaching such a clause, and does the service have any duty to warn the user about it?
- **Question:** Does an employer have any claim against the service (for example, unfair competition or interference) for producing or publishing content about it?
- **status:** NOT REVIEWED
- **Answer:**

### L10. Terms: enforceability and consumer protection

- **Question:** Are the liability exclusions in terms §5 enforceable against consumers under the unfair-terms rules (Civil Code, Article 385¹ et seq. as the lawyer reads them)? Which parts must be removed or softened?
- **Question:** Governing law and competent court for consumers and for businesses. Should the service use separate terms for each?
- **status:** NOT REVIEWED
- **Answer:**

### L11. Cookies, sessions and analytics

- **Question:** Does the app need a consent banner for cookies? The session cookie and the CSRF cookie are needed for sign-in; does the telecommunications law exemption cover them? Confirm that no analytics tool may load on pages that show conversation content.
- **status:** NOT REVIEWED
- **Answer:**

---

## B. Changes needed to the `/privacy` page after review (do not change now)

The page exists and this pass did not change it. After the lawyer's review, the owner or a web session must change the following. Each point is a fact the current page gets wrong for the hosted service, or a gap.

1. **Facts list, item 1** (`web/app/lib/messages/en.ts`, `privacy.facts`): "The interview transcript stays on your side ... This service receives only the final record." This is false for the hosted service: the conversation passes through server memory and to the model provider. Rewrite it for the hosted mode, or show the hosted and CLI/MCP designs separately.
2. **Facts list, item 2**: check that the record in the hosted mode carries no account, name or e-mail (a test is needed; see section C).
3. **Limits list, item 2**: "The AI client or provider you use sees your conversation under your own terms" describes the CLI/MCP mode. In the hosted mode the service sends the conversation to Anthropic on its own key. Name Anthropic and state the operator's role as the lawyer decided (L3).
4. **Missing content** for the hosted mode: the controller's name and contact, the payment provider, the 30-minute retention, the "Stop and delete" control, the AI disclosure, the drafts notice, the rights list and the right to complain to the Polish supervisory authority, the link to the full policy and terms.
5. **Language:** only `en.ts` exists under `web/app/lib/messages/`. A Polish version of every page is required for W6 (plan §7). The privacy page has no Polish text.
6. **Metadata description** (`web/app/lib/messages/en.ts`, app description): "Simulated data only." It must be removed before the first paid interview, because real personal data will be processed.
7. **Policy version:** the consent screen and the page must show the same version number of the policy, which the lawyer's review sets. Re-ask consent when it changes.
8. **Links** on the page point to `docs/privacy/DESIGN.md` on GitHub. Keep them, but they describe the CLI/MCP design; add the hosted policy as the authority for hosted users.
9. Do not publish the page with the drafts' text. Publish only the reviewed text, with the review date.

## C. Facts the drafts rely on (to be proved in code before launch)

The drafts state these as design facts. Each must be backed by a test or a documented, reproducible check; if one is false, the drafts must change first.

| Claim in the drafts | Where it must be proved |
|---|---|
| Conversation content is not written to the database or to logs | a test that scans the database schema and log output after a full interview (plan W2: "no text in logs") |
| The conversation and the result are removed after 30 minutes of inactivity or completion | a test with a clock that advances past the expiry (contract §10: `expiresAt`) |
| The result is returned only to the account that created the interview | an authorization test (`404` for another account, contract §10) |
| Deletion by "Stop and delete" removes the conversation and the result | a test that calls `DELETE /interviews/{id}` and then checks memory and `GET` returns `404` |
| A submission to employer signals happens only with consent C | a test that a submission without consent is refused |
| A credit is returned on a service-side failure, not on withdrawal | tests for `failed` and `stopped` (contract §10, status values) |
| No card data reaches the service | a review of the checkout integration (redirect, no card fields) |
| Logs and metrics carry counts only | the canary-in-logs test (T5 amendment, ADR-0019) |

## D. What this file does not do

- It does not answer any question. Each answer is the lawyer's.
- It does not check any statute, regulator's page or provider's terms. The sources were unreachable in this pass (see the note at the top).
- It does not decide the price, the retention periods marked `[…]`, the payment provider or the hosting provider. These are owner decisions and the lawyer's inputs.
- It does not cover the licence of dependencies or the employer-side product (plan §1 non-scope).
