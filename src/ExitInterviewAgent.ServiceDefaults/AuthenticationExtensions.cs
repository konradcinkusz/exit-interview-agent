using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.ServiceDefaults;

/// <summary>
/// Validation only (P5): RS256 tokens verified against the identity service's published
/// metadata/JWKS. Nothing in this repository holds a signing key or mints a token.
/// Without <c>Jwt:Authority</c> the scheme still registers (so authorization policies resolve)
/// but no token can validate: protected endpoints answer 401 and <c>/health</c> says so (P8).
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Jwt:Authority"]?.TrimEnd('/');
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        var configured = !string.IsNullOrWhiteSpace(authority);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            if (configured)
            {
                options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
                options.RequireHttpsMetadata = authority!.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            }
            options.MapInboundClaims = false; // keep "sub" as "sub"
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], // asymmetric only: verify never equals mint
            };
        });
        services.AddAuthorization();
        return services.AddIntegration("identity", configured,
            configured ? $"validating against {authority}" : "Jwt:Authority not set; protected endpoints answer 401");
    }
}
