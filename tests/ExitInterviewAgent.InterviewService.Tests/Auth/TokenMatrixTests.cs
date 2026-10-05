using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Tests.Auth;

/// <summary>
/// The validation matrix, run against BOTH schemes: the web scheme at <c>/api/v1/me</c> and the MCP scheme at the
/// mount point's probe. Every row is an attack or a mistake; the expectation is the same refusal on each side, and a
/// token of one scheme is never accepted by the other (they share one signing key, so only iss/aud keep them apart).
/// </summary>
public sealed class TokenMatrixTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    public static TheoryData<string> Schemes => new() { "web", "mcp" };

    private static string PathFor(string scheme) => scheme == "web" ? "/api/v1/me" : "/mcp/_probe";

    private TokenBuilder Valid(string scheme) => scheme == "web" ? factory.NewWebToken() : factory.NewMcpToken();

    private async Task<HttpResponseMessage> CallAsync(string scheme, string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await client.GetAsync(PathFor(scheme));
    }

    private async Task AssertRefusedAsync(string scheme, string token, HttpStatusCode expected = HttpStatusCode.Unauthorized)
        => Assert.Equal(expected, (await CallAsync(scheme, token)).StatusCode);

    [Theory, MemberData(nameof(Schemes))]
    public async Task Valid_token_is_accepted(string scheme)
        => Assert.Equal(HttpStatusCode.OK, (await CallAsync(scheme, Valid(scheme).Build())).StatusCode);

    [Theory, MemberData(nameof(Schemes))]
    public async Task No_token_is_401(string scheme)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await CallAsync(scheme, null)).StatusCode);

    [Theory, MemberData(nameof(Schemes))]
    public async Task Wrong_issuer_is_401(string scheme)
    {
        var token = Valid(scheme);
        token.Issuer = "https://evil.example";
        await AssertRefusedAsync(scheme, token.Build());
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Wrong_audience_is_401(string scheme)
    {
        var token = Valid(scheme);
        token.Audience = "https://some-other-resource.example/mcp";
        await AssertRefusedAsync(scheme, token.Build());
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Missing_audience_is_401(string scheme)
    {
        var token = Valid(scheme);
        token.Audience = null;
        await AssertRefusedAsync(scheme, token.Build());
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Expired_token_is_401(string scheme)
    {
        var token = Valid(scheme);
        token.NotBefore = DateTime.UtcNow.AddHours(-2);
        token.Expires = DateTime.UtcNow.AddHours(-1);
        await AssertRefusedAsync(scheme, token.Build());
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Token_without_a_subject_is_forbidden(string scheme)
    {
        var token = Valid(scheme);
        token.Subject = null;
        await AssertRefusedAsync(scheme, token.Build(), HttpStatusCode.Forbidden);
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Token_signed_by_an_unknown_key_with_an_unknown_kid_is_401(string scheme)
    {
        using var attacker = RSA.Create(2048);
        var token = Valid(scheme);
        token.OtherKey = attacker;
        token.KeyId = "not-in-the-jwks";
        await AssertRefusedAsync(scheme, token.Build());
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Token_signed_by_an_unknown_key_claiming_a_known_kid_is_401(string scheme)
    {
        using var attacker = RSA.Create(2048);
        var token = Valid(scheme);
        token.OtherKey = attacker;
        await AssertRefusedAsync(scheme, token.Build());
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Unsigned_token_with_alg_none_is_401(string scheme)
    {
        static string B64(object o) => Base64UrlEncoder.Encode(JsonSerializer.Serialize(o));
        var header = B64(new { alg = "none", typ = scheme == "web" ? "JWT" : "at+jwt" });
        var payload = B64(scheme == "web"
            ? new { sub = "attacker", iss = ServiceFactory.Issuer, aud = ServiceFactory.Audience, exp = 9999999999, scope = "interview:submit" }
            : new { sub = "attacker", iss = ServiceFactory.McpIssuer, aud = ServiceFactory.McpResource, exp = 9999999999, scope = "interview:submit" });

        await AssertRefusedAsync(scheme, $"{header}.{payload}.");
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Hs256_token_keyed_with_the_public_key_is_401(string scheme)
    {
        // The classic algorithm-confusion forgery: sign HMAC with the RSA public key material as the secret.
        using var rsa = RSA.Create(2048);
        var secret = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())) { KeyId = ServiceFactory.KeyId };
        var valid = Valid(scheme);
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = valid.Issuer,
            Audience = valid.Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = "attacker", ["scope"] = "interview:submit" },
            AdditionalHeaderClaims = new Dictionary<string, object> { ["typ"] = valid.Type },
            SigningCredentials = new SigningCredentials(secret, SecurityAlgorithms.HmacSha256),
        });

        await AssertRefusedAsync(scheme, forged);
    }

    [Theory, MemberData(nameof(Schemes))]
    public async Task Tampered_payload_is_401(string scheme)
    {
        var parts = Valid(scheme).Build().Split('.');
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Base64UrlEncoder.Decode(parts[1]))!;
        payload["sub"] = JsonDocument.Parse("\"someone-else\"").RootElement;
        parts[1] = Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload));

        await AssertRefusedAsync(scheme, string.Join('.', parts));
    }

    [Fact]
    public async Task A_web_token_is_refused_by_the_mcp_scheme_even_with_the_scope()
    {
        // Same key, valid signature, valid lifetime, the scope claim present: only issuer and audience differ.
        var token = factory.NewWebToken();
        token.Scope = "interview:submit";

        await AssertRefusedAsync("mcp", token.Build());
    }

    [Fact]
    public async Task An_mcp_token_is_refused_by_the_web_scheme()
        => await AssertRefusedAsync("web", factory.MintMcpToken("account-123"));

    [Fact]
    public async Task A_web_token_re_issued_with_the_mcp_audience_is_still_refused_by_the_mcp_scheme()
    {
        var token = factory.NewWebToken();
        token.Audience = ServiceFactory.McpResource; // wrong issuer + wrong typ remain
        token.Scope = "interview:submit";

        await AssertRefusedAsync("mcp", token.Build());
    }

    [Fact]
    public async Task An_mcp_token_with_the_web_issuer_and_the_mcp_audience_is_refused()
    {
        var token = factory.NewMcpToken();
        token.Issuer = ServiceFactory.Issuer;

        await AssertRefusedAsync("mcp", token.Build());
    }

    [Theory]
    [InlineData("https://mcp.test.example/mcp/")]       // trailing slash: not the canonical form
    [InlineData("https://mcp.test.example")]            // the origin, not the endpoint
    [InlineData("https://MCP.test.example/mcp")]        // not lowercased
    [InlineData("https://mcp.test.example/mcp/extra")]  // a sub-path
    public async Task Mcp_audience_must_be_the_canonical_resource_exactly(string audience)
    {
        var token = factory.NewMcpToken();
        token.Audience = audience;

        await AssertRefusedAsync("mcp", token.Build());
    }

    [Theory]
    [InlineData("https://auth.test.example/")]          // trailing slash on iss
    [InlineData("http://auth.test.example")]            // wrong scheme
    [InlineData("https://AUTH.test.example")]           // not the exact string
    public async Task Mcp_issuer_must_equal_the_configured_issuer_exactly(string issuer)
    {
        var token = factory.NewMcpToken();
        token.Issuer = issuer;

        await AssertRefusedAsync("mcp", token.Build());
    }

    [Fact]
    public async Task Mcp_token_must_carry_the_access_token_type()
    {
        var token = factory.NewMcpToken();
        token.Type = "JWT";

        await AssertRefusedAsync("mcp", token.Build());
    }

    [Theory]
    [InlineData("offline_access")]                     // only the refresh scope
    [InlineData("interview:read")]                     // a scope we do not define
    [InlineData("interview:submitted")]                // prefix of nothing; not a token match
    [InlineData("INTERVIEW:SUBMIT")]                   // scopes are case sensitive
    [InlineData("")]                                   // empty
    public async Task Mcp_token_without_the_submit_scope_is_403_with_an_insufficient_scope_challenge(string scope)
    {
        var token = factory.NewMcpToken(scope: scope);

        var response = await CallAsync("mcp", token.Build());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("error=\"insufficient_scope\"", challenge);
        Assert.Contains("scope=\"interview:submit\"", challenge);
    }

    [Fact]
    public async Task Mcp_token_with_no_scope_claim_at_all_is_403()
    {
        var token = factory.NewMcpToken();
        token.Scope = null;

        await AssertRefusedAsync("mcp", token.Build(), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("interview:submit")]
    [InlineData("offline_access interview:submit")]
    [InlineData("  interview:submit   offline_access ")]
    public async Task Mcp_token_with_the_submit_scope_among_others_is_accepted(string scope)
        => Assert.Equal(HttpStatusCode.OK, (await CallAsync("mcp", factory.NewMcpToken(scope: scope).Build())).StatusCode);

    [Fact]
    public async Task Mcp_principal_carries_only_sub_client_id_and_scope_not_the_email()
    {
        var response = await CallAsync("mcp", factory.NewMcpToken("account-9").Build());

        var body = await response.Content.ReadFromJsonAsyncElement();
        Assert.Equal("account-9", body.GetProperty("subject").GetString());
        Assert.Equal("claude-test", body.GetProperty("clientId").GetString());
        var claims = body.GetProperty("claims").EnumerateArray().Select(c => c.GetString()).ToArray();
        Assert.Contains("scope", claims);
        Assert.DoesNotContain("email", claims);
    }
}
