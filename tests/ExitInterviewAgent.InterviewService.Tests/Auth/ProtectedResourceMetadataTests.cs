using System.Net;
using System.Net.Http.Headers;
using ExitInterviewAgent.InterviewService.Tests.Support;

namespace ExitInterviewAgent.InterviewService.Tests.Auth;

public sealed class ProtectedResourceMetadataTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    [Theory]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    [InlineData("/.well-known/oauth-protected-resource")]
    public async Task Metadata_is_public_and_lists_authservice_first(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsyncElement();
        Assert.Equal(ServiceFactory.McpResource, body.GetProperty("resource").GetString());
        var servers = body.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(ServiceFactory.McpIssuer, servers[0]);
        var scopes = body.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("interview:submit", scopes);
        Assert.Contains("offline_access", scopes);
        Assert.Equal("header", body.GetProperty("bearer_methods_supported")[0].GetString());
    }

    [Fact]
    public async Task Metadata_for_a_path_that_is_not_the_resource_is_404()
        => Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/.well-known/oauth-protected-resource/other")).StatusCode);

    [Fact]
    public async Task Metadata_ignores_the_request_host_for_everything_it_returns()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-protected-resource/mcp");
        request.Headers.Host = "evil.example";
        request.Headers.Add("X-Forwarded-Host", "evil.example");

        var text = await (await factory.CreateClient().SendAsync(request)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("evil.example", text);
    }

    [Fact]
    public async Task Unauthenticated_call_to_the_mcp_endpoint_gets_the_challenge_mcp_clients_follow()
    {
        var response = await factory.CreateClient().SendAsync(McpWire.ListTools());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.StartsWith("Bearer ", challenge);
        Assert.Contains("resource_metadata=\"https://mcp.test.example/.well-known/oauth-protected-resource/mcp\"", challenge);
        Assert.Contains("scope=\"interview:submit\"", challenge);
        Assert.DoesNotContain("error=", challenge); // RFC 6750 §3.1: no error when no credentials were presented
    }

    [Fact]
    public async Task Rejected_token_is_challenged_with_invalid_token_and_no_detail()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.NewMcpToken().With(t => t.Issuer = "https://evil.example").Build());

        var response = await client.SendAsync(McpWire.ListTools());

        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("error=\"invalid_token\"", challenge);
        Assert.DoesNotContain("evil.example", challenge);
        Assert.DoesNotContain("IDX", challenge);
    }

    [Fact]
    public async Task Web_endpoints_keep_the_default_challenge_and_do_not_advertise_the_mcp_metadata()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("resource_metadata", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Unconfigured_mcp_serves_no_metadata_refuses_every_token_and_is_visible_in_health()
    {
        using var unconfigured = new ServiceFactory { Settings = { ["Mcp:Issuer"] = "", ["Mcp:Resource"] = "" } };
        var client = unconfigured.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/.well-known/oauth-protected-resource")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", unconfigured.MintMcpToken("account-123"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(McpWire.ListTools())).StatusCode); // no mount point either

        var health = await HealthAsync(client);
        Assert.Contains(health.GetProperty("integrations").EnumerateArray(),
            i => i.GetProperty("name").GetString() == "mcp-auth" && !i.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task Configured_mcp_is_visible_in_health()
    {
        var health = await HealthAsync(factory.CreateClient());

        Assert.Contains(health.GetProperty("integrations").EnumerateArray(),
            i => i.GetProperty("name").GetString() == "mcp-auth" && i.GetProperty("configured").GetBoolean());
    }

    [Theory]
    [InlineData("https://mcp.test.example")]            // no path: a token audience must name the endpoint
    [InlineData("ftp://mcp.test.example/mcp")]
    [InlineData("http://mcp.test.example/mcp")]         // http only on loopback
    [InlineData("https://mcp.test.example/mcp#frag")]
    [InlineData("not a url")]
    public async Task Invalid_resource_configuration_leaves_mcp_unconfigured(string resource)
    {
        using var f = new ServiceFactory { Settings = { ["Mcp:Resource"] = resource } };

        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateClient().GetAsync("/.well-known/oauth-protected-resource")).StatusCode);
    }

    [Fact]
    public async Task Resource_is_canonicalised_lowercase_without_trailing_slash()
    {
        using var f = new ServiceFactory { Settings = { ["Mcp:Resource"] = "HTTPS://MCP.test.example/mcp/" } };

        var body = await (await f.CreateClient().GetAsync("/.well-known/oauth-protected-resource/mcp")).Content.ReadFromJsonAsyncElement();

        Assert.Equal("https://mcp.test.example/mcp", body.GetProperty("resource").GetString());
    }

    private static async Task<System.Text.Json.JsonElement> HealthAsync(HttpClient client)
    {
        HttpResponseMessage response;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            response = await client.GetAsync("/health");
        } while (response.StatusCode != HttpStatusCode.OK && DateTime.UtcNow < deadline);
        return await response.Content.ReadFromJsonAsyncElement();
    }
}
