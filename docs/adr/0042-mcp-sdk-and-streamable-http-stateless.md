# 0042. MCP server: the official C# SDK, Streamable HTTP, stateless

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: P10 (interface and registration, not a hand-rolled protocol), P11 (one principal at the edge), P8 (MCP stays optional and visible), `SECURITY-REVIEW.md` §8. Builds on ADR-0012; brief §4 ("do not rely on MCP sampling"), §5.A.

## Context

Mode A needs an MCP server on the mount point T2 prepared (`Mcp:Resource`, policy `mcp-submit`). Facts checked on 2026-10-05:

- **SDK.** NuGet `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` (organisation `ModelContextProtocol`, repository `modelcontextprotocol/csharp-sdk`) are at **2.2.0** (stable since 2.0.0; 2.0.0-preview/rc before; 1.x before that). Licence **Apache-2.0** (nuspec), compatible with this repository's MIT as a dependency. It targets net8/9/10, depends on `Microsoft.Extensions.Hosting.Abstractions` and `Caching.Abstractions` only. The assembly names these protocol revisions: 2024-11-05, 2025-03-26, 2025-06-18, **2025-11-25** and **2026-07-28**.
- **Transport.** The SDK implements Streamable HTTP; since the 2026-07-28 revision (SEP-2567) its default is stateless. In stateless mode there is no `Mcp-Session-Id`, GET and DELETE are unavailable and the SDK's own documentation says server-to-client requests, "client sampling, elicitation, and roots capabilities are disabled".
- **Claude.** Anthropic's connector documentation (read 2026-10-05, [docs](https://claude.com/docs/connectors/building/index)) says Claude supports Streamable HTTP, tools, prompts and resources, follows authorization specs 2025-03-26, 2025-06-18 and 2025-11-25, and "doesn't yet support ... resource subscriptions, sampling, advanced or draft capabilities". It says every tool must declare `readOnlyHint` and `destructiveHint`.
- What was **not** verified: that a real Claude client completes initialize and a tool call against this server (see `docs/guides/connect-claude.md`).

## Decision

1. Use `ModelContextProtocol.AspNetCore` 2.2.0 (version in `Directory.Packages.props` only). Fall back would have been a hand-written JSON-RPC endpoint; not needed.
2. Streamable HTTP, **stateless** (`Stateless = true`), mapped with `MapMcp()` on the T2 route group, so authentication, the `mcp-submit` policy and the `api` rate limit apply to every method. Legacy SSE stays off (SDK default). No session state means no sticky routing, no per-session memory to bound, and no way for the server to call back into the host.
3. Sampling, elicitation and roots are not used, referenced or required. A test reads the assembly's metadata for those API names and fails if one appears (`McpSecurityTests`). This closes the "verify and record" condition of the brief for **sampling not being relied on**; OP-8 records Anthropic's statement.
4. The tool, prompt and resource types are listed explicitly (`WithTools([typeof(...)])`), never discovered by assembly scan, so a public member added later cannot become part of the contract by accident.
5. `interview-service` references the `Agent` project to publish `InterviewProtocol.Current` (and its raw JSON) as the protocol. `Agent` stays free of services; this adds the `Microsoft.Extensions.AI.Abstractions` package to the service's dependency closure and nothing runs from it.

## Consequences

- One new package family (Apache-2.0). Pre-1.0 churn is not a concern (2.2.0 stable), but the SDK is young: its behaviours that matter for privacy were tested, not assumed (ADR-0043 lists three that needed counter-measures).
- The 2026-07-28 revision is accepted by the server; whether Claude speaks it today is unknown, and the older revisions are accepted too.
- Revisit when the SDK or the protocol changes the stateless default, or when a host needs sessions for a feature we want.
