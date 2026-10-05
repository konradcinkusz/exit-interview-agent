using System.Net;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Mcp;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Mcp;

/// <summary>Who may reach the tools, and what the transport refuses before any tool code runs.</summary>
public sealed class McpSecurityTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static HttpRequestMessage Submit() => McpWire.Post(McpWire.CallToolBody(McpNames.SubmitTool, TestRecords.Valid()));

    [Fact]
    public async Task No_token_is_401_with_the_challenge_that_points_clients_at_the_metadata()
    {
        var response = await _host.Client().SendAsync(Submit());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata=\"https://mcp.test.example/.well-known/oauth-protected-resource/mcp\"", challenge);
        Assert.Contains("scope=\"interview:submit\"", challenge);
    }

    [Fact]
    public async Task A_token_without_the_scope_is_403_insufficient_scope_and_no_tool_runs()
    {
        var token = _host.NewMcpToken(TestRecords.NewSub()).With(t => t.Scope = "offline_access").Build();

        var response = await _host.Client(token).SendAsync(Submit());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("error=\"insufficient_scope\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task A_web_token_is_refused_on_every_mcp_method()
    {
        var client = _host.Client(_host.WebToken(TestRecords.NewSub()));

        foreach (var body in new[] { McpWire.ListToolsBody, McpWire.CallToolBody(McpNames.SubmitTool, TestRecords.Valid()) })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(McpWire.Post(body))).StatusCode);
        }
    }

    [Fact]
    public async Task A_token_for_another_resource_is_refused()
    {
        var token = _host.NewMcpToken(TestRecords.NewSub()).With(t => t.Audience = "https://other.example/mcp").Build();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client(token).SendAsync(Submit())).StatusCode);
    }

    [Fact]
    public async Task The_token_goes_in_the_header_and_a_query_string_token_is_not_read()
    {
        var token = _host.McpToken(TestRecords.NewSub());

        var response = await _host.Client().SendAsync(McpWire.Post(McpWire.ListToolsBody, "/mcp?access_token=" + token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_mcp_path_is_rate_limited_per_account()
    {
        var client = _host.Client(_host.McpToken(TestRecords.NewSub()));
        var other = _host.Client(_host.McpToken(TestRecords.NewSub()));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 125; i++)
        {
            statuses.Add((await client.SendAsync(McpWire.ListTools())).StatusCode);
        }

        Assert.Equal(120, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.OK, (await other.SendAsync(McpWire.ListTools())).StatusCode); // another account is not affected
    }

    [Fact]
    public async Task Legacy_sse_and_session_endpoints_do_not_exist()
    {
        var client = _host.Client(_host.McpToken(TestRecords.NewSub()));

        foreach (var path in new[] { "/mcp/sse", "/mcp/message", "/sse", "/message" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/mcp")).StatusCode);          // no standalone stream in stateless mode
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("/mcp")).StatusCode);       // no sessions to end
    }

    [Fact]
    public async Task Stateless_means_no_session_id_is_ever_issued()
    {
        var client = _host.Client(_host.McpToken(TestRecords.NewSub()));

        var response = await client.SendAsync(McpWire.Post("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
    }

    // ---- per-tool scope enforcement (defence in depth behind the HTTP policy) ----

    private static ClaimsPrincipal With(string? scope) => new(new ClaimsIdentity(scope is null ? [] : [new Claim("scope", scope)], "test"));

    [Theory]
    [InlineData("interview:submit", McpNames.ValidateTool, true)]
    [InlineData("offline_access interview:submit", McpNames.SubmitTool, true)]
    [InlineData("offline_access", McpNames.SubmitTool, false)]
    [InlineData("interview:submitted", McpNames.SubmitTool, false)]
    [InlineData("INTERVIEW:SUBMIT", McpNames.SubmitTool, false)]
    [InlineData("interview:submit", "some_new_tool", false)]   // a tool nobody gave a scope to is closed
    [InlineData("interview:submit", null, false)]
    [InlineData(null, McpNames.ValidateTool, false)]
    public void Each_tool_call_needs_its_scope(string? scope, string? tool, bool allowed)
        => Assert.Equal(allowed, McpToolScopes.Permits(With(scope), tool));

    [Fact]
    public async Task The_tool_call_check_holds_on_its_own_when_the_http_policy_is_relaxed()
    {
        // The HTTP policy normally refuses a token without the scope first, so the per-tool check could never fail in a test.
        // Relax the policy to "any valid MCP token" and the tool-level check is the only thing between the token and the tool.
        using var host = new TestHost(services: s => s.PostConfigure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(o =>
            o.AddPolicy(AuthPolicies.McpSubmit, p => p.AddAuthenticationSchemes(AuthSchemes.Mcp).RequireAuthenticatedUser().RequireClaim("sub"))));
        var weak = host.NewMcpToken(TestRecords.NewSub()).With(t => t.Scope = "offline_access").Build();
        var employer = TestRecords.NewEmployer();

        var refused = await McpWire.CallToolAsync(host.Client(weak), McpNames.SubmitTool, TestRecords.Valid(employer));
        var validate = await McpWire.CallToolAsync(host.Client(weak), McpNames.ValidateTool, TestRecords.Valid(employer));

        Assert.Equal(HttpStatusCode.OK, refused.Http);
        Assert.True(refused.IsError);
        Assert.Equal("INSUFFICIENT_SCOPE", refused.Code);
        Assert.Equal("INSUFFICIENT_SCOPE", validate.Code);
        // Nothing was stored for that employer: a properly scoped token from the same account can still submit.
        var sub = TestRecords.NewSub();
        Assert.True((await McpWire.CallToolAsync(host.Client(host.McpToken(sub)), McpNames.SubmitTool, TestRecords.Valid(employer))).Accepted);
    }

    [Fact]
    public void Every_listed_tool_has_a_scope_entry()
    {
        var listed = typeof(InterviewTools).GetMethods().Select(m => m.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)).Where(a => a.Length > 0).Count();

        Assert.Equal(listed, McpToolScopes.Required.Count);
    }

    // ---- nothing in the server depends on sampling, elicitation or roots (brief section 4, OP-8) ----

    [Fact]
    public void The_server_assembly_references_no_sampling_elicitation_or_roots_api()
    {
        var path = typeof(Program).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var forbidden = new[] { "SampleAsync", "ElicitAsync", "RequestRootsAsync", "AsSamplingChatClient", "CreateMessageRequestParams", "ElicitRequestParams", "ListRootsRequestParams", "SamplingCapability", "ElicitationCapability", "RootsCapability" };

        var names = md.MemberReferences.Select(h => md.GetString(md.GetMemberReference(h).Name))
            .Concat(md.TypeReferences.Select(h => md.GetString(md.GetTypeReference(h).Name)))
            .ToHashSet();

        Assert.Empty(names.Intersect(forbidden));
        Assert.Contains("AddMcpServer", names); // the scan reads the right assembly and does see SDK references
    }

    [Fact]
    public void The_scan_would_catch_a_dependency_if_one_were_added()
    {
        using var stream = File.OpenRead(typeof(ModelContextProtocol.Server.McpServer).Assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        // The SDK itself defines these; this proves the helper reads names the way the real test needs to.
        Assert.Contains(md.TypeDefinitions.Select(t => md.GetString(md.GetTypeDefinition(t).Name)), n => n == "CreateMessageRequestParams");
    }
}

/// <summary>The transport guard, request by request, and a mutation check that it is what refuses.</summary>
public sealed class McpTransportGuardTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private HttpClient Authed() => _host.Client(_host.McpToken(TestRecords.NewSub()));

    private static HttpRequestMessage WithHeader(string name, string value)
    {
        var request = McpWire.ListTools();
        request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("https://claude.ai")]            // not allowed unless the operator lists it
    [InlineData("null")]
    [InlineData("not a url")]
    [InlineData("https://mcp.test.example.evil.example")]
    public async Task A_request_with_an_origin_that_is_not_listed_is_403(string origin)
    {
        var response = await Authed().SendAsync(WithHeader("Origin", origin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ORIGIN_NOT_ALLOWED", body);
        Assert.DoesNotContain("evil.example", body); // the offending value is not echoed
    }

    [Fact]
    public async Task The_origin_is_refused_before_authentication_so_an_anonymous_probe_learns_nothing_else()
    {
        var response = await _host.Client().SendAsync(WithHeader("Origin", "https://evil.example"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task A_request_without_an_origin_is_the_normal_case_for_a_server_side_client()
        => Assert.Equal(HttpStatusCode.OK, (await Authed().SendAsync(McpWire.ListTools())).StatusCode);

    [Fact]
    public async Task An_origin_the_operator_lists_is_allowed_in_any_letter_case()
    {
        using var host = new TestHost(new() { ["Mcp:AllowedOrigins:0"] = "https://Claude.AI" });
        var client = host.Client(host.McpToken(TestRecords.NewSub()));

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(WithHeader("Origin", "https://claude.ai"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(WithHeader("Origin", "https://claude.ai.evil.example"))).StatusCode);
    }

    [Theory]
    [InlineData("1999-01-01")]
    [InlineData("latest")]
    [InlineData("2025-11-25, 2026-07-28")]
    public async Task An_unsupported_protocol_version_header_is_400(string version)
    {
        var response = await Authed().SendAsync(WithHeader(McpTransportGuard.ProtocolVersionHeader, version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNSUPPORTED_PROTOCOL_VERSION", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task A_supported_protocol_version_header_is_served(string version)
        => Assert.Equal(HttpStatusCode.OK, (await Authed().SendAsync(WithHeader(McpTransportGuard.ProtocolVersionHeader, version))).StatusCode);

    [Fact]
    public void Every_version_the_guard_allows_is_one_the_sdk_speaks_and_the_reverse()
    {
        var handler = typeof(ModelContextProtocol.Server.McpServer).Assembly.GetType("ModelContextProtocol.McpSessionHandler")!;
        var field = handler.GetField("SupportedProtocolVersions", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
        var sdk = ((IEnumerable<string>)field.GetValue(null)!).ToArray();

        Assert.Equal(sdk.Order().ToArray(), McpTransportGuard.SupportedProtocolVersions.Order().ToArray());
    }

    [Fact]
    public async Task A_body_over_the_transport_limit_is_413_before_the_record_library_sees_it()
    {
        var body = McpWire.CallToolBody(McpNames.SubmitTool, new JsonObject { ["padding"] = new string('x', McpTransportGuard.MaxRequestBytes) });

        var response = await Authed().SendAsync(McpWire.Post(body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public void The_check_is_a_pure_function_of_the_request_and_the_allow_list()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers.Origin = "https://evil.example";

        Assert.Null(McpTransportGuard.Check(context.Request, new HashSet<string> { "https://evil.example" }));
        Assert.Equal((403, "ORIGIN_NOT_ALLOWED"), McpTransportGuard.Check(context.Request, new HashSet<string>()));
    }
}
