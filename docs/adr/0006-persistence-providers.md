# 0006. Persistence providers: PostgreSQL and InMemory only

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: **deviates from** P4 (provider-portable: PostgreSQL | SqlServer, InMemory fallback).

## Context

P4's switch names PostgreSQL and SqlServer. Nothing in the brief, the topology (Fly Postgres) or
authservice's deployment calls for SQL Server, and a second provider doubles the migration sets that must
be kept in step.

## Decision

`AddDatabaseContext<T>` supports `PostgreSQL` (with a connection string) and falls back to InMemory
(tests, a fresh clone with no container). Migrations are Npgsql migrations in
`Persistence/Migrations` of the owning service, applied by `MigrateAsync` from a hosted service after the
listener is up; `EnsureCreated` is used only on the InMemory path.

## Consequences

Adding SqlServer later means a provider branch in the kernel's `DatabaseProviderExtensions` and a second
migrations assembly per service; authservice's `*.Migrations.PostgreSQL` / `*.Migrations.SqlServer` split
is the model. Trigger: a real requirement for SQL Server, which none of the current tasks has.
