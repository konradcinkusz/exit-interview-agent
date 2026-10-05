# 0043. MCP transport guard: Origin, protocol version, size, closed vocabulary, SDK log floor

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `SECURITY-REVIEW.md` §7 (errors never leak), §8 (deny by default); threat model T-07, T-15, T-18; brief §6 ("no PII or interview content in logs, traces").

## Context

The MCP specification asks HTTP servers to validate `Origin` (DNS rebinding) and to answer an unsupported `MCP-Protocol-Version` with 400. The SDK validates the version header only against the request body, and has no Origin check. Separately, the content-canary test (the T5 pattern, extended to the MCP path) found, on SDK 2.2.0:

1. At **Trace**, category `ModelContextProtocol.Server.McpServer` logs every outgoing JSON-RPC message in full, which for this server contains the **receipt code**.
2. The SDK writes the **method, tool name, prompt name and resource URI** a client asked for into log lines (Information when served, Error when not found) and into the tags of its `Experimental.ModelContextProtocol` metrics and spans. Those are client-chosen strings: a host steered by injected text could carry conversation content to the operator's telemetry through them. Message filters and request filters run after the SDK has already tagged and logged, so they cannot prevent it.

## Decision

A guard on the MCP path (`McpTransportGuard`, placed before CORS and authentication):

- **Origin**: any request carrying `Origin` that is not in `Mcp:AllowedOrigins` (default none) is 403 `ORIGIN_NOT_ALLOWED`. Claude's connector runs on Anthropic's servers and sends none. A bearer token on every request is the second defence (a rebinding page has no token). The offending value is never echoed.
- **Protocol version**: a header outside the five revisions the SDK speaks is 400 `UNSUPPORTED_PROTOCOL_VERSION`; absent is allowed. A test pins our list to the SDK's own.
- **Size**: bodies are read with a bound (`RecordLimits.MaxPayloadBytes` + 16 KiB for the envelope); over it is 413 before the record library sees it. The SDK's `MaxRequestBodySize` is set where the server allows it.
- **Closed vocabulary** (`McpBodyScrubber`): in the request body, any `method`, `params.name` or `params.uri` outside this server's fixed set is replaced by a constant before the SDK reads it, by rewriting those string tokens only. Every other byte, the record included, is untouched, so duplicate-key detection still works. The SDK's ordinary not-found handling then answers.
- **Log floor**: the `ModelContextProtocol` log categories are clamped to Information in code after configuration is read, replacing any rule for them, provider-specific ones included, so `Logging:LogLevel:Default=Trace` cannot expose messages.

## Consequences

- Client-chosen names never reach logs, metrics or spans (canary: method, tool, prompt, URI, argument names, Origin, header, record text). The client's `clientInfo` name and version still appear in Information logs; they are chosen by the host application, not the conversation. Recorded as residual.
- An operator who wants SDK wire logs for debugging cannot get them from configuration; they would have to change the code, which is the point.
- The scrubber parses every MCP POST once (at most about 176 KiB). The cost is not measured; the rate limiter bounds it.
- Revisit when the SDK adds redaction of these tags or logs, or when a new SDK version changes categories (the canary test fails first).
