# 0012. Two JWT schemes in interview-service: web tokens and MCP tokens

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: P5 (one signing key, held by authservice; everything else validates), P8 (optional
  integrations degrade and are visible), `IDENTITY-AND-ACCOUNTS.md` §1, §10, `SECURITY-REVIEW.md` (token validation),
  `SHARED-SERVICE-REUSE.md`. Builds on ADR-0003; brief §4 "MCP path".

## Context

Claude reaches this system through an MCP endpoint and signs the user in through authservice's OAuth 2.1
authorization server (authservice ADR 0005). Its tokens are not the tokens the web portal uses. Facts below were read in
authservice's source at commit `cccf978` (2026-10-03) and then **observed** against that source built and run locally:
a real authorization-code + PKCE flow with the Claude redirect URI, using a confidential client. The pinned tag `v0.3.4`
(`dfcce91`) differs from that commit only in frontend-URL/e-mail handling, external-provider redirects, the cleanup services and
small Admin/Program/csproj edits (`git diff v0.3.4 cccf978 --stat -- src`: 12 files, none of them the authorization server, `TokenService`, consents,
refresh or account deletion), so the behaviour recorded here holds for the pin.

What an MCP access token is (authservice `docs/decisions/0005-mcp-authorization-server.md` A9, confirmed by decoding a
real token):

| Part | Value |
|---|---|
| header | `alg: RS256`, `kid`: the JWKS key's thumbprint, `typ: at+jwt` |
| `iss` | `Jwt:PublicBaseUrl` of authservice, no trailing slash. It must be an **https** URL (startup refuses otherwise) |
| `aud` | one string: the resource the client asked for, which must be in the client's `AllowedResources` (https, no fragment) |
| `sub` | `ApplicationUser.Id`: **the same id the web token carries**, so one account has one `sub` on both paths |
| `client_id`, `scope` | `scope` is one space-delimited string, e.g. `interview:submit offline_access` |
| also | `jti`, `iat`, `exp` (15 minutes by default) and the claims web tokens carry: `email`, `…/nameidentifier`, `…/name`, roles, `organization` |

A web token differs in `iss` (`Jwt:Issuer`, here `ExitInterviewAgent`), `aud` (`Jwt:Audience`), `typ` (`JWT`) and has no
`client_id`/`scope`. **Both families are signed with the same key and published in the same JWKS**, so a signature check
alone cannot tell them apart. authservice refuses to start if `Jwt:Issuer` equals the MCP issuer and `Jwt:Audience`
equals a resource; this service refuses the other direction by validating issuer and audience exactly.

## Decision

`interview-service` registers two JwtBearer schemes and two policies. Endpoints name a **policy**, never a scheme.

| | Web / BFF | MCP |
|---|---|---|
| scheme (`AuthSchemes`) | `Bearer` (default; registered by the kernel) | `McpBearer` |
| policy (`AuthPolicies`) | `account` | `mcp-submit` |
| issuer | `Jwt:Issuer` | `Mcp:Issuer`, compared as one exact string (a delegate, so the discovery document's issuer is not added to the accepted set) |
| audience | `Jwt:Audience` | `Mcp:Resource`, canonical form, compared exactly (trailing-slash tolerance switched off) |
| other checks | RS256 only, lifetime | RS256 only, lifetime, `typ` must be `at+jwt`, `sub` present |
| scope | none | **enforced here**: `interview:submit`, case-sensitive token match on the space-delimited `scope` |
| keys | JWKS of `Jwt:Authority` | RFC 8414 metadata of `Mcp:MetadataAddress` (default: `Jwt:Authority` + `/.well-known/oauth-authorization-server`), whose `jwks_uri` is the same JWKS |

- **Scopes.** `interview:submit` (the only one this resource defines; T8's submit tool requires it) and `offline_access`.
  `offline_access` is not enforced here: authservice requires it on every client so Claude is issued a refresh token. It
  is listed in `scopes_supported` because Claude asks for what is advertised; whether Claude requests it from our
  document or from authservice's own metadata (which also lists it) could not be tested without Claude.
- **Protected-resource metadata (RFC 9728)** is public at `/.well-known/oauth-protected-resource` and at the path-suffixed
  `/.well-known/oauth-protected-resource/<resource path>`. `authorization_servers` has authservice **first** (Claude uses
  the first entry and does not fall back). Every value comes from configuration, never from the request's `Host`.
- **Challenges.** 401: `WWW-Authenticate: Bearer [error="invalid_token",] resource_metadata="…", scope="interview:submit"`
  (no `error` when no credentials were presented; the failure detail is never echoed). 403 for a valid token without the
  scope: `error="insufficient_scope"`.
- **Mount point.** `Mcp:Resource`'s path (for example `/mcp`) is a route group requiring `mcp-submit` and the API rate
  limit. T8 maps the MCP transport on it. A `GET /mcp/_probe` exists **in Development only**, marked temporary, so the
  scheme, policy and claim set are testable until T8; it is not mapped in Production.
- **One request, one scheme.** On the MCP path the web scheme forwards to the MCP scheme, so a request is validated once and a
  valid MCP token does not produce a spurious "Bearer was not authenticated" failure.
- **Data minimisation.** After validation the principal keeps only `sub`, `client_id`, `scope`, `jti`, `iss`, `aud`, `exp`,
  `iat`, `nbf` on both schemes. The `email` claim that authservice puts in every token never reaches a handler (ADR-0014).
- **Anti-corruption at the edge (P11).** Two token dialects (different issuers, audiences, claim sets, `typ`) are normalised once,
  here, into one internal principal (`sub`, `client_id`, `scope`); nothing downstream knows there were two.
- **Deny by default, audited mechanically.** `EndpointAuthorizationMatrixTests` lists every endpoint: the anonymous set is exactly
  `/health`, `/alive`, the two protected-resource metadata URLs (and OpenAPI in Development); every other endpoint must name
  `account` (`/api/v1/*`) or `mcp-submit` (`/mcp/*`). A new endpoint outside that fails the build.
- **Configuration** (all public values, none secret): `Mcp:Issuer`, `Mcp:Resource`, `Mcp:MetadataAddress`. Unset or invalid
  (a resource without a path, a non-https non-loopback URL, a fragment), the scheme registers but validates nothing, the
  metadata answers 404, the mount point is not mapped, and `/health` lists `mcp-auth` as not configured (P8).

## Consequences

- A token minted for the web portal cannot call the MCP endpoint and an MCP token cannot call `/api/v1/*`, even though one
  key signs both. The test matrix (`tests/**/Auth/TokenMatrixTests.cs`) runs every row against both schemes: wrong issuer,
  wrong or missing audience, expired, no subject, unknown kid, unknown key claiming a known kid, `alg=none`, HS256 keyed with a
  public key, tampered payload; and for MCP the audience and issuer near-misses, wrong `typ`, and scope variants.
- **Local development of the MCP path needs two public https URLs** (authservice's issuer and the resource): authservice
  refuses http for both, and Claude's servers must reach them. The AppHost configures the client only when
  `Mcp:AuthPublicBaseUrl` and `Mcp:ResourceUrl` are set (see `scripts/README.md`); otherwise the MCP path is simply off.
  Without Claude, the path was exercised end to end against a locally built authservice behind a self-signed TLS proxy.
- **Not verifiable here:** that Claude completes the flow against this metadata, and the published `v0.3.4` image itself
  (the sandbox cannot pull it; the equivalence above is by source diff, not by running the image).
- Revocation: an issued MCP access token cannot be recalled (authservice D4); its 15-minute life is the containment. A deleted
  account's token keeps validating until `exp`, on both schemes (observed: the old access token still passed after account
  deletion). This service has no account store to check against, by design.
- `Mcp:Resource` is compared as a string: changing its path or host after clients are registered requires changing
  authservice's `AllowedResources` in the same release, or every MCP token is refused.
