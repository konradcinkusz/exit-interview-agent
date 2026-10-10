# Privacy policy: hosted exit-interview web app

> **DRAFT for review by a lawyer. Status: NOT REVIEWED.**
> This is not legal advice and not a finished document. It describes how the service is meant to work and lists what must be confirmed.
> Text in square brackets `[…]` is to be filled in by the owner or a lawyer. Do not publish this text before review (see [CHECKLIST.md](CHECKLIST.md)).
> Draft version: 0.1, 2026-10-10. Polish text, for Polish-language users (which text governs is for a lawyer to decide): [privacy-policy.pl.md](privacy-policy.pl.md).

## 1. Who is the controller

- Controller: `[ADMINISTRATOR]` (name, address, tax identifier).
- Contact for data matters: `[KONTAKT]`.
- Data protection officer: `[DPO, or: not appointed, to be decided]`.

Working assumption for review: the controller is responsible for account and payment data. For the conversation, the service acts as a technical intermediary that passes it to the model provider. The allocation of roles is a question for a lawyer (see CHECKLIST, L3).

## 2. What data we collect and why

| Category | What it contains | Purpose | Where it is held |
|---|---|---|---|
| Account | e-mail address, account identifier, sign-in method, consents | sign-in, verifying the address, proof of consents | identity service (authservice), run by `[ADMINISTRATOR]` |
| Payment | order, number of interviews bought, payment status, payment identifier at the provider | granting an interview, billing, invoices | the service's database; card data **never reaches us**, it is handled by `[PAYMENT PROVIDER]` |
| Conversation | your answers, the interviewer's questions, what is said during the interview | running the interview | **only in server memory** during the interview (sections 3 and 4); sent to the model provider (section 5) |
| Result | the structured record (ratings and short quotes) and the draft texts | showing the result to you | **only in server memory** for 30 minutes after the interview ends; then deleted |
| Submission to employer signals | the record, only if you have clearly agreed | aggregated statistics shown only above a minimum number of records | the service's database; deleted as in section 4 |
| Technical data | timestamps, number of model calls and estimated token count, IP address for rate limits | security, limits, cost control | logs and counters without conversation content; `[period, to be decided]` |

We do not use the data for profiling or advertising. The app does not use analytics tools on conversation content.

Legal bases (to be confirmed by a lawyer): account and payment, a contract with you (Article 6(1)(b) GDPR); submission to employer signals, voluntary and withdrawable consent (Article 6(1)(a) GDPR); conversation content that contains special-category data needs a separate basis (Article 9 GDPR), see CHECKLIST, L4.

## 3. What we do not keep (a design statement)

- We do not write conversation content to the database or to logs.
- The result (record and drafts) is returned only to you. We do not publish it and do not send it to anyone else.
- Logs, traces and metrics contain no conversation content, names or e-mail addresses.

> This statement must be tested before launch. If the code stores conversation content, the statement is false and this section must be withdrawn.

## 4. How long we keep data

| Data | Period | What happens afterwards |
|---|---|---|
| Conversation content (in memory) | until the interview ends, or 30 minutes without activity | removed from memory |
| Result (record and drafts) | 30 minutes after the interview ends | deleted; you can delete it earlier |
| Submission to employer signals | until you ask for deletion, or until `[maximum record age, to be decided]` | deleted automatically or on request |
| Account data | until the account is deleted, or `[period, to be decided]` | deleted (for review: proof of consents may need to be kept longer) |
| Payment data and invoices | `[period required by tax law, to be decided by a lawyer or accountant]` | deleted after the period |
| Submission markers (a one-way hash of an account and employer pair) | `[period, to be decided]` | deleted after the period |
| Logs and counters without content | `[period, to be decided]` | deleted after the period |

Deletion on request: the "Stop and delete" button during the interview deletes the conversation and the result. A submission is deleted with its receipt code, or through `[KONTAKT]`. The service does not reveal whether a submission existed.

## 5. Processors and recipients

| Recipient | What it receives | Role (for review) |
|---|---|---|
| **Anthropic** (the model provider, `Anthropic, PBC`) | the conversation content needed to run the interview and to write the result and drafts | processor or separate controller, depending on its terms for API customers; **the project has not verified the provider's terms or retention rules for this account**; data processing agreement `[to be signed or confirmed]` |
| `[PAYMENT PROVIDER]` | order and payment data | controller or processor, to be decided |
| `[HOSTING]` (server infrastructure) | data stored on servers | processor, `[processing agreement]` |
| Identity service operator | account data | `[to be decided]` |

We do not sell data and do not share it with advertisers.

## 6. Transfers outside the European Economic Area

The conversation content is sent to the model provider. `[Where the provider processes it: to be confirmed]`. If processing takes place outside the EEA, the transfer basis must be `[standard contractual clauses or another basis, to be decided by a lawyer]`. The project has not verified the legal sources for this transfer (see `docs/legal/CONSIDERATIONS.md`, section 6).

## 7. Your rights

You have the right to: access your data, rectification, erasure, restriction of processing, data portability, objection to processing based on legitimate interests, and withdrawal of consent at any time. Withdrawal does not affect the lawfulness of processing before the withdrawal.

You also have the right to lodge a complaint with the President of the Personal Data Protection Office (Poland). Requests to the controller: `[KONTAKT]`.

How the service handles these rights in practice (procedure, deadlines, exceptions) needs a lawyer's review.

## 8. Automated processing

The interview is run by an AI system. You are told this at the start. The results are drafts for you to read. They have no legal effect on you or on anyone else, are not an employment, service or other decision, and the service does not check the facts you give.

## 9. Third parties in the conversation

Names or details of third parties (managers, colleagues) may come up in the conversation. The service tries to detect and mask them but cannot guarantee this. Do not give third parties' details unless needed. Information about people who cannot be masked may require separate information to them (see CHECKLIST, L7).

## 10. Security

Access requires sign-in. Provider keys are held only on the server. Traffic is encrypted. No security measure is absolute: in case of a breach we will inform you and the supervisory authority as the GDPR requires.

## 11. Changes to this policy

We will tell you about material changes when you sign in. Consents record the version of the policy they were given under.

## 12. Disclaimer

This document is a draft. It does not replace advice from a lawyer and is not legal advice.
