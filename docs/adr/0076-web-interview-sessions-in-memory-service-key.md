# 0076. Web interview sessions: in memory, service key

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): P3 (database per service: deviated for transcripts, see Consequences), P5 (configuration via environment), P8 (optional dependencies degrade and are visible), P11 (anti-corruption at the edge), `SERVICE-API-PATTERNS` (endpoint triad, named operations, RFC 9457 problems with stable codes), `SECURITY-REVIEW` (deny by default, ownership at the resource, random ids, no text in logs), `TESTING-STRATEGY` (test at the layer with the logic, InMemory, no network), `IDENTITY-AND-ACCOUNTS` (the account is the only owner key; no enumeration)

## Context

The web app (`docs/architecture/web-app-plan.md`, sections 2, 3 and 10) lets a person who will not use a terminal talk to the interviewer in a browser. The interview logic is the `ExitInterviewAgent.Agent` library. The plan fixes the session API (section 10) and says the transcript lives in server memory only and is dropped when the interview ends or after an idle timeout. The plan also says the service runs the model with the service's own key, held server-side.

The facts checked for this decision:

- `ExitInterviewAgent.Agent` runs an interview from an `IInterviewee`; `InterviewRunner` needs nothing but that interface and an `IChatClient` (checked in `Runner/InterviewRunner.cs`).
- The provider catalog and the resolver read keys from environment variables only (`Providers/ProviderConfigResolver.cs`); the CLI's user config file is not needed by the service.
- A non-fatal model failure makes the agent continue on fallback wording; only a fatal one stops the interview (`Roles/MeteredChatClient.cs`, ADR-0034). The service does not change that.
- The legal notice printed under the tiles lives in the CLI project (`Cli/Tiles/TileRenderer.cs`, internal). The service repeats it; a test keeps the two equal.

## Decision

1. **Sessions are held in process memory** (`InterviewSessionStore`), keyed by a random 256-bit id (43 base64url characters, from `RandomNumberGenerator`). The id is bound to the account (`sub`). Another account gets 404.
2. **One open session per account.** A non-terminal session blocks a second start with 409 `interview_in_progress`. A finished session does not block.
3. **Idle timeout 30 minutes** from the last request. **A finished session stays readable 30 minutes** after it ended, then it is wiped. Expired sessions answer 410 `gone` for a further 30 minutes (a tombstone holding the id and owner only), then 404.
4. **Credits are behind two seams**: `ICreditGate` (consumed when a session starts) and `ICreditRefund` (called once when a session ends as `failed`). The default gate allows everything. With `Interviews:RequireCredit` true it refuses everything (402), so an unwired payments ledger cannot leak free interviews. W3 replaces both.
5. **Provider configuration** is `Interviews:Provider` and `Interviews:Model` (plus optional `BaseUrl` and `ApiKeyEnv`, which name a variable). The key is read from the environment by the CLI's resolver. Without a provider, a start answers 503 `interviews_disabled` before any credit is consumed. The `mock` provider uses the scripted model, for development and tests.
6. **The emergency switch** `Interviews:Enabled` (false) refuses new sessions with 503 `interviews_disabled`. It is read on every start.
7. **The interview runs on a background task** per session, through `ChannelInterviewee`: each interviewer turn is published as an event, and the task waits on a bounded reply channel. A reply waits up to 60 seconds for the next turn. A timeout fails the session (503 `provider_unavailable`, credit refunded).
8. **The closing turn is shown at once.** The tiles are generated after it, so a closing reply does not wait for them. The status becomes `completed` (or `stopped`) when the tiles are ready, and the result is readable then.
9. **Logs, spans and metrics carry no interview text.** The service logs exception type names and enum names only. A test (`No_interview_text_reaches_the_logs_or_the_spans`) plants a canary in a reply and asserts that it reaches no log line and no span. A mutation check showed the test fails when a reply is logged.
10. **Contract changes** relative to plan section 10 are recorded in the plan in the same pull request: a `400 invalid_request` on start, a `409 reply_in_progress` on reply, the stable `not_found` code, and the closing-turn behaviour in item 8.

## Consequences

- **Easier:** the session rules are testable on InMemory with a fake clock. The endpoints are thin; the rules live in `InterviewSessions`.
- **Harder:** a restart loses every open session. The store does not persist ids, so after a restart the id is unknown and the answer is 404, not the 410 the plan first described. Credits for those sessions are returned by W3, which records session ids in the ledger. This is a known gap, recorded in the plan.
- **Deviation from P3:** interview text (the transcript and the result) lives in one process, not in `interviewdb`. This is the plan's decision (section 3), made to keep text out of the database. The register in `00-ARCHITECTURE.md` carries the row.
- **A second replica cannot see another replica's sessions.** The Fly deployment (W10) must run one instance, or move sessions to shared state. This is the same limit as the existing in-process limiters (threat model T-18).
- **The service references `ExitInterviewAgent.Providers`** and builds the platform model client itself (`ConfiguredInterviewModelFactory`), so the key is read by the service from its environment, never by the browser or the BFF. The plan (section 3) names this; the Providers project brings the Anthropic SDK into this service's build. This amends the rule in `ProviderArchitectureTests` (formerly "only the CLI and the eval harness reference Providers"): the test now allows `ExitInterviewAgent.InterviewService` and is renamed accordingly. Use of Providers here is kept minimal: the resolver and the client factory, no logging of settings or keys, and the Dockerfile copies the project.
- **The notice text is duplicated** (`InterviewNotice`, equal to the CLI's by a test). The clean fix moves it into the agent library; that is a later change to the CLI.
- **The employer is not asked** in the web interview. The record carries the placeholder `employer-not-stated`. Submission, when it comes, states a real one.
- **Transient provider errors do not end a session** (ADR-0034). A flaky provider can give the person fallback wording rather than a 503. Only a fatal failure ends the session as `failed`.
- **Trigger for revisiting:** a second replica, a restart policy that must keep sessions, or the first real-model run (W9), whose timeouts decide whether 60 seconds is enough.
