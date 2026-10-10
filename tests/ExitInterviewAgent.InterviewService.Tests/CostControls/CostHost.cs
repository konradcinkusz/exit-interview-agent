using System.Security.Cryptography;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Tests.CostControls;

/// <summary>
/// The service in-process for the W4 cost-control tests. <see cref="TokenBuilder"/> is sealed and has no claim for the email
/// verification flag, so this host signs its own tokens with the extra claims a test needs. It is otherwise the same setup as
/// the W2 interview host: a fake clock, a throwaway key, the scripted mock by default.
/// </summary>
public sealed class CostHost : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Settings applied before the host builds. Email verification is off by default (the Development value).</summary>
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Interviews:Provider"] = "mock",
        ["Interviews:Model"] = "scripted",
        ["Interviews:RequireVerifiedEmail"] = "false",
    };

    /// <summary>When set, this model replaces the configured provider.</summary>
    public IChatClient? ModelOverride { get; set; }

    /// <summary>Settings for this test only: the defaults above, with the given keys replaced.</summary>
    public CostHost With(params (string Key, string Value)[] overrides)
    {
        foreach (var (key, value) in overrides)
        {
            Settings[key] = value;
        }
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Issuer", ServiceFactory.Issuer);
        builder.UseSetting("Jwt:Audience", ServiceFactory.Audience);
        builder.UseSetting("Mcp:Issuer", ServiceFactory.McpIssuer);
        builder.UseSetting("Mcp:Resource", ServiceFactory.McpResource);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureServices(services =>
        {
            var key = new RsaSecurityKey(_rsa) { KeyId = ServiceFactory.KeyId };
            foreach (var scheme in new[] { JwtBearerDefaults.AuthenticationScheme, "McpBearer" })
            {
                services.PostConfigure<JwtBearerOptions>(scheme, o => o.TokenValidationParameters.IssuerSigningKey = key);
            }
            services.AddSingleton<TimeProvider>(Clock);
            if (ModelOverride is { } model)
            {
                services.AddSingleton<IInterviewModelFactory>(new FixedModelFactory(model));
            }
        });
    }

    /// <summary>An HTTP client that calls the API as the account. <paramref name="claims"/> are added to the token as they are.</summary>
    public HttpClient As(string account, IDictionary<string, object>? claims = null)
    {
        var client = CreateClient();
        var all = new Dictionary<string, object> { ["sub"] = account };
        if (claims is not null)
        {
            foreach (var (name, value) in claims)
            {
                all[name] = value;
            }
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Sign(all));
        return client;
    }

    /// <summary>A token whose account has verified its email (the claim authservice would have to send; see ADR-0078).</summary>
    public HttpClient Verified(string account) => As(account, new Dictionary<string, object> { ["email_verified"] = true });

    private string Sign(Dictionary<string, object> claims)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ServiceFactory.Issuer,
            Audience = ServiceFactory.Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = ServiceFactory.KeyId }, SecurityAlgorithms.RsaSha256),
            AdditionalHeaderClaims = new Dictionary<string, object> { ["typ"] = "JWT" },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rsa.Dispose();
        base.Dispose(disposing);
    }

    private sealed class FixedModelFactory(IChatClient client) : IInterviewModelFactory
    {
        public InterviewModel Create() => new(client, null);
    }
}
