using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>
/// Hosts the real service in-process on the InMemory provider (no container). Each factory owns a
/// throwaway RSA key generated at construction so tests can mint tokens the service will accept:
/// production holds no signing key, this key exists only inside the test process. One key serves both
/// schemes, as authservice's one key serves both token families.
/// </summary>
public sealed class ServiceFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "test-issuer";
    public const string Audience = "test-audience";
    public const string McpIssuer = "https://auth.test.example";
    public const string McpResource = "https://mcp.test.example/mcp";
    public const string KeyId = "test-key";

    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>Extra settings a test class wants (applied before the host builds).</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Mcp:Issuer", McpIssuer);
        builder.UseSetting("Mcp:Resource", McpResource);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureServices(services =>
        {
            var key = new RsaSecurityKey(_rsa) { KeyId = KeyId };
            foreach (var scheme in new[] { JwtBearerDefaults.AuthenticationScheme, "McpBearer" })
            {
                services.PostConfigure<JwtBearerOptions>(scheme, o => o.TokenValidationParameters.IssuerSigningKey = key);
            }
        });
    }

    /// <summary>A valid web/BFF token.</summary>
    public string MintToken(string subject, string? audience = null)
        => new TokenBuilder(_rsa) { Subject = subject, Issuer = Issuer, Audience = audience ?? Audience, Type = "JWT" }.Build();

    /// <summary>A valid MCP token as authservice's authorization server issues it (ADR 0005 A9).</summary>
    public string MintMcpToken(string subject, string scope = "interview:submit offline_access")
        => NewMcpToken(subject, scope).Build();

    public TokenBuilder NewMcpToken(string subject = "account-123", string scope = "interview:submit offline_access")
        => new(_rsa)
        {
            Subject = subject,
            Issuer = McpIssuer,
            Audience = McpResource,
            Scope = scope,
            ClientId = "claude-test",
            Type = "at+jwt",
        };

    public TokenBuilder NewWebToken(string subject = "account-123")
        => new(_rsa) { Subject = subject, Issuer = Issuer, Audience = Audience, Type = "JWT" };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Builds a token with every knob a validation matrix needs.</summary>
public sealed class TokenBuilder(RSA signingKey)
{
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? Scope { get; set; }
    public string? ClientId { get; set; }
    public string? Email { get; set; } = "someone@example.invalid";
    public string Type { get; set; } = "JWT";
    public string? KeyId { get; set; } = ServiceFactory.KeyId;
    public DateTime Expires { get; set; } = DateTime.UtcNow.AddMinutes(5);
    public DateTime? NotBefore { get; set; }
    public RSA? OtherKey { get; set; }
    public string Algorithm { get; set; } = SecurityAlgorithms.RsaSha256;

    public string Build()
    {
        var claims = new Dictionary<string, object>();
        if (Subject is not null) claims["sub"] = Subject;
        if (Scope is not null) claims["scope"] = Scope;
        if (ClientId is not null) claims["client_id"] = ClientId;
        if (Email is not null) claims["email"] = Email;
        var key = new RsaSecurityKey(OtherKey ?? signingKey) { KeyId = KeyId };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = Expires,
            NotBefore = NotBefore,
            IssuedAt = NotBefore is null ? null : NotBefore,
            Claims = claims,
            AdditionalHeaderClaims = new Dictionary<string, object> { ["typ"] = Type },
            SigningCredentials = new SigningCredentials(key, Algorithm),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
