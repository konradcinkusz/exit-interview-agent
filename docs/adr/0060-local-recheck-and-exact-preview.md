# 0060. Before anything is sent: re-check the record here, and show exactly what leaves

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §5/§6 (the server does not trust the client, the client does not trust itself), `security-review` (fail closed), [ADR-0010](0010-pii-detector-deterministic-heuristics.md), [ADR-0031](0031-submission-pipeline-and-employment-verifier-seam.md)

## Context

A record file can be edited, can come from another version of this tool, or can have been produced by a model that slipped a name past the guard. The server re-checks everything, but a rejection after the ticket is typed is a worse experience, and, for personal data, a record that goes over the network and is then refused has already left the machine.

## Decision

- **The CLI re-validates with the libraries the server uses:** `RecordValidator` (schema, limits, quote rules), AI disclosure, at least one covered topic (the same notion as `Submittable`), and a personal-data scan of every quote and the free-text fields with the same `PiiDetector` (default options; the server's are configuration, so a clean local result is necessary, not sufficient). It **fails closed**: any exception in the check is a failed check. It reports codes, schema paths and **kinds**, never the text found, e.g. `looks like an email address at /topics/management/quotes/0`: the CLI can name the field, the server only names kinds.
- **What leaves is shown, in full, before the confirmation:** the destination, that it is one POST, the byte count, that the request carries the record and the ticket header and nothing else (no transcript, no name, no file names, no provider or model details), the checks that passed, the known limit (the ticket ties the request to the web account at that instant), that a receipt code comes back once, and **the record itself, verbatim** (the bytes of the file, decoded). Because the bytes sent are the bytes shown, there is no canonicalisation step that could make them differ.
- **Server rejections are mapped to plain wording** for every code the T5 service sends ([ADR-0057](0057-cli-submit-and-delete-receipt-commands.md)), with the one fact the user needs most: whether the ticket was used up (rejections that happen before the ticket is consumed say it was not; `TICKET_INVALID` says to mint a new one). `PII_DETECTED` carries kinds only and the message says so and what to do. A table-driven test covers each code; the end-to-end test against the real service pins the ones the service really produces (`TICKET_INVALID`, `ALREADY_SUBMITTED`, `INVALID_RECEIPT_CODE`).

## Consequences

- A person sees the same record the researcher will, before it leaves, and most refusals never leave the machine.
- The local detector and the server's can drift (options, versions); that is why the server re-scan stays and why the message for `PII_DETECTED` exists.
- A very long record is printed in full; the point is that nothing is hidden.
