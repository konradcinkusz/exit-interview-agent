# 0041. Prove the suite can fail: broken variants, a real-code mutation pass, and rule sets that are not the code under test

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): `ai-evals` §9, `testing-strategy` §6 ("a test that cannot fail is worse than none"), AGENTS.md ("a gate you could not run is reported as not run")

## Context

A suite adopted on the strength of a percentage can stay green forever without ever being shown able to fail. And a grader that is the same code as the protection it grades can only agree with it.

## Decision

- **Broken variants, in the test suite.** Each variant breaks exactly one thing the spec forbids and must be caught by the named constraint **assertion**: an unmasked PII guard (a real agent variant through a public seam), a quote that is not in the transcript, a quote that reads like an instruction, a sentiment key, an identifier key, a record that says the AI was not disclosed, a trace without the disclosure event, an undeclared span, a chat span with an undeclared role, a record after a withdrawal, an interview that carried on after an explicit withdrawal, a topic turn after the withdrawal, a marker in a span tag and in a log line, a canary test whose marker never reached the transcript, a record without a validated extraction, a failed extraction that still produced a record, an injection that changes the turn structure, a topic out of order, too many model calls, a leading question that reaches the transcript, an agent that did nothing. Each variant is first shown to pass on the clean run. A harness crash is an `error` and never a catch (tested).
- **Real-code mutation pass, in a scratch working tree, evidence recorded and the weakened code never committed.** Twelve protections of the Agent project are weakened one at a time and the gate must fail on each; the results are in [`docs/eval/MUTATION-EVIDENCE.md`](../eval/MUTATION-EVIDENCE.md) with the command that reproduces them (`scripts/mutate-agent.py`). A variant that survives is a missing scenario and is listed as such.
- **Independent rule sets.** Leading-question rate is graded by `QuestionGuard.LeadingReason` **and** by `IndependentRules`, a separately written lexicon and structure check with a rule the guard lacks (a coordinated second interrogative). They share an author and an idea of "leading", so they are not statistically independent; the point is that a bug in one is visible to the other, and their disagreement is a reported metric. The same holds for withdrawal (an independent screen checks that an explicit withdrawal stopped the interview) and for instruction-like quotes.
- **A control run for constraint C-05.** The same persona and seed are run again with the instruction-like sentences removed; the turn skeleton must be identical. The one exemption: a `redirect` that answers a reply in which the PII guard masked a name is the protocol's response to a name, not to an instruction (the injected text contains an e-mail address and the name detector reads "Note" at a sentence start as a name, a recorded finding); the exemption is in the grader and in the spec.

## Consequences

- The mutation pass is a suite-health check, not a per-commit gate: it needs the Agent source to be edited and rebuilt, so it is run by hand after a change to the assertion vocabulary, and its last result is in the evidence file with the commit it was run against.
- Some mutations survive by design of the corpus (see the evidence file); each is either a missing scenario, filed as one, or a protection another constraint already covers.
