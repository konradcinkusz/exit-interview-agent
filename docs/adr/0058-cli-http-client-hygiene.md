# 0058. CLI HTTP client: no redirects, one secret header, caps, and exactly what is retried

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): `security-review` §7 (errors never leak internals), `service-api-patterns` §1; threat model T-09, T-15; [ADR-0057](0057-cli-submit-and-delete-receipt-commands.md)

## Context

The CLI sends a bearer secret (a ticket, later a receipt code) to an address the user typed. Everything the HTTP stack does by default on the user's behalf (follow redirects, keep cookies, retry, decompress, print exception text) is a way for that secret or the record to go somewhere unplanned.

## Decision

All of it is in `SubmissionClient`; each point has a test, and the first two are mutation-checked.

- **Redirects are never followed.** The real handler is a `SocketsHttpHandler` with `AllowAutoRedirect = false`, and any `3xx` that arrives is reported as a failure and its `Location` is not printed. A redirect would forward the secret header (and, for 307/308, the record) to whatever host it names. Test: a real loopback listener answers 301/302/307/308 pointing at a second listener; the second must see **zero** requests. (Allowing redirects fails this test; so does dropping the `3xx` check.)
- **Exactly one secret header per request:** `X-Submission-Ticket` for the submission, `X-Receipt-Code` for the deletion. No `Authorization`, no cookies (`UseCookies = false`), no query string, no other custom header. `Content-Type: application/json` only on the submission.
- **`User-Agent: exit-interview`: the product name and nothing else.** No version, no OS, no runtime. The redemption instant already links an account (through the ticket) to a record (T-09); the request metadata that accompanies it (client address, time, route, user agent) should add as little as possible. Cost: the server cannot tell CLI versions apart from the header. It does not need to: compatibility rides on the record's `schemaVersion` and the problem `code`s, which are versioned contract. (Decided here; revisit only if a deployment needs a version for a compatibility fix, and then as an opt-in, not a default.)
- **TLS is the platform default and is not touched:** no certificate callback, no pinned protocol list, no client certificates, no switch to skip checks (a test asserts the handler's `SslOptions` are untouched). `https://` is required by `ServerUrl` except for loopback.
- **Timeouts and caps:** 10 s to connect, 30 s for the whole call (headers and body, one deadline), response headers at most 16 KiB, **response body at most 64 KiB** (a larger or lying answer is discarded, `ResponseTooLarge`), no automatic decompression. A success body that is not a receipt code of the expected shape is `BadResponse`, not trusted.
- **Retries, stated exactly: none, with one exception.** A request is attempted **once more** (after 750 ms) if and only if the connection could not be opened at all: `HttpRequestError.NameResolutionError` or `ConnectionError`, i.e. before any byte of the request left, so nothing can be duplicated. **Not retried:** a TLS failure (not transient, and a certificate problem should be seen), a timeout, a broken connection after sending, any HTTP answer including 429 and 5xx (the message says how long to wait; the user decides), cancellation. The same rule applies to the deletion (it is idempotent, but one rule is easier to reason about and to test). Tests count calls per error kind; "retry on any exception" and "retry up to four times" both fail them.
- **No exception text is ever surfaced.** .NET connection errors carry host names and addresses (and, if one had been put there, a URL's user info). Every failure is one of nine `Failure` kinds with fixed wording that shows at most `scheme://host:port` of an address that already passed `ServerUrl`. Canary test: exceptions built with a canary in their message never reach any output.
- **The server is not trusted to print to a terminal.** Only the contract's fields are read (`code`, `errors[].code|path`, `kinds[]`, the `rate_limited` shape, `Retry-After`) and a value is kept only if it has the shape of a code (`^[A-Z][A-Z0-9_]{0,63}$`), a schema path or a kind name. The `title`, and anything else a server (or a proxy in between) puts in a body, is dropped; control characters and escape sequences cannot reach the terminal (test with ESC sequences; widening the code pattern fails it).
- **Cancellation:** Ctrl-C reaches the request through the host's token; the outcome is reported as unknown (the server may have received it).

## Consequences

- A hostile or mistaken address can see the secret only if the user types that address and it is `https` (or loopback): the CLI cannot be bounced to a third host and cannot be downgraded.
- `HTTPS_PROXY` and the system proxy settings are honoured by the platform default; a proxy that terminates TLS sees the secret. That is the user's chosen network, named here, not a CLI behaviour.
- A timeout leaves the outcome unknown ([OP-19](../OPEN-PROBLEMS.md)).
