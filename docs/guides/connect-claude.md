# Connect Claude to the exit-interview MCP server (operator runbook)

For the person running the deployment. **Nothing here has been run against a real Claude client.** The token flow was verified end to end with a real authservice (T2, [ADR-0012](../adr/0012-two-jwt-schemes-and-the-mcp-resource-server.md));
the MCP transport was verified with the official SDK client in-process (T8, [`../architecture/mcp.md`](../architecture/mcp.md)). Claude itself was not available to this project.
Each step is marked **[doc]** (stated in Anthropic's documentation on 2026-10-05, link given), **[repo]** (stated by authservice or this repository), or **[not verified]**.
Anthropic's UI and policies change; trust the current page over this one.

## What you need

1. **Two public https URLs** [repo: authservice refuses an http issuer or resource]: one for authservice (its origin, no path; it becomes `Jwt:PublicBaseUrl`, the issuer of MCP tokens) and one for the MCP endpoint **with a path**, for example `https://<host>/mcp`.
   Claude connects from Anthropic's servers, not from the user's device, so both must be reachable from the public internet [doc: [Add a connector](https://claude.com/docs/connectors/custom/remote-mcp), [Authentication](https://claude.com/docs/connectors/building/authentication)];
   Anthropic documents its outbound range as `160.79.104.0/21` [doc, same page] if a firewall needs it.
   Locally this means two tunnels ([`../../scripts/README.md`](../../scripts/README.md)). The authorization server's discovery host must be reachable too, since requests to it come from the same range [doc].
2. **authservice** with RS256 signing, `Jwt__PublicBaseUrl`, `AuthorizationServer__EncryptionKey`, trusted forwarded headers behind a TLS-terminating proxy, and `min_machines_running = 1` (Claude gives discovery and token endpoints 10 s, refresh 30 s) [doc + repo: authservice [DEPLOYMENT.md](https://github.com/konradcinkusz/authservice/blob/main/docs/DEPLOYMENT.md#registering-an-mcp-client)].
3. **interview-service** configured with `Mcp__Issuer` (= authservice `Jwt:PublicBaseUrl`, exactly) and `Mcp__Resource` (= the MCP URL, exactly) [repo: [`McpOptions`](../../src/ExitInterviewAgent.InterviewService/Infrastructure/Auth/McpOptions.cs)].
   Unset, the endpoint answers 401 and `/health` lists `mcp-auth` as not configured.

## Configuration keys

| Key | Meaning |
|---|---|
| `Mcp:Issuer` | authservice's `Jwt:PublicBaseUrl`, no path |
| `Mcp:Resource` | the MCP endpoint's canonical URL with a path; the `aud` of every token; the route the MCP transport is mounted on |
| `Mcp:MetadataAddress` | where the RFC 8414 document is read; defaults to `<Jwt:Authority>/.well-known/oauth-authorization-server` |
| `Mcp:AllowedOrigins` | browser origins allowed to call the endpoint; **leave empty** for Claude (its connector sends no `Origin`). Any request with an unlisted `Origin` is refused 403 |

## 1. Register Claude as a client in authservice [repo]

Generate the two secrets (`openssl rand -hex 32`, `openssl rand -base64 32`); they are platform secrets, never in `[env]` or the repository. Then:

```
AuthorizationServer__Clients__0__ClientId=<claude-exit-interview>
AuthorizationServer__Clients__0__DisplayName=Claude
AuthorizationServer__Clients__0__ClientSecret=<secret>                 # secret
AuthorizationServer__Clients__0__RedirectUris__0=https://claude.ai/api/mcp/auth_callback
AuthorizationServer__Clients__0__AllowedScopes__0=interview:submit
AuthorizationServer__Clients__0__AllowedScopes__1=offline_access
AuthorizationServer__Clients__0__AllowedResources__0=<the MCP URL>
AuthorizationServer__Scopes__0__Name=interview:submit
AuthorizationServer__Scopes__0__Description=Submit one exit interview record on your behalf
AuthorizationServer__EncryptionKey=<key>                               # secret
```

`https://claude.ai/api/mcp/auth_callback` is the redirect URI for claude.ai, Desktop, mobile and Cowork [doc: Authentication, "Callback URLs"]. Claude Code uses a loopback redirect instead; this project supports **Claude only through the hosted apps**
([brief §3.2](../architecture/PROJECT-BRIEF.md)) and has not registered one. In local development the AppHost does this for you when `Mcp:AuthPublicBaseUrl` and `Mcp:ResourceUrl` are set (`claude-exit-interview-dev`).

Check: `curl <authservice>/.well-known/oauth-authorization-server`, and `curl <MCP origin>/.well-known/oauth-protected-resource/mcp` (the `authorization_servers` list must start with authservice: Claude uses only the first entry [doc]).
An unauthenticated `POST <MCP URL>` must answer **401** with `WWW-Authenticate: Bearer resource_metadata="…", scope="interview:submit"` [doc: Claude requires the 401; tested here].

## 2. Add the connector in Claude [doc, not verified live]

Anthropic's page ([Add a connector that isn't in the directory](https://claude.com/docs/connectors/custom/remote-mcp), read 2026-10-05) says:

- **Free, Pro, Max**: Customize → Connectors → *Add custom connector*; enter the server URL; enter OAuth credentials if needed; *Add*.
- **Team, Enterprise**: an Owner (or, on Enterprise, a member whose custom role manages libraries) goes to Organization settings → Connectors → *Add* → *Custom*, enters the URL and the OAuth client, *Add*; members then go to Customize → Connectors and click *Connect*.
- *MCP server URL*: the https address that accepts MCP requests, for example `https://mcp.example.com/mcp`.
- *Authentication*: "Sign in now" is the setting that fits this server (each user signs in through the OAuth flow).
- *OAuth client*: choose **Use your own OAuth client** and enter the client id **and the client secret** from step 1. The two other options ("Use Claude's published identity" = Client ID Metadata Document; "Register automatically" = DCR) do not work with authservice,
  which registers only confidential clients configured statically (authservice ADR 0005) [repo]. The page says to leave the secret blank unless the authorization server requires one: **authservice requires it.** Older authservice text calls the same place "Advanced settings"; the dialog layout "varies by organization" [doc].
- Authentication settings **cannot be changed after adding**; to change them, remove the connector and add it again [doc].
- Leave *Transport* alone: a URL not ending in `/sse` selects Streamable HTTP [doc].

**[not verified]**: the exact labels on your screen; that the sign-in, consent (authservice's hosted pages) and token exchange complete; that tools, the prompt and the resources then appear.

## 3. Run an interview [not verified]

Per Anthropic, users can switch connectors on and off per chat (the **+** menu → Connectors) and set a tool's permission to Blocked [doc]. The intended use: switch the connector on, ask Claude to run an exit interview with this connector;
the server's `instructions` point the model at the `conduct_exit_interview` prompt. **How Claude surfaces MCP prompts to the user (a picker, a slash command, or only to the model) is not stated on the pages read and was not verified.**
Claude may ask the user to approve each tool call [doc: "Review tool approval requests carefully"]. Expect one prompt fetch, two resource reads, validate (possibly more than once), one submit.
Claude documents ~150,000 characters as the maximum tool result size on claude.ai and 240 s per tool call [doc]; this server's results are far below both.

## 4. If it does not work

| Symptom | Likely cause |
|---|---|
| "Couldn't reach the MCP server" | the 401 lacks `resource_metadata`, or the well-known paths are not reachable from Anthropic's range; a WAF in front of authservice [doc: troubleshooting tip] |
| Authorization fails after sign-in | redirect URI not exactly `https://claude.ai/api/mcp/auth_callback`; client id/secret mismatch; `AllowedResources` differs from `Mcp:Resource` by a trailing slash or case |
| 401 on every call after sign-in | `Mcp:Issuer` differs from `Jwt:PublicBaseUrl`; `Mcp:Resource` differs from the `aud` (must be the canonical form: lowercase host, no trailing slash) |
| 403 `insufficient_scope` | client not allowed `interview:submit`, or the user did not grant it |
| 403 `ORIGIN_NOT_ALLOWED` | something is calling from a browser; keep `Mcp:AllowedOrigins` empty for Claude |
| 429 | 120 requests a minute per account on the MCP mount |
| Tool result `ALREADY_SUBMITTED` | one submission per employer per account inside the ledger window |

## What to tell users before they connect

The connect screen of the web app (T9) must say the same: Claude and its provider see the whole conversation; this service receives only the record; the receipt code is shown once and is the only way to delete the record
([threat model T-07](../security/THREAT-MODEL.md)). Do not describe the record as anonymous ([ADR-0018](../adr/0018-records-are-treated-as-personal-data.md)).
