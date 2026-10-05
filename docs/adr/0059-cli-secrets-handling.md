# 0059. CLI secrets: a type that cannot be printed, and the canary tests that prove it

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): `security-review` §5, threat model T-15 (secrets in logs), T-09; [ADR-0033](0033-provider-configuration-credentials-and-disclosure.md) (the same rule for API keys)

## Context

Two secrets pass through the CLI: the submission ticket (single use, 15 to 20 minutes) and the receipt code (the only deletion key, shown once). Neither may reach a log, a span, a metric label, an exception message, a file the user did not ask for, or the terminal except where it is meant to be shown.

## Decision

- **`RedactedSecret`** holds a secret. `ToString()`, string interpolation and the debugger view say `[redacted]`; it has no implicit string conversion and no serialisable public data beyond its length; one internal method, `Reveal()`, exists for the single place that puts it into a header (and, for the receipt code, onto the terminal once). The characters are kept in an array that `Dispose` overwrites. **Limit, stated:** a .NET string cannot be wiped, so the copy made for the header, and the process environment the user exported it into, are not erased; a crash dump or `/proc/<pid>/environ` of the same user can contain it (OP-18). The type removes accidents, not a hostile local user.
- **Shape check only:** 16 to 256 characters of the URL-safe base64 alphabet. That keeps a pasted sentence, a header-injection attempt or a whole JSON out of a request, without teaching the client the server's real format (which may change). A value that fails is an error that never repeats it.
- **No flag** carries a secret (architecture test, [ADR-0057](0057-cli-submit-and-delete-receipt-commands.md)). The prompt is not echoed (`Console.ReadKey(intercept: true)`); with no terminal the secret is read as a plain line, where there is nothing to echo to.
- **Where a secret may appear:** the receipt code on standard output, once, on success, and in the file named by `--save-receipt`. The ticket: nowhere. Nothing is logged: the CLI has no logger on these paths, telemetry export stays PII-free ([ADR-0035](0035-provider-telemetry-and-export.md)) and does not wrap submission.
- **Canary tests** drive every outcome (created, ticket refused, personal data found, rate limited, a 500 whose body echoes the canary ticket, connection refused, an address with the canary as password and query, no confirmation, unreadable ticket) and assert the canary ticket is in **no** output or file; the canary quote text is shown in the preview (that is its purpose) and in **no** error line; a canary in an exception message reaches nothing; a canary receipt is never printed twice and never reaches standard error.

## Consequences

- The tests prove the CLI's own paths. They cannot prove what the terminal, the shell, a multiplexer's scrollback or a screen recorder keep (the receipt code is on screen once by design).
- On Windows the hidden prompt and `UnixCreateMode` are not exercised here (OP-18).
