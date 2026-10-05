# 0008. Validation library: one JSON-Schema package, code-only errors, quote fidelity

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): `security-review` §7 (errors and output), `service-api-patterns` §3 (validation), brief §6 (every client record is untrusted input), P2 (kernel stays plumbing)

## Context

`ExitInterviewAgent.Records` validates records that arrive from clients and must never leak what a client
submitted: a rejected record can contain personal data in any string, including in a property *name*.
It needs a JSON Schema (draft 2020-12) validator, and the brief allows BCL plus one verified, justified package.

Candidates, checked on NuGet on 2026-10-05:

| Package | Version | Licence (from the nuspec) | Observation |
|---|---|---|---|
| `JsonSchema.Net` | 9.4.0 | `OSMFEULA.txt`, `requireLicenseAcceptance=true` | A maintenance-fee EULA, not an OSI licence. Rejected for an MIT repository. |
| `Corvus.Json.Validator` | 5.7.5 | Apache-2.0 | Pulls Roslyn (`Microsoft.CodeAnalysis.CSharp`) and a code-generation stack into a runtime validation path. Rejected as disproportionate. |
| `LateApexEarlySpeed.Json.Schema` | 4.2.0 | BSD-3-Clause | Draft 2020-12, built on System.Text.Json; its package description states it passes the official JSON Schema Test Suite (not independently re-run here). **Chosen.** |

## Decision

1. **Package.** `LateApexEarlySpeed.Json.Schema` 4.2.0, version in `Directory.Packages.props`. It is wrapped by one
   class (`RecordValidator`), so replacing it touches one file. Cost, stated plainly: it brings 11 transitive
   packages, mostly `Microsoft.Extensions.*` 3.1.0 through `Microsoft.Extensions.Http`. NuGet audit
   (`NuGetAudit` with level `low`, transitives included) reports nothing for them today.
   What was verified here is behaviour, not the package's test-suite claim: golden valid/invalid fixtures, injection-style
   quotes and a shared-instance concurrency test.
2. **Settings.** `GenerateErrorMessages = false` (library messages can echo values), a regex timeout
   (`RecordLimits.RegexTimeout`, default 100 ms, a ReDoS guard), format assertion off, all errors listed.
3. **Errors are a stable code plus a path, never text.** Codes are in `RecordErrorCodes` (renaming or reusing one is
   a breaking change). A path contains only property names the schema defines and array indexes; any other
   name a client submitted is replaced by `*`. The library reports `additionalProperties` failures at the offending
   key, so the validator reports the **parent** path instead. Failures of an `if` condition (which only select the
   `then` or `else` branch) are not errors and are dropped.
4. **Limits before the schema.** Payload size (default 160 KiB: the worst valid record, 30 quotes of 400 code points each escaped as a surrogate pair, is about 144 kB; a test builds it), nesting depth
   (default 8, a valid record nests 5), duplicate property names rejected (`AllowDuplicateProperties = false`,
   .NET 10), then the schema. Each has its own code.
5. **Canonical serialization.** Same record, same bytes: UTF-8, no whitespace, schema property order, optional bands omitted,
   default System.Text.Json escaping (so non-ASCII is `\uXXXX` and the form does not depend on the consumer's encoder).
6. **Quote fidelity.** `QuoteVerifier.VerifyQuotes(transcript, record)`: each quote must be an ordinal substring of the
   transcript after one documented normalization (collapse every run of Unicode white space to one space, trim). Case,
   punctuation, diacritics and Unicode composition are *not* normalized, so a paraphrase or "looks the same" quote
   fails. It reports topic and quote index only. It must be run against the same **masked** transcript the extractor saw.
7. **Project placement.** `Records` is its own library, not `Contracts` (which is dependency-free DTOs) and not the
   kernel (P2: plumbing only). It is a domain contract with logic, shared by the CLI, the ingest service, the MCP
   adapter and the eval harness. Architecture tests pin its references to the framework and the validator.

## Consequences

- An attacker-controlled key such as an email address used as a property name cannot reach a log through an error.
- Unicode composition differences between transcript and quote cause a false "not verbatim" (fail-closed). Revisit
  if measured on real model output; the fix would be NFC on both sides, not loosening the check.
- Trigger to revisit the package: a licence change, an unpatched advisory in its transitives, or a quirk that the
  golden tests cannot absorb.
