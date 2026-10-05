# 0009. Record schema versioning: v1 is immutable, evolution is additive in a new major

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): P14 (documented contracts), brief §6; consumers in four places (CLI, ingest, MCP, eval harness) must agree on one contract

## Context

The record is produced by clients the operator does not control (a CLI binary, a user's AI client) and consumed
by several components. Stored records outlive any one release. Without an explicit policy, "just add a field" would
silently split the fleet.

## Decision

- The schema file is `schemas/exit-interview-record.v1.schema.json`, `$id`
  `urn:exit-interview-agent:schema:exit-interview-record:v1`. A record's `schemaVersion` is the major version as a
  string (`"1"`); the schema accepts exactly that constant.
- **v1 is immutable once released.** After the release gate, the file changes only for a typo in a `description`.
  Any change to accepted or rejected documents is a new major.
- **Every object is closed** (`additionalProperties: false`). A v1 validator rejects an unknown field. This is
  intended: the privacy guarantee (no identifier can ride along) depends on it.
- **Additive evolution means a new major file that is a superset.** `v2` may add optional properties, widen an enum, raise
  a cap, or add a topic as optional. Every valid v1 record stays valid under v2, so stored records need no migration. A
  v1 validator will reject a v2 record (closed objects), so deployments upgrade validators before producers.
- **Breaking means a new major that is not a superset** (remove or rename a field, narrow an enum, lower a cap, change a
  type, make an optional field required, change a semantic such as the meaning of `no_data`). It needs an ADR, a
  migration note and a deliberate deployment order.
- **Never a per-person field in any version** (ADR-0011).
- The embedded copy of the schema in the library is compiled from the published file; a test pins them byte for byte. Wire
  names of the model's enums are pinned to the schema's enums by test, so the two cannot drift.

## Consequences

- No semver minor: with closed objects a minor would be indistinguishable from a major to an old validator, so
  the policy has one axis. A pre-release schema (before the first release) may still change.
- Servers can hold several major versions side by side and dispatch on `schemaVersion`; none exists yet.
- Cost: producers are forced forward together with validators. Accepted; the fleet is small and controlled.
