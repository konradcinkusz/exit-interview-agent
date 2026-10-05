using System.Net;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Tests.Support;

namespace ExitInterviewAgent.InterviewService.Tests.Submissions;

/// <summary>
/// Which principal reaches which route (T-17 for the new surface). The account and MCP principals both reach the same
/// SubmissionService with the same subject; the wrong scheme is refused on both sides.
/// </summary>
public sealed class EndpointAccessTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<(HttpResponseMessage Response, string Body)> PostAsync(string path, string? bearer, byte[]? body = null) =>
        _host.Client(bearer).PostAsync(path, TestRecords.Json(body ?? TestRecords.Bytes(TestRecords.Valid()))).ReadAsync();

    [Fact]
    public async Task The_account_endpoint_takes_a_web_token_and_nothing_else()
    {
        var sub = TestRecords.NewSub();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/api/v1/submissions", null)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/api/v1/submissions", _host.McpToken(sub))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/api/v1/submissions", _host.WebToken(sub))).Response.StatusCode);
    }

    [Fact]
    public async Task The_mcp_principal_reaches_the_service_and_a_web_token_does_not_reach_the_mcp_mount()
    {
        var sub = TestRecords.NewSub();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/mcp/_submit", _host.WebToken(sub))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("/mcp/_submit", null)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/mcp/_submit", _host.McpToken(sub))).Response.StatusCode);
    }

    [Fact]
    public async Task An_mcp_token_without_the_submit_scope_is_refused()
    {
        var token = _host.NewMcpToken(TestRecords.NewSub()).With(t => t.Scope = "offline_access").Build();

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("/mcp/_submit", token)).Response.StatusCode);
    }

    [Fact]
    public async Task The_ledger_is_one_ledger_across_both_paths_because_the_subject_is_the_same_user_id()
    {
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();

        Assert.Equal(HttpStatusCode.Created, (await PostAsync("/mcp/_submit", _host.McpToken(sub), TestRecords.Bytes(TestRecords.Valid(employer)))).Response.StatusCode);
        var (second, body) = await PostAsync("/api/v1/submissions", _host.WebToken(sub), TestRecords.Bytes(TestRecords.Valid(employer)));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(SubmissionCodes.AlreadySubmitted, TestRecords.Code(body));
    }

    [Fact]
    public async Task Minting_a_ticket_needs_an_account_token_not_an_mcp_token()
    {
        var sub = TestRecords.NewSub();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().PostAsync("/api/v1/tickets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client(_host.McpToken(sub)).PostAsync("/api/v1/tickets", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _host.Client(_host.WebToken(sub)).PostAsync("/api/v1/tickets", null)).StatusCode);
    }

    [Fact]
    public async Task The_anonymous_endpoints_do_not_depend_on_a_bearer_token_and_a_token_does_not_help_them()
    {
        var client = _host.Client(_host.WebToken(TestRecords.NewSub()));

        var ticketed = await client.PostAsync("/api/v1/submissions/ticketed", TestRecords.Json(TestRecords.Valid()));

        Assert.Equal(HttpStatusCode.Unauthorized, ticketed.StatusCode); // no ticket header: a bearer token is not a ticket
        Assert.Equal(SubmissionCodes.TicketInvalid, TestRecords.Code(await ticketed.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Get_and_other_verbs_on_the_new_routes_are_not_served()
    {
        var client = _host.Client(_host.WebToken(TestRecords.NewSub()));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/api/v1/submissions")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/api/v1/receipts")).StatusCode);
    }
}
