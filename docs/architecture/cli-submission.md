# CLI submission: from `record.json` to a receipt, and back

Status: **Implemented (T11)**, tested against a contract-mirroring fake and, in-process, against the real service. **Not run against a deployment**:
nothing is deployed. Decisions: [ADR-0057](../adr/0057-cli-submit-and-delete-receipt-commands.md) (commands, inputs, confirmation, address),
[ADR-0058](../adr/0058-cli-http-client-hygiene.md) (HTTP client), [ADR-0059](../adr/0059-cli-secrets-handling.md) (secrets),
[ADR-0060](../adr/0060-local-recheck-and-exact-preview.md) (local re-check, exact preview), [ADR-0061](../adr/0061-cli-receipt-handling.md) (receipts).
The server side is [submission-flow.md](submission-flow.md) and [ADR-0029](../adr/0029-receipt-deletion-semantics.md)/[ADR-0030](../adr/0030-submission-tickets-for-the-cli.md).
Brief §4 is the design: **no OAuth in the CLI**; a one-time ticket minted in the web panel.

## Sequence

```mermaid
sequenceDiagram
    autonumber
    actor P as Person
    participant W as Web panel /cli
    participant C as exit-interview CLI
    participant S as Service (anonymous endpoints)
    P->>C: interview --out dir (record.json written, valid and submittable)
    P->>W: sign in, "Create a ticket" (shown once, 15-20 min, single use)
    W-->>P: ticket
    P->>C: submit --record dir/record.json --server https://...
    C->>C: re-validate (schema, AI disclosure, covered topic, PII scan); fail closed
    C-->>P: what leaves this machine + the exact record
    P->>C: types "submit"
    C->>P: asks for the ticket (env, hidden prompt, or one stdin line)
    P-->>C: ticket
    C->>S: POST /api/v1/submissions/ticketed  [X-Submission-Ticket] body = record bytes
    alt accepted
        S-->>C: 201 {receiptCode}
        C-->>P: receipt code, once; the exact delete command; optional --save-receipt file (0600)
    else refused
        S-->>C: 4xx problem+json {code, errors?, kinds?}
        C-->>P: plain-language reason, whether the ticket is still usable
    end
    P->>C: delete-receipt --server https://... (code: env, hidden prompt, or stdin)
    C->>S: DELETE /api/v1/receipts  [X-Receipt-Code]
    S-->>C: 204 (same for every well-formed code) / 400 INVALID_RECEIPT_CODE
    C-->>P: "if it existed, it is deleted now"
```

## What leaves the machine

| Leaves | Does not leave |
|---|---|
| The record, byte for byte as shown (bands, ratings, quotes, employer reference, interview id, disclosure flags) | The transcript, the prompts and replies, file names, the provider, model or API key |
| The ticket, in one request header | The receipt code (only on deletion, in one header) |
| What any HTTP client sends: the client address, the time, `User-Agent: exit-interview` (no version), the route | Cookies, `Authorization`, any query string, any second custom header |

Known limit, shown to the person before they confirm: the ticket resolves to the web account, so **the redemption instant is a correlation point**
(threat model T-09). This command narrows nothing on the server and adds nothing: one request, minimal metadata, nothing retried after a byte was sent.

## The request, exactly

- `POST {origin}/api/v1/submissions/ticketed`, `Content-Type: application/json`, header `X-Submission-Ticket`, body = the file's bytes (at most 160 KiB, checked locally).
- `DELETE {origin}/api/v1/receipts`, header `X-Receipt-Code`, no body.
- The origin is `--server` or `EXIT_INTERVIEW_SERVER_URL`; there is no default; `https` only (loopback may be `http`); no user info, path, query or fragment.
- Redirects are never followed; at most one more attempt, and only if the connection could not be opened; 10 s connect, 30 s total; answers over 64 KiB are discarded.

## Secrets: where they come from and where they may appear

| Secret | Source (in order) | May appear |
|---|---|---|
| Ticket | `EXIT_INTERVIEW_TICKET`; hidden prompt on a terminal; else one line of stdin | Nowhere (not in output, files, logs, errors) |
| Receipt code (to delete) | `EXIT_INTERVIEW_RECEIPT_CODE`; hidden prompt; else one line of stdin | Nowhere |
| Receipt code (just issued) | the server's `201` | Standard output once; the `--save-receipt` file if asked |

There is **no flag** for either: argv appears in process lists and shell history. An architecture test enforces it.

## Outcomes

| Server answer | Message (short) | Exit |
|---|---|---|
| 201 | receipt code once + how to delete | 0 |
| 401 `TICKET_INVALID` | wrong, expired or used (not told which): mint a new ticket on the web `/cli` page | 5 |
| 403 `EMPLOYMENT_NOT_VERIFIED` | account not linked to this employer; ticket not used up | 5 |
| 409 `ALREADY_SUBMITTED` | one per account and employer; deleting does not reopen it | 5 |
| 409 `INTERVIEW_ID_TAKEN` | this record is already stored (likely an earlier attempt whose answer was lost) | 5 |
| 400 `NOT_JSON` / `DUPLICATE_KEY` / `NESTING_TOO_DEEP`, 413 `PAYLOAD_TOO_LARGE` | not a record as `interview --out` writes it | 5 |
| 422 record codes (`MISSING_FIELD`, `UNKNOWN_FIELD`, `LENGTH_LIMIT`, `PII_NOT_MASKED`, ...) | what is wrong and where (schema path) | 5 |
| 422 `AI_NOT_DISCLOSED` | the service stores only interviews where the AI was disclosed | 5 |
| 422 `PII_DETECTED` | kinds only (email, name, ...); edit the quotes; ticket not used up | 5 |
| 422 `PII_CHECK_FAILED` | the check could not finish; try later | 5 |
| 429 (`rate_limited`, `TICKET_LIMIT`) | wait N seconds (from `Retry-After`) | 5 |
| 3xx | a redirect, never followed; check the address | 6 |
| no connection / TLS failure | nothing was sent | 6 |
| timeout, broken connection, cancelled | **the outcome is unknown**: the server may have stored it; the receipt would be lost | 6 / 130 |
| delete: 204 | "if it existed, it is deleted now" | 0 |
| delete: 400 `INVALID_RECEIPT_CODE` | not well formed: a typo, not a deletion | 5 |

## Threat notes (rows in the [threat model](../security/THREAT-MODEL.md))

- **T-09** (redemption correlation): not changed by the client; the client does not add to it (no version in the user agent, no retry that sends the ticket twice, no extra identifiers). Stated to the user at the moment of consent.
- **T-15** (secrets in logs): the CLI has no logger on these paths, telemetry export subscribes to named sources only (a test fails if an HTTP source is added), secrets sit in a redacting type, exception text is never printed; canary tests for the ticket, the receipt code, the quote text and an address with user info and a query.
- **Redirect and downgrade** (new, client-side): a redirect cannot carry the header elsewhere; plain `http` only to loopback; TLS defaults untouched, with no switch to skip verification.
- **A hostile or broken server** can only choose among the codes the contract defines; it cannot put text or terminal escape sequences on the screen.
- **A stolen ticket** (T-09, [ADR-0030](../adr/0030-submission-tickets-for-the-cli.md)) submits once as that account; the ticket's short life and single use are the only limit. The CLI keeps the ticket out of files and history; it cannot protect the environment variable a person exported it into.

## Tests, and what they cannot show

| Where | What |
|---|---|
| `tests/ExitInterviewAgent.Cli.Tests/SubmitCliTests.cs` | one test per outcome; confirmation with scripted stdin; hidden prompt with and without a terminal; secret sources and precedence; local check; file permissions; redirects against a real loopback listener (a second listener must see no request); retry counts per error kind; timeout and cancellation; response cap; hostile answers; address rules; canary tests; no flag for any secret |
| `InterviewSubmitOfferTests.cs` | the offer at the end of `interview`; never automatic |
| `tests/ExitInterviewAgent.InterviewService.Tests/Submissions/CliEndToEndTests.cs` | web token mints a ticket on the real service, the real CLI submits with it, the receipt is shown once, the record exists, `delete-receipt` removes it; a spent ticket and a duplicate are refused with the real codes |
| mutation checks (run by hand, recorded in the T11 PR) | redirects allowed, `3xx` check removed, retry on any error, retry four times, ticket in the query, extra `Authorization` header, a `--ticket` flag, confirmation bypassed, local PII scan removed, receipt file readable by others, receipt file overwriting, server text echoed, plain `http` accepted, user info accepted, secret `ToString()` returning the value |

**Contract note.** `FakeSubmissionBackend` (Cli.Tests/Support) mirrors the T5 contract (status codes, headers, problem+json shape, `rate_limited` body). It has no shared fixture to
borrow from: if T5's contract changes, change the fake in the same PR. The end-to-end test against the real service is what notices when they drift.

**Not verified:** a real TLS connection (every test is over loopback `http` or an in-process handler; the HTTPS rule is the address check plus the untouched `SslOptions`); the hidden prompt
on Windows and macOS (verified once by hand on a Linux pty); a real deployment; behaviour behind a corporate TLS-terminating proxy.
