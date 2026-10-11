# 0079. Local stack: pgAdmin, interview configuration, development credit grant

- Status: accepted
- Date: 2026-10-11
- Principle or guide served (or deviated from): P1 (the AppHost is the composition root and exists for development), P5 (configuration through the environment; secrets as parameters), P8 (optional dependencies degrade and are visible); `SECURITY-REVIEW` §8 (deny by default); deviates from the rule of ADR-0077 that credits come only from a verified payment event (see the register)

## Context

The development stack ran Postgres, authservice, interview-service and the web app, but three things could not be exercised locally:

1. **No database browser.** Inspecting the schema or the ledger meant a `psql` session into a container.
2. **No interview model.** The AppHost passed interview-service only its database and its JWT settings. With `Interviews:Provider` empty the service answers 503 on every session start, so the web interview could not run at all.
3. **No way to practise the paid path.** Credits come only from a verified payment event (ADR-0077). The stack ran with `Interviews:RequireCredit=false`, so the credit gate was never exercised, and the checkout and webhook had no local way to add credits.

The payment provider's fake (ADR-0077) already runs the production webhook path with no network and no money, so the missing piece is configuration and one development route.

Facts checked: the Aspire.Hosting.PostgreSQL 13.6.0 package exposes `WithPgAdmin` and `WithHostPort` for the pgAdmin container; the publish manifest of the AppHost (`--publisher manifest`) lists every resource and environment variable and needs no container runtime. A run-mode start was attempted in this environment and stopped in the orchestrator's container watch, because the container runtime was unhealthy here (recorded as not run below).

## Decision

**pgAdmin (AppHost only).** `postgres.WithPgAdmin(...)` on port 5050 with the password from a generated Aspire secret parameter (`pgadmin-password`). Aspire registers the postgres server in pgAdmin automatically. It is part of the development composition root and is never in a Fly manifest. The port is fixed so the address is the same on every run.

**Interview model.** `interviews-provider` defaults to `anthropic` when the optional secret `anthropic-api-key` is set and to `mock` otherwise. `interviews-model` defaults to the placeholder `scripted` for the mock, which ignores it, and has no default for a real provider, because the project hardcodes no model identifier. The key reaches interview-service only as an environment variable whose name is given by `Interviews__ApiKeyEnv` (the key is never in configuration). `ANTHROPIC_WORKSPACE_ID` is an optional, non-secret parameter.

**Visibility of the model (P8).** A row `interview-model` in `/health` and in the startup banner: DEGRADED while the scripted mock runs, with a message that says it is a mock; configured when a real provider resolves; with the reason otherwise. The row uses the same resolver as the model factory, so it cannot disagree with the model that runs. It never reports a key.

**Development payments.** `Billing__Provider=fake`, a price of 1000 and currency `pln` as parameters (development values, set only in the AppHost; the service has no default price), the success and cancel addresses at the fixed web address, and a generated webhook secret (`billing-fake-webhook-secret`). The web app's port is fixed at 3000 so the checkout returns the payer to a known address.

**Credit gate.** `Interviews__RequireCredit` is a parameter (`interviews-require-credit`, default `false`). Set it to `true` to exercise the gate.

**Development credit grant.** `POST /api/v1/dev/grant-credits {quantity}` (1 to 10) for an ordinary account. The route is mapped only when the environment is Development AND `Billing:Provider` is `fake` AND checkout is configured (one predicate, `DevStack.CreditGrantEnabled`). In every other case the route does not exist and answers 404. The grant records a purchase under an event id beginning `dev-grant:`, so it is visibly not a provider event. The `dev-credit-grant` row in `/health` reports the state; it is configured when the grant is off by design, because an absent development route is not a degradation.

**Verified email.** The AppHost sets `Interviews__RequireVerifiedEmail=false` explicitly, in addition to `appsettings.Development.json`, because authservice does not yet issue the `email_verified` claim (task L4). A test pins that the development host sees `false`.

**Secrets.** Every generated or user-supplied value is an Aspire parameter: from user-secrets (`Parameters:<name>`, written with `dotnet user-secrets set`) or generated for the run. None is a literal in source or in a committed file. The names and the commands are listed in `secrets.env.example`.

## Consequences

- Locally: pgAdmin on port 5050; the interview runs on the scripted mock with no key; a real model is one user-secret away; the paid path can be practised with the grant and `RequireCredit=true`.
- The development grant is a second way to create credits. It is the only one, and it is compiled in but unmounted outside Development with the fake provider. Its test (`DevGrantEndpointTests`) fails if the route is mounted in Production or without the fake provider (verified by a mutation: forcing the gate on turns nine tests red).
- The mock is a scripted conversation, not a model. The row says so; no quality claim may rest on a session run on it (AI-EVALS applies to real models only).
- The fixed web port 3000 and the pgAdmin port 5050 can collide with other local tools. The AppHost then fails to bind; the fix is to stop the other tool.
- The AppHost's run mode was not exercised here (the container runtime was unhealthy). The manifest check covers the resource graph and the variables. A developer's run with a working Docker is the check that the container starts (task L3 documents the local run).
- Trigger to revisit: a real payment provider in development (the grant then has no purpose), or a second environment that needs the grant (never: it is development only by rule).
