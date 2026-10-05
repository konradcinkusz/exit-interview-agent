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
/// production holds no signing key, this key exists only inside the test process.
/// </summary>
public sealed class ServiceFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "test-issuer";
    public const string Audience = "test-audience";

    private readonly RSA _rsa = RSA.Create(2048);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            o => o.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(_rsa)));
    }

    public string MintToken(string subject, string? audience = null)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience ?? Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = subject },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256),
        });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
        }
        base.Dispose(disposing);
    }
}
