# 0061. CLI receipt handling: shown once, an optional private file, honest deletion

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): [ADR-0029](0029-receipt-deletion-semantics.md), threat model T-11, [OP-12](../OPEN-PROBLEMS.md)

## Context

The receipt code is a bearer secret with no account link: the only way to delete a record, impossible to recover, and valuable to anyone who finds it.

## Decision

- **Shown once**, with the words "this is the only way to delete this record; the service stores only a fingerprint, cannot find or re-issue it, and does not show it again", that anyone holding it can delete the record and that it does not identify the person, and the **exact deletion command** (`exit-interview delete-receipt --server <origin>`; the code itself is never part of a command line).
- **`--save-receipt <file>` is optional and never overwrites.** The file is created with `FileMode.CreateNew` and, on Unix, mode `0600` **at creation** (never world-readable, even for an instant); it holds the code and a newline, so `delete-receipt < file` works. The path is checked **before anything is sent** (existing file, a directory, a missing folder are usage errors): a receipt shown once must not be lost to an unwritable path. If the write still fails, the code is on the terminal and the CLI says to copy it now. The CLI prints where the file is and that whoever can read it can delete the record. Not encrypted (OP-12 leaves encrypted receipt stores as a later step).
- **`delete-receipt` words the answer as ADR-0029 requires**: "if a record with this receipt code existed, it is deleted now", that the same answer is given for every well-formed code, and that deleting does not reopen the one submission per employer (the ledger is not touched). A malformed code (`400 INVALID_RECEIPT_CODE`) is a visible error, not a false "deleted".

## Consequences

- A person who does nothing keeps nothing: no file, no history, only what the terminal's scrollback holds. That is the trade-off between "no secret on disk unless asked" and "a lost code cannot be recovered"; the message makes the choice visible.
- Windows: `UnixCreateMode` does not apply; the file inherits the folder's ACL. The CLI says "readable only by you *where the system supports it*" (OP-23).
