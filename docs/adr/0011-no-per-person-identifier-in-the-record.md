# 0011. No per-person stable identifier anywhere in the record or its schema

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §3.1 and §6 (account has no link to the record), `metric-ethics` §1 (an anti-goal that only exists as prose is a request; one the architecture cannot express is a rule)

## Context

The system's privacy promise is that a submitted record cannot be tied to the account that submitted it, except
through the separately-keyed submission ledger, which holds no content. That promise fails the moment any field in
the record is, or can be joined to, a per-person identifier.

## Decision

- The record contains **no** user id, account id, email, IP address, name, device or session identifier, timestamp,
  or any value derived from one. The only identifier is `interviewId`: 128 random bits from the operating-system
  CSPRNG (`InterviewId.NewRandom`), "not derived from anything". The type has no constructor that takes an input
  other than the id's own text.
- **Enforced by architecture tests**, which fail the build if any of these appear:
  1. a schema property name that, split into words (camelCase, snake_case), contains an identifier-shaped word
     (user, account, email, ip, name, device, session, token, timestamp, hash, ...) or is `id` itself, other than
     `interviewId`;
  2. an object schema that is not closed (`additionalProperties: false`), because an open object can carry anything;
  3. a context field that is not an enum (so no free-text context);
  4. a public property or constructor parameter of the model with such a name;
  5. a key in a golden valid record that the schema does not define.
  The guard's own test feeds it known-bad names (`userId`, `account_id`, `ipAddress`, `createdAt`, ...) and requires it to flag
  them, so the check cannot pass vacuously; it was also exercised once against the real schema with an `accountId`
  property added, and failed as intended.
- The word list is a list of names, so it can be evaded by a field called `x`. The closed-object rule and review are the
  other layers; the test is a tripwire against the common mistake, not a proof.

## Consequences

- Features that want to know "who" (rate limits, one-per-employer, deletion by receipt) live outside the record:
  the ledger, receipt-code hash and submission ticket of T5. The record schema never needs to change for them.
- Adding a legitimate field whose name trips the word list is allowed by editing the list in the test, in a pull request
  that says why; that is the point of making it visible.
