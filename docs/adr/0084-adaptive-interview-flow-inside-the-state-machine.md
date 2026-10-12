# ADR-0084: Adaptive interview flow inside the state machine

- Status: accepted
- Date: 2026-10-12
- Principle or guide served (or deviated from): ADR-0022 (the state machine decides what comes next, the model only words it), ADR-0062 (question wording protocol and guard), ADR-0075 (deepening on a serious account), ADR-0040 (baseline regeneration rule), P13 (a test at the layer with the logic), P15 (trace content is codes and counts only). Spec: [interview-dynamics-spec.md §3, §5, §6, WP5](../architecture/interview-dynamics-spec.md). Numbered 0084 because the platform-drafts work took 0083.

## Context

The interview asked the six topics in a fixed order, one example probe per vague answer, and deepened a serious account up to four times. The interviewee's own words never changed the order, and a light topic got the same follow-up as a heavy one. The research in [interview-dynamics-2026-10-12](../research/interview-dynamics-2026-10-12.md) and the spec ask for an interview that follows the person: start easy, come to what they care about first, dig where they have something to say and move on where they do not.

Protocol 1.3 (WP1) and the helpers (WP2 `TopicPlanner`, WP3 reply signals, WP4 prompts and request fields) are merged. Their output is not yet used by the machine or the runner. WP5 wires them in.

Facts checked while doing it:
- `TopicPlanner.Plan` re-ranks on every call. Its fallback (no topic has salience) makes "the first two remaining topics core" true at each call, so a plan recomputed after each topic would make every topic core. The machine therefore fixes the fallback core set once, at the first topic transition.
- `ReplySignals` had no concrete-cue flag. Rules 4 and 5 of the follow-up policy need one ("a substantive answer with no concrete cue"). The flag is added as a trailing optional field, so existing callers are unchanged.
- The budget (`maxInterviewerTurns`) and the deepening limit (`maxDeepProbesPerTopic`) are read from the protocol file. Protocol 1.3 sets them to 40 and 3 (spec §3.4, §3.5); the 1.2 values were 60 and 4.

## Decision

1. **The flow is the spec's §3.1, inside `InterviewMachine`.** The warm-up (topic none), then the reason for leaving, then the other five topics in the planner's order: core topics get the follow-up policy, light topics one scale question (and a scale follow-up on 1, 2 or 5). Then retention, then anything else, then close with `AllTopicsCovered`. Each step carries its `Move`, and a `Callback` flag when the topic was mentioned before.
2. **The planner's order is used, with two rules of our own.** Core topics are capped at `maxCoreTopics` over the interview, not per call. In the fallback (no salience anywhere) the two core topics are the first two in protocol order, fixed at the first transition. Once any topic has salience, the planner's depths are used.
3. **The follow-up policy is §3.4, rules 1–6, as written.** Rule 1 (already said) is checked after the safety branches. Rule 5 uses `Elaborate` where the reply carries a polarity cue and `Consequence` otherwise, because the spec's "no consequence cue" has no signal in this codebase. No move is asked twice on a topic. Ladders and consequences stop at `targetQuestions`.
4. **The length budget is §3.5.** At `maxQuestions − 2` the machine goes to retention, anything else and close. The check-in is said once at the first topic transition after `checkInAfterQuestions`, unless the person was terse twice.
5. **The safety branches are §3.7.** Distress releases the topic at once with the `support` preface, and the next topic is light. A legal remark adds the `legalNotice` preface once and the flow continues. An exit event is an ordinary reply and never opens the deepening. A serious account deepens on a topic, not on the warm-up or on anything else. Retention is answered without deepening.
6. **The runner does three things the machine cannot.** It feeds each masked reply to the planner before the machine decides (sentences flagged as instructions are skipped). It holds the register in memory, set by the consent reply and by any later explicit register, for Polish only, and renders it into fixed texts. It asks the guard's move shape (`Scale`, `Follow`, `Default`) and retries a question the guard rejected once, with the reason code. A second rejection falls back to the protocol wording and counts in `QuestionsRejected`.
7. **Trace.** `interview.turn.move`, `interview.turn.preface` and `interview.question.retried` are added (§5.2). `support` and `legalNotice` share the code `notice`, so a trace cannot tell distress from a legal remark. The register is never traced.

## Consequences

- Easier: the interview's shape is decided by code and tested per branch; a rejected question costs one more call, not a different interview; a light topic gets one question, not a probe.
- Harder: the machine has more state (the planner, the fallback core set, the pending check-in and legal notices). Its tests are the record of the rules and are long.
- Deviations from the spec text, each deliberate:
  - The ADR is 0084, not 0083 (taken).
  - `ReplySignals.Concrete` is added (WP3's file, one trailing optional field).
  - The fallback core set is fixed at the first transition (the planner re-ranks every call; see Context).
  - Retention and anything else get no deepening (the spec is silent).
  - Rule 5 uses the polarity cue alone to choose `Elaborate` (see Decision 3).
- Not done here, and dependent on WP6: the Layer 1 metrics Q-01 to Q-13, the personas `pay-first-pl` and `distress-pl`, the scale responses in personas, and the new scenarios that need them. Q-01..Q-13 are therefore NOT RUN in this change. The baseline moves once, in WP5, with the justification ADR-0040 requires; the parent regenerates `evals/baseline.json` on this branch.
- Revisit when W9 (a real model) shows that the lexical cue lists miss what people say; then the planner and the distress signal need a measured revision (spec §9).
