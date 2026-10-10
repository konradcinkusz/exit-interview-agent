# ADR-0074: Draft tiles are generated from the record, locally, never from the transcript

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): ADR-0018 (records are personal data), the brief's "the project hosts no model" rule and its non-goals, ADR-0025 (metadata-only telemetry), ADR-0033 (provider disclosure)

## Context

After an interview the person has a record but nothing they can use directly. The owner proposed a step that turns the interview into neutral draft texts for publication, ideally run on the owner's own API key for a fee per interview.

Two facts shape the answer. First, a hosted service on the owner's key would see whole transcripts: personal data, partly of special sensitivity, which this project has deliberately never held (the server receives only the record). Second, texts meant for publication about an employer carry defamation and identification risks that the record's design (bands, masked quotes, opaque employer reference) does not carry. The cost of a real interview has never been measured, because no real model has been run.

## Decision

Build the tiles feature as a **local CLI command** (`exit-interview tiles`) that reads a validated record and uses the provider the user already chose (their key, or a local model). It never reads the transcript, never contacts the project's servers, and publishes nothing. Model output is untrusted: it is parsed against a schema and checked by a deterministic, model-free guard that drops (never edits) failing tiles. A fixed notice states that the person is responsible for what they publish and that the program verifies no facts.

The hosted, paid variant is **not** part of this decision. It needs its own threat model, data-processing terms, retention rules, payment and abuse handling, and legal review, and starts only after the real cost per interview has been measured.

## Alternatives considered

- **Hosted service on the owner's key, paid per interview:** rejected for now; it makes the owner a processor of full transcripts and removes the project's central privacy property.
- **Generate tiles from the transcript:** rejected; richer text, but it widens what the model sees and what can leak into publishable text. The record is already masked and bounded.
- **Let the model write and publish without a code-side guard:** rejected; the guard is the part that can be tested offline and kept stable across models.
- **A web-portal feature first:** deferred; the CLI is testable end to end offline and does not need a server.

## Consequences

- Users get usable texts without any new server or data flow; the only new data flow is the record to the user's own chosen provider, disclosed before it happens.
- Quality of the wording is unmeasured with real models; tests prove the guard and the plumbing on the scripted mock, not the usefulness of the text.
- The banned-term list and grounding check are lexical and will miss paraphrased accusations; the notice and the person's own review are the backstop. This is recorded as a limit, not hidden.
- Adding eval-harness constraints for tiles later changes the spec and the baseline and is deferred on purpose.
