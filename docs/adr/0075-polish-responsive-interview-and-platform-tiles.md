# ADR-0075: Polish, a responsive interviewer, and platform tiles from the transcript

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): ADR-0022 (the state machine decides, the model words), ADR-0062 (question wording protocol), ADR-0074 (tiles from the record; this ADR amends two points), ADR-0018 (records are personal data), ADR-0040 (baseline regeneration rule)

## Context

The first real use of the CLI, with a real model, gave three findings: the interview is English only, the interviewer does not react to what is said (one probe per topic, fixed acknowledgements, an instruction not to comment on replies), and the tiles are generic. The owner asked for Polish and any language the interviewee chooses, for deep questioning when someone reports something serious such as bullying, and for tiles in the shape of Glassdoor, Google and Reddit posts, generated at the end of every interview.

## Decision

1. **Language.** A Polish protocol, `--language pl|en|auto`, and a switch to the language the interviewee writes in or asks for. Cue lists and the question guard get Polish rules.
2. **Deepening.** A deterministic serious-account cue starts a deepening phase on that topic: up to four neutral, fact-seeking follow-ups from a fixed menu, an optional one-sentence reflection of the interviewee's own words, and a one-time reminder that they may skip or stop. The state machine stays in control; the model only words the questions. Protocol 1.2.
3. **Tiles.** Three platform kinds (Glassdoor, GoogleReview, Reddit) are added and `interview` produces tiles at its end by default, with the provider the user already chose.
4. **Amendment to ADR-0074 (input).** In the same session the writer may also receive the PII-masked transcript. ADR-0074 limited input to the record chiefly to keep a hosted service from holding transcripts; with the local CLI and the user's own provider the transcript has already gone to that provider, and a long narrative cannot be written from the record's short quotes. Run from a saved record, input is the record only.
5. **Amendment to ADR-0074 (banned terms).** The single list becomes tiered: always banned (crime and illegality asserted as fact, health, protected characteristics of an identifiable person); allowed only in the Reddit kind, only when the interviewee used the word and the sentence is framed as their own experience (bullying, mobbing, harassment, discrimination); not allowed in the short public kinds. The company name is never filled in.

## Alternatives considered

- **Let the model decide when and how deep to probe:** rejected; it removes the part of the system the evaluation harness can prove (limits, no names, consent, withdrawal) and invites leading questions.
- **Keep one probe, widen the acknowledgement text only:** rejected; the owner's case (a report of bullying) needs real follow-up, not only warmer wording.
- **Hard-ban the experience terms everywhere:** rejected for Reddit-style narrative, which is the point of that tile; kept for the short kinds.
- **Tiles from the record only, longer prompts:** rejected; five masked quotes per topic cannot carry a detailed account.
- **Separate Polish product:** rejected; one protocol per language under one state machine keeps the evaluation shared.

## Consequences

- A Polish-speaking user gets a Polish interview and Polish tiles; the interview reacts to serious accounts instead of moving on.
- The protocol version, limits and evaluation baseline change; the baseline is regenerated with a justification (ADR-0040) and the mutation proof is re-run.
- The cue lists are lexical and Polish inflection coverage is partial; the deepening can miss a paraphrase and can fire on a term used in a harmless sense. Recorded as limits.
- A tile that describes mobbing or harassment, even framed as the author's experience, can create legal exposure for the person who publishes it. The notice says so; the program cannot remove the risk, and the quality of such text with a real model is unmeasured.
- A transcript-based tile can carry more identifying detail than the record. It is built from the PII-masked transcript, passes the same guard, and is shown to the user before anything is used.

## Implementation notes

Added at implementation (Y5). They describe what the code does; the Decision above is unchanged.

**Where it lives**

- Language: `Agent/Protocol/interview-protocol.pl.v1.json` next to `interview-protocol.v1.json`; `InterviewProtocol.For(language)`; `Cli/InterviewCommand.cs` (`--language`, `ResolveLanguage`: `auto` follows the UI culture). The mid-interview switch is in `Agent/Machine/` and `Agent/Runner/` (Y4, PR #37).
- Serious-account signal and deepening: `Agent/Machine/ReplySignals.cs` (`Serious`, `DeepCovered` as a bit mask over `DeepFocus`); `Agent/Machine/InterviewMachine.cs` (the deepening phase, `DeepProbesUsed`, the once-only skip reminder). Cue lists are lexical, Polish and English.
- Question guard and prompts: `Agent/Roles/QuestionGuard.cs` (Polish rules), `Agent/Roles/Prompts.cs` (reflection rule, language lines).
- Tiles: `Agent/Tiles/TileContracts.cs` (kinds `Glassdoor`, `GoogleReview`, `Reddit`; per-kind limits), `TileGuard.cs` (tiered banned terms, the drop codes below), `TileWriter.cs` (4000 output tokens), `TileWriterOutput.cs` (64 KiB input cap), `TilePrompts.cs`. Rendering: `Cli/Tiles/TileRenderer.cs`. Automatic tiles at the end of `interview`: `Cli/InterviewCommand.cs` (`AutoTilesAsync`, `--no-tiles`, `CheckOutputFolder`).
- Evaluation: `Eval/Layer1/*`, `evals/scenarios/` (three deepening scenarios: `hap-003` and `con-003` in Polish, `hap-004` in English), `evals/baseline.json`.

**Protocol 1.2 limits** (`limits` block, both protocol files; values before and after)

| Limit | 1.1 | 1.2 |
|---|---|---|
| `maxDeepProbesPerTopic` | none | 4 |
| `maxInterviewerTurns` | 40 | 60 |
| `maxModelCalls` | 60 | 90 |
| `maxEstimatedTokens` | 60000 | 90000 |
| `maxReplyChars` | 2000 | 2000 |

**Rejection codes.** A tile the guard does not pass is dropped and counted, never edited. The codes (`TileDropReason`, shown in the notice as `Odrzucone: N (kody: …)`): `schema_invalid`, `pii_found`, `ungrounded`, `copied_quote`, `banned_term`, `too_long`, `empty`. A repeated kind is dropped as `schema_invalid`. A writer failure is reported as `schema_invalid` with no message text.

**Tile length limits** (design limits for drafts, in characters): short note 280, Glassdoor 900, Google review 500, Reddit 4500; generic text 600; title 60. The notice says that platform rules change and must be checked.

**Eval.** The spec moved to 1.1.0. The baseline was regenerated on 2026-10-10 with a justification (ADR-0040; `scripts/check-baseline-justification.sh`). Mean tokens per interview rose from 8407.3 to 9825.8 (+16.9 %, outside the 10 % tolerance); the justification records why. The mutation pass is recorded in [interview-v2.md](../architecture/interview-v2.md#what-was-measured-and-what-was-not).

**Not measured.** Whether a real model asks good deepening questions, reflects the interviewee's words without judgement, or writes Polish or Reddit-style text that passes the guard as intended. The mock and the guard tests cannot show this.
