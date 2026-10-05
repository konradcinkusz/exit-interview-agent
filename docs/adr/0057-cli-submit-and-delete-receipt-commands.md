# 0057. CLI: `submit`, `delete-receipt`, the integrated offer, and where the server address comes from

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §4 ("CLI login": no OAuth in the CLI, a ticket minted in the web panel), brief §5 (nothing leaves the machine but the record), [ADR-0030](0030-submission-tickets-for-the-cli.md), [ADR-0029](0029-receipt-deletion-semantics.md), `service-api-patterns` §1 (anonymous callers), `security-review` §5.

## Context

T5 built `POST /api/v1/submissions/ticketed` (anonymous, ticket in `X-Submission-Ticket`) and `DELETE /api/v1/receipts` (anonymous, `X-Receipt-Code`). T9 mints tickets on a web page. The CLI is the last piece: a person has a `record.json` from `interview --out` and a ticket, and wants to send one and be able to take it back.

## Decision

- **`exit-interview submit --record <file|-> --server <url> [--save-receipt <file>] [--yes]`.** The record is the file `interview --out` wrote, sent **byte for byte** (so what is shown is what is sent). `--record -` reads it from standard input; standard input is then taken, so `--yes` and `EXIT_INTERVIEW_TICKET` are required (it is an error otherwise, with the reason).
- **The ticket and the receipt code have no flag.** Arguments appear in process listings and shell history. Order of sources: the environment variable (`EXIT_INTERVIEW_TICKET`, `EXIT_INTERVIEW_RECEIPT_CODE`), else a hidden prompt when a terminal is attached, else one line of standard input (a pipe has nothing to hide from). An **architecture test** walks every flag of every command and fails if any is named like a secret (`ticket`, `token`, `secret`, `password`, `receipt`, `key`, `code`, `credential`) unless it only *names an environment variable* (`--api-key-env`) or is the output path `--save-receipt`. Adding `--ticket` fails it (mutation-checked).
- **A typed confirmation, always.** The CLI shows what leaves the machine and the record, then asks for the word `submit`; anything else (including `yes`, a blank line, end of input, Ctrl-C) keeps the record local (exit 3). `--yes` skips the question; it is documented as being for tests and scripts the user controls. The ticket is requested **after** the confirmation, so it is not typed or read for something the user declines.
- **`interview` offers the same step, never takes it.** When the record is valid and submittable and a server address is configured, `interview` ends with the same flow (the confirmation is the offer). There is no `--yes` there: a script cannot auto-submit an interview. A declined or failed submission does not turn a completed interview into a failed one (exit 0), except that a server refusal or a network failure of an attempted submission is reported with the submit exit code. Without a server address the interview only says how to submit. A bad address is refused **before** the interview starts.
- **`exit-interview delete-receipt --server <url>`** reads the receipt code as above and calls `DELETE /api/v1/receipts`. It prints the honest wording ADR-0029 requires: "if a record with this receipt code existed, it is deleted now", that a `204` does not confirm one existed, and that deleting does not reopen the one submission for that employer.
- **The server address has no default.** A default would put a real deployment's address in a released binary (the CLI would then post to a party nobody chose for this build), and nothing is deployed. It comes from `--server`, else `EXIT_INTERVIEW_SERVER_URL`; **not from the config file**: a config file is persistent state that another program can edit to silently redirect a submission, and the flag or variable is visible in the invocation. Rules: `https://` required, except plain `http://` to a loopback address (`localhost`, `127.0.0.0/8`, `::1`: a service on this computer, and the tests); no user info, path, query or fragment (that is where secrets leak from: shell history, logs). A refused address is never echoed back.
- **Exit codes of `submit`:** 0 submitted; 2 usage; 3 not confirmed or no ticket; 4 the record failed the local check ([ADR-0060](0060-local-recheck-and-exact-preview.md)); 5 the server refused; 6 network failure or unknown outcome; 130 cancelled.

## Consequences

- A person can go from `interview --out` to a receipt and a deletion without ever putting a secret in a command line. They do need an environment variable, a terminal or a pipe for it: three paths, all tested.
- Pasting the ticket into the terminal's visible stdin (a pipe) is not hidden: it is the person's pipe. A terminal with scrollback keeps what the CLI prints (the receipt code, shown once); see OP-23.
- `--server` has no config-file form. If that annoys, the trigger for revisiting is a deployment that publishes a stable address; the change would be a signed or pinned default, not a silent one.
