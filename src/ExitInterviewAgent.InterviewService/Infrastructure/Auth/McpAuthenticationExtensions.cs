using System.Security.Claims;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Infrastructure.Auth;

/// <summary>
/// The second JWT scheme and both authorization policies (ADR-0012). Validation only (P5): the key comes from the
/// authservice JWKS, nothing here holds or mints one. The two schemes share one signing key and are told apart by
/// issuer and audience, which is why neither list is ever widened to "any of ours".
/// </summary>
public static class McpAuthenticationExtensions
{
    /// <summary>RFC 9068 access-token type, which authservice's MCP tokens carry (ADR 0005 A9).</summary>
    private const string AccessTokenType = "at+jwt";

    /// <summary>Claims this service keeps from a validated token. Everything else (email, name, roles) is dropped.</summary>
    private static readonly HashSet<string> RetainedClaims = new(StringComparer.Ordinal)
    {
        "sub", "client_id", "scope", "jti", "iss", "aud", "exp", "iat", "nbf",
    };

    public static IServiceCollection AddInterviewAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var mcp = configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
        services.AddSingleton(mcp);

        var authority = configuration["Jwt:Authority"]?.TrimEnd('/');
        var metadata = string.IsNullOrWhiteSpace(mcp.MetadataAddress)
            ? (string.IsNullOrWhiteSpace(authority) ? null : $"{authority}/.well-known/oauth-authorization-server")
            : mcp.MetadataAddress.Trim();

        services.AddAuthentication().AddJwtBearer(AuthSchemes.Mcp, options =>
        {
            if (metadata is not null)
            {
                options.MetadataAddress = metadata;
                options.RequireHttpsMetadata = metadata.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            }
            options.MapInboundClaims = false;
            var issuer = mcp.NormalizedIssuer;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                // Exact match against the one configured issuer. Not ValidIssuer: JwtBearer would add the issuer of
                // the discovery document to the accepted set, and the accepted set is exactly one string.
                IssuerValidator = (iss, _, _) => issuer is not null && string.Equals(iss, issuer, StringComparison.Ordinal)
                    ? iss
                    : throw new SecurityTokenInvalidIssuerException("issuer is not the configured authorization server"),
                ValidateAudience = true,
                ValidAudience = mcp.CanonicalResource,
                IgnoreTrailingSlashWhenValidatingAudience = false,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidTypes = [AccessTokenType],
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = Minimize,
                OnChallenge = ctx => ChallengeAsync(ctx, mcp),
                OnForbidden = ctx => ForbiddenAsync(ctx, mcp),
            };
        });

        // Data minimisation on the web scheme too: the BFF's token carries the account's email, which this service
        // has no use for and must never be able to log (ADR-0014).
        services.PostConfigure<JwtBearerOptions>(AuthSchemes.Web, o =>
        {
            // On the MCP path the web scheme must not even try: the request is authenticated once, by the MCP scheme.
            // Otherwise every MCP call would also be validated (and logged as a failure) by the wrong scheme.
            o.ForwardDefaultSelector = ctx => mcp.ResourcePath is { } path && ctx.Request.Path.StartsWithSegments(path) ? AuthSchemes.Mcp : null;
            var previous = o.Events.OnTokenValidated;
            o.Events.OnTokenValidated = async ctx =>
            {
                await previous(ctx);
                await Minimize(ctx);
            };
        });

        services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
        services.AddAuthorization(o =>
        {
            o.AddPolicy(AuthPolicies.Account, p => p
                .AddAuthenticationSchemes(AuthSchemes.Web)
                .RequireAuthenticatedUser()
                .RequireClaim("sub"));
            o.AddPolicy(AuthPolicies.McpSubmit, p => p
                .AddAuthenticationSchemes(AuthSchemes.Mcp)
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .AddRequirements(new ScopeRequirement(McpScopes.InterviewSubmit)));
        });

        return services.AddIntegration("mcp-auth", mcp.IsConfigured, mcp.Describe());
    }

    /// <summary>Replaces the principal with one that holds only <see cref="RetainedClaims"/>.</summary>
    public static ClaimsPrincipal MinimizeClaims(ClaimsPrincipal principal)
    {
        var identity = new ClaimsIdentity(
            principal.Claims.Where(c => RetainedClaims.Contains(c.Type)),
            principal.Identity?.AuthenticationType,
            nameType: "sub",
            roleType: ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    private static Task Minimize(TokenValidatedContext context)
    {
        if (context.Principal is not null)
        {
            context.Principal = MinimizeClaims(context.Principal);
        }
        return Task.CompletedTask;
    }

    private static Task ChallengeAsync(JwtBearerChallengeContext context, McpOptions mcp)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        // RFC 6750 §3.1: no error code when the request carried no credentials; invalid_token when it did and failed.
        // The failure's detail is never echoed (it names issuers and audiences).
        context.Response.Headers.WWWAuthenticate = BuildChallenge(mcp, context.AuthenticateFailure is null ? null : "invalid_token");
        return Task.CompletedTask;
    }

    private static Task ForbiddenAsync(ForbiddenContext context, McpOptions mcp)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.WWWAuthenticate = BuildChallenge(mcp, "insufficient_scope");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The 401 challenge MCP clients follow (MCP authorization, RFC 9728 §5.1): <c>resource_metadata</c> points at
    /// the protected-resource document and <c>scope</c> names what the endpoint needs.
    /// </summary>
    public static string BuildChallenge(McpOptions mcp, string? error)
    {
        var parts = new List<string>();
        if (error is not null)
        {
            parts.Add($"error=\"{error}\"");
        }
        if (mcp.ProtectedResourceMetadataUrl is { } url)
        {
            parts.Add($"resource_metadata=\"{url}\"");
        }
        parts.Add($"scope=\"{McpScopes.InterviewSubmit}\"");
        return $"Bearer {string.Join(", ", parts)}";
    }
}

/// <summary>This resource server enforces scope itself (authservice only carries it).</summary>
public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        // `scope` is one space-delimited string (ADR 0005 A9); a JSON-array claim would arrive as several claims.
        var granted = context.User.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (granted.Contains(requirement.Scope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
