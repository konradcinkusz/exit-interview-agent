using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Tests.Support;

namespace ExitInterviewAgent.InterviewService.Tests.Account;

public sealed class MeEndpointTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private HttpClient ClientWith(string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_with_401()
    {
        var response = await ClientWith(null).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_yields_the_subject_and_nothing_else()
    {
        var response = await ClientWith(factory.MintToken("account-123")).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new MeResponse("account-123"), await response.Content.ReadFromJsonAsync<MeResponse>());
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        var response = await ClientWith(factory.MintToken("account-123", audience: "some-other-product")).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Symmetrically_signed_token_is_rejected_even_when_claims_match()
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var forged = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(
            new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Issuer = ServiceFactory.Issuer,
                Audience = ServiceFactory.Audience,
                Expires = DateTime.UtcNow.AddMinutes(5),
                Claims = new Dictionary<string, object> { ["sub"] = "attacker" },
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256),
            });

        var response = await ClientWith(forged).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
