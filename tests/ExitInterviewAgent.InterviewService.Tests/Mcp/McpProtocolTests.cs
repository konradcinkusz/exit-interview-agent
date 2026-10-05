using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Mcp;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using ModelContextProtocol.Protocol;

namespace ExitInterviewAgent.InterviewService.Tests.Mcp;

/// <summary>
/// The MCP server as a host sees it: the official client, a real MCP token, the real service. Covers the listings, the
/// prompt, the resources and both tools, including every rejection a host model has to be able to act on.
/// </summary>
public sealed class McpProtocolTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly string _sub = TestRecords.NewSub();

    public void Dispose() => _host.Dispose();

    private Task<ModelContextProtocol.Client.McpClient> ConnectAsync(string? token = null) =>
        McpTestClient.ConnectAsync(_host.Client(token ?? _host.McpToken(_sub)));

    [Fact]
    public async Task The_server_lists_exactly_two_tools_one_prompt_and_three_resources()
    {
        await using var client = await ConnectAsync();

        Assert.Equal([McpNames.SubmitTool, McpNames.ValidateTool], (await client.ListToolsAsync()).Select(t => t.Name).Order().ToArray());
        Assert.Equal([McpNames.ConductPrompt], (await client.ListPromptsAsync()).Select(p => p.Name).ToArray());
        Assert.Equal([McpNames.ProtocolUri, McpNames.RecordSchemaUri, McpNames.TopicsUri], (await client.ListResourcesAsync()).Select(r => r.Uri).Order().ToArray());
    }

    [Fact]
    public async Task Tool_annotations_say_what_each_tool_does()
    {
        await using var client = await ConnectAsync();
        var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name);

        var validate = tools[McpNames.ValidateTool].ProtocolTool.Annotations!;
        Assert.True(validate.ReadOnlyHint);
        Assert.True(validate.IdempotentHint);
        Assert.False(validate.OpenWorldHint);

        var submit = tools[McpNames.SubmitTool].ProtocolTool.Annotations!;
        Assert.False(submit.ReadOnlyHint);
        Assert.False(submit.IdempotentHint);
        Assert.False(submit.DestructiveHint); // it adds a record, it destroys nothing
        Assert.False(submit.OpenWorldHint);
    }

    [Fact]
    public async Task The_server_does_not_offer_or_need_anything_the_host_may_lack()
    {
        await using var client = await ConnectAsync();   // a client that advertises no sampling, no elicitation, no roots

        var caps = client.ServerCapabilities;
        Assert.NotNull(caps.Tools);
        Assert.NotNull(caps.Prompts);
        Assert.NotNull(caps.Resources);
        Assert.Null(caps.Completions);
        Assert.NotEqual(true, caps.Resources!.Subscribe);
        // Every tool still works for such a client (the other tests in this class use exactly that client).
    }

    [Fact]
    public async Task The_prompt_returns_the_protocol_as_one_instruction_message()
    {
        await using var client = await ConnectAsync();

        var result = await client.GetPromptAsync(McpNames.ConductPrompt, new Dictionary<string, object?> { ["language"] = "en" });

        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        var text = Assert.IsType<TextContentBlock>(message.Content).Text;
        Assert.Contains("I am an AI interviewer", text);
        Assert.Contains(McpNames.ValidateTool, text);
        Assert.Contains(McpNames.SubmitTool, text);
    }

    [Fact]
    public async Task The_resources_are_the_published_documents()
    {
        await using var client = await ConnectAsync();

        var schema = Assert.Single((await client.ReadResourceAsync(McpNames.RecordSchemaUri)).Contents);
        Assert.Equal(ExitInterviewAgent.Records.RecordSchema.JsonText, Assert.IsType<TextResourceContents>(schema).Text);
        var protocol = Assert.IsType<TextResourceContents>(Assert.Single((await client.ReadResourceAsync(McpNames.ProtocolUri)).Contents));
        Assert.Equal(ExitInterviewAgent.Agent.Protocol.InterviewProtocol.CurrentJson, protocol.Text);
        var topics = Assert.IsType<TextResourceContents>(Assert.Single((await client.ReadResourceAsync(McpNames.TopicsUri)).Contents));
        Assert.Contains("reason_for_leaving", topics.Text);
    }

    [Fact]
    public async Task Validate_accepts_a_good_record_and_stores_nothing()
    {
        await using var client = await ConnectAsync();
        var employer = TestRecords.NewEmployer();

        var result = await client.CallAsync(McpNames.ValidateTool, TestRecords.Valid(employer));

        Assert.NotEqual(true, result.IsError);
        var body = result.Body();
        Assert.Equal("valid", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("stored").GetBoolean());
        // The dry run took no ledger slot: the same record can still be submitted afterwards.
        Assert.True((await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(employer))).Body().GetProperty("stored").GetBoolean());
    }

    [Fact]
    public async Task Validate_reports_codes_and_paths_but_never_the_text()
    {
        await using var client = await ConnectAsync();
        var record = TestRecords.Valid(change: r => r["topics"]!["culture"]!["rating"] = 9);
        record["zzfieldcanary"] = "zzvaluecanary";

        var result = await client.CallAsync(McpNames.ValidateTool, record);

        var body = result.Body();
        Assert.Equal("invalid", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("code").GetString()));
        Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
        var raw = body.GetRawText();
        Assert.DoesNotContain("zzfieldcanary", raw);
        Assert.DoesNotContain("zzvaluecanary", raw);
    }

    [Fact]
    public async Task Validate_names_the_kinds_of_personal_data_and_not_the_data()
    {
        await using var client = await ConnectAsync();
        var record = TestRecords.Valid(change: r => r.SetQuotes("culture", "Write to zzpiicanary@example.org about it."));

        var body = (await client.CallAsync(McpNames.ValidateTool, record)).Body();

        Assert.Equal(SubmissionCodes.PiiDetected, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("piiKinds").EnumerateArray(), k => k.GetString() == "Email");
        Assert.DoesNotContain("zzpiicanary", body.GetRawText());
    }

    [Fact]
    public async Task Validate_says_when_ai_was_not_disclosed()
    {
        await using var client = await ConnectAsync();
        var record = TestRecords.Valid(change: r => r["interview"]!["aiDisclosed"] = false);

        var body = (await client.CallAsync(McpNames.ValidateTool, record)).Body();

        Assert.Equal(SubmissionCodes.AiNotDisclosed, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_stores_the_record_and_returns_the_receipt_code_once()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid());

        Assert.NotEqual(true, result.IsError);
        var body = result.Body();
        Assert.Equal("accepted", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("stored").GetBoolean());
        var code = body.GetProperty("receiptCode").GetString()!;
        Assert.True(ReceiptCodes.IsWellFormed(code));
        Assert.Contains("once", body.GetProperty("notice").GetString());
    }

    [Fact]
    public async Task Submit_twice_for_the_same_employer_is_refused_with_a_stable_code_and_no_second_receipt()
    {
        await using var client = await ConnectAsync();
        var employer = TestRecords.NewEmployer();
        await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(employer));

        var second = await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(employer));

        Assert.True(second.IsError);
        var body = second.Body();
        Assert.Equal(SubmissionCodes.AlreadySubmitted, body.GetProperty("code").GetString());
        Assert.False(body.GetProperty("stored").GetBoolean());
        Assert.False(body.TryGetProperty("receiptCode", out _));
    }

    [Fact]
    public async Task Submit_refuses_personal_data_and_undisclosed_ai()
    {
        await using var client = await ConnectAsync();

        var pii = await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(change: r => r.SetQuotes("culture", "Call me on zzpiicanary@example.org please.")));
        var ai = await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(change: r => r["interview"]!["aiDisclosed"] = false));

        Assert.True(pii.IsError);
        Assert.Equal(SubmissionCodes.PiiDetected, pii.Body().GetProperty("code").GetString());
        Assert.DoesNotContain("zzpiicanary", pii.Body().GetRawText());
        Assert.True(ai.IsError);
        Assert.Equal(SubmissionCodes.AiNotDisclosed, ai.Body().GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_refuses_an_oversized_record_with_the_stable_code()
    {
        await using var client = await ConnectAsync();
        var record = TestRecords.Valid();
        record["padding"] = new string('x', 165 * 1024); // over the 160 KiB record limit, under the transport limit

        var result = await client.CallAsync(McpNames.SubmitTool, record);

        Assert.True(result.IsError);
        Assert.Equal(SubmissionCodes.PayloadTooLarge, result.Body().GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_transcript_field_cannot_ride_along()
    {
        await using var client = await ConnectAsync();
        var record = TestRecords.Valid();
        record["transcript"] = "zztranscriptcanary the whole conversation";

        var result = await client.CallAsync(McpNames.SubmitTool, record);

        Assert.True(result.IsError);
        Assert.Equal("UNKNOWN_FIELD", result.Body().GetProperty("code").GetString());
        Assert.DoesNotContain("zztranscriptcanary", result.Body().GetRawText());
    }

    [Fact]
    public async Task The_submission_belongs_to_the_account_in_the_token_not_to_anything_in_the_arguments()
    {
        var employer = TestRecords.NewEmployer();
        await using var first = await ConnectAsync(_host.McpToken(TestRecords.NewSub()));
        await using var other = await ConnectAsync(_host.McpToken(TestRecords.NewSub()));

        // Two accounts, one employer: each gets its own slot. Same account would be a duplicate (previous test).
        Assert.Equal("accepted", (await first.CallAsync(McpNames.SubmitTool, TestRecords.Valid(employer))).Body().GetProperty("status").GetString());
        Assert.Equal("accepted", (await other.CallAsync(McpNames.SubmitTool, TestRecords.Valid(employer))).Body().GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_withdrawn_receipt_code_deletes_what_an_mcp_submission_stored()
    {
        await using var client = await ConnectAsync();
        var code = (await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid())).Body().GetProperty("receiptCode").GetString()!;
        var anonymous = _host.Client();
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/receipts");
        request.Headers.Add(SubmissionHeaders.ReceiptCode, code);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await anonymous.SendAsync(request)).StatusCode);
    }
}
