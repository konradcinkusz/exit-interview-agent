using System.Security.Claims;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Tests.Auth;

/// <summary>Data minimisation: the service keeps the claims it uses and drops the account's email and name (ADR-0014).</summary>
public sealed class ClaimMinimizationTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private static ClaimsPrincipal Full() => new(new ClaimsIdentity(
    [
        new Claim("sub", "account-1"), new Claim("email", "x@example.invalid"), new Claim("client_id", "c"),
        new Claim("scope", "interview:submit"), new Claim(ClaimTypes.Name, "x"), new Claim(ClaimTypes.Role, "Admin"),
        new Claim("organization", "org-1"),
    ], "test"));

    [Fact]
    public void Only_the_retained_claims_survive()
    {
        var kept = ExitInterviewAgent.InterviewService.Infrastructure.Auth.McpAuthenticationExtensions.MinimizeClaims(Full()).Claims.Select(c => c.Type).Order().ToArray();

        Assert.Equal(["client_id", "scope", "sub"], kept);
    }

    [Theory]
    [InlineData(AuthSchemes.Web)]
    [InlineData(AuthSchemes.Mcp)]
    public async Task Both_schemes_apply_it_after_token_validation(string scheme)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(scheme);
        var context = new TokenValidatedContext(new DefaultHttpContext(), new AuthenticationScheme(scheme, null, typeof(JwtBearerHandler)), options)
        {
            Principal = Full(),
        };

        await options.Events.OnTokenValidated(context);

        Assert.DoesNotContain(context.Principal!.Claims, c => c.Type is "email" or ClaimTypes.Name or "organization");
        Assert.Equal("account-1", context.Principal.FindFirst("sub")?.Value);
    }
}
