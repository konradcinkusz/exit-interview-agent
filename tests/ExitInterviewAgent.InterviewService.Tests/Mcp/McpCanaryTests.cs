using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.InterviewService.Mcp;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.InterviewService.Tests.Mcp;

/// <summary>
/// The content canary, across the MCP path (T-15). One scenario drives every MCP method with unique strings planted in
/// every place a host can put text: the record's quotes, the employer, the argument names, the prompt arguments, a
/// resource URI, a request header, the token's subject and the body of a malformed request. Everything the process emits is
/// captured (log lines at Trace, exception text, activity tags and events, event-source payloads, metric labels) and none
/// of it may contain a canary. The capture is proven to see MCP traffic by un-clamping the SDK's message logging, which
/// DOES leak the receipt code; that is the regression this test exists to catch.
/// </summary>
public sealed class McpCanaryTests
{
    private const string Quote = "zzmcpquotecanary7731";
    private const string Employer = "emp-zzmcpemployercanary";
    private const string SubCanary = "acct-zzmcpsubcanary5512";
    private const string Header = "zzmcpheadercanary9084";
    private const string FieldName = "zzmcpfieldcanary2201";
    private const string Hint = "zzmcphintcanary6610";
    private const string Lang = "zzmcplangcanary";
    private const string Uri = "zzmcpuricanary3307";
    private const string Malformed = "zzmcpmalformedcanary8812";
    private const string Origin = "zzmcporigincanary4471";
    private const string Verifier = "zzmcpexceptioncanary4419";
    private const string Tool = "zzmcptoolcanary5530";
    private const string Prompt = "zzmcppromptcanary6641";
    private const string Method = "zzmcpmethodcanary7752";

    private static readonly string[] Canaries = [Quote, Employer, SubCanary, Header, FieldName, Hint, Lang, Uri, Malformed, Origin, Verifier, Tool, Prompt, Method];

    private static TestHost NewHost(Capture capture, Action<IServiceCollection>? services = null, Dictionary<string, string?>? settings = null) =>
        new(settings, null, services, capture.Logs);

    /// <summary>Returns the secrets the scenario obtained (the receipt codes), which must not appear anywhere either.</summary>
    private static async Task<List<string>> RunScenarioAsync(TestHost host)
    {
        var secrets = new List<string>();
        var http = host.Client(host.McpToken(SubCanary));
        http.DefaultRequestHeaders.Add("X-Canary", Header);
        var raw0 = host.Client(host.McpToken(SubCanary));
        await using var client = await McpTestClient.ConnectAsync(http);

        // Discovery.
        await client.ListToolsAsync();
        await client.ListPromptsAsync();
        await client.ListResourcesAsync();
        await client.GetPromptAsync(McpNames.ConductPrompt, new Dictionary<string, object?> { ["language"] = Lang, ["employerHint"] = Hint });
        await client.ReadResourceAsync(McpNames.RecordSchemaUri);
        // Client-chosen names go over the raw wire: the in-process SDK client would put the same names into ITS OWN metrics and
        // spans, which share this process and would be a false positive.
        await raw0.SendAsync(McpWire.Post($"{{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"resources/read\",\"params\":{{\"uri\":\"exit-interview://{Uri}\"}}}}"));
        await raw0.SendAsync(McpWire.Post(McpWire.CallToolBodyWithArguments(Tool, new JsonObject { ["x"] = Quote })));
        await raw0.SendAsync(McpWire.Post($"{{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"prompts/get\",\"params\":{{\"name\":\"{Prompt}\",\"arguments\":{{\"x\":\"{Quote}\"}}}}}}"));
        await raw0.SendAsync(McpWire.Post($"{{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"{Method}\",\"params\":{{\"x\":\"{Quote}\"}}}}"));
        await raw0.SendAsync(McpWire.Post($"{{\"jsonrpc\":\"2.0\",\"method\":\"{Method}\"}}"));

        // Validate: valid, schema-invalid with canary field and value, PII with a canary around it.
        await client.CallAsync(McpNames.ValidateTool, TestRecords.Valid(Employer, r => r.SetQuotes("culture", $"The team was {Quote} indeed.")));
        await client.CallAsync(McpNames.ValidateTool, TestRecords.Valid(Employer, r => r[FieldName] = Quote));
        await client.CallAsync(McpNames.ValidateTool, TestRecords.Valid(Employer, r => r.SetQuotes("culture", $"{Quote} write to {Quote}@example.org")));

        // Submit: accepted (receipt code), duplicate, invalid, PII, oversize.
        var accepted = await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(Employer, r => r.SetQuotes("culture", $"The team was {Quote} indeed.")));
        secrets.Add(accepted.Body().GetProperty("receiptCode").GetString()!);
        await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(Employer, r => r.SetQuotes("culture", Quote)));
        await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(Employer + "x", r => r[FieldName] = Quote));
        await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(Employer + "y", r => r.SetQuotes("culture", $"{Quote} write to {Quote}@example.org")));
        await client.CallAsync(McpNames.SubmitTool, TestRecords.Valid(Employer + "z", r => r["padding"] = new string('q', 165 * 1024)));

        // The raw wire: malformed JSON-RPC, a canary in an unknown argument name, a refused Origin, an oversize body.
        var raw = host.Client(host.McpToken(SubCanary));
        await raw.SendAsync(McpWire.Post($"{{ \"jsonrpc\": \"2.0\", \"id\": 1, \"method\": \"tools/call\", \"params\": {{ \"name\": \"{McpNames.SubmitTool}\", \"arguments\": {{ \"record\": \"{Malformed}\" "));
        await raw.SendAsync(McpWire.Post(McpWire.CallToolBodyWithArguments(McpNames.SubmitTool, new JsonObject { [FieldName] = Quote })));
        await raw.SendAsync(McpWire.Post(McpWire.CallToolBody(McpNames.SubmitTool, new JsonObject { ["padding"] = new string('q', McpTransportGuard.MaxRequestBytes) })));
        var withOrigin = McpWire.ListTools();
        withOrigin.Headers.TryAddWithoutValidation("Origin", "https://" + Origin + ".example");
        await raw.SendAsync(withOrigin);
        await host.Client().SendAsync(McpWire.ListTools()); // no token
        return secrets;
    }

    private static void AssertClean(string everything, IEnumerable<string> canaries)
    {
        foreach (var canary in canaries)
        {
            var at = everything.IndexOf(canary, StringComparison.OrdinalIgnoreCase);
            Assert.False(at >= 0, at < 0 ? "" : $"canary '{canary[..Math.Min(20, canary.Length)]}...' leaked into telemetry: ...{everything[Math.Max(0, at - 200)..Math.Min(everything.Length, at + 80)]}");
        }
    }

    [Fact]
    public async Task No_canary_reaches_a_log_line_a_trace_attribute_an_event_an_exception_message_or_a_metric_label()
    {
        using var capture = new Capture();
        using var host = NewHost(capture, s => s.AddSingleton<IEmploymentVerifier>(new ThrowingVerifier()));

        var secrets = await RunScenarioAsync(host);

        var everything = capture.Everything;
        Assert.True(everything.Length > 5000, "the capture saw almost nothing; the test would pass for the wrong reason");
        Assert.Contains("tools/call", everything);   // the SDK's own lines are in scope of the check
        AssertClean(everything, Canaries.Concat(secrets));
    }

    [Fact]
    public async Task A_configuration_that_asks_for_trace_logging_of_the_sdk_changes_nothing()
    {
        using var capture = new Capture();
        var provider = typeof(CaptureLoggerProvider).FullName!;
        using var host = NewHost(capture, settings: new()
        {
            ["Logging:LogLevel:ModelContextProtocol"] = "Trace",
            ["Logging:LogLevel:ModelContextProtocol.Server.McpServer"] = "Trace",
            [$"Logging:{provider}:LogLevel:ModelContextProtocol"] = "Trace",
            [$"Logging:{provider}:LogLevel:Default"] = "Trace",
        });

        var secrets = await RunScenarioAsync(host);

        AssertClean(capture.Everything, Canaries.Concat(secrets));
    }

    [Fact]
    public async Task Mutation_check_unclamped_sdk_logging_leaks_the_receipt_code_and_the_test_sees_it()
    {
        using var capture = new Capture();
        using var host = NewHost(capture, s =>
            s.PostConfigure<LoggerFilterOptions>(o => o.Rules.Add(new LoggerFilterRule(null, McpServerSetup.SdkLogCategory, LogLevel.Trace, null))));

        var secrets = await RunScenarioAsync(host);

        Assert.Contains(secrets[0], capture.Everything); // the leak is visible: the clean results above are not vacuous
    }

    [Fact]
    public async Task A_body_logging_regression_on_the_mcp_path_is_caught_by_the_same_scenario()
    {
        using var capture = new Capture();
        using var host = NewHost(capture, s => s.AddTransient<IStartupFilter, BodyLoggingFilter>());

        await RunScenarioAsync(host);

        Assert.Contains(Quote, capture.Everything);
    }

    [Fact]
    public void The_sdk_log_clamp_replaces_any_rule_for_its_categories_and_keeps_the_rest()
    {
        var options = new LoggerFilterOptions { MinLevel = LogLevel.Trace };
        options.Rules.Add(new LoggerFilterRule(null, "ModelContextProtocol.Server.McpServer", LogLevel.Trace, null));
        options.Rules.Add(new LoggerFilterRule("Console", "ModelContextProtocol", LogLevel.Debug, null));
        options.Rules.Add(new LoggerFilterRule(null, "Microsoft.AspNetCore", LogLevel.Warning, null));

        McpServerSetup.ClampSdkLogging(options);

        Assert.All(options.Rules.Where(r => r.CategoryName?.StartsWith("ModelContextProtocol") == true), r => Assert.Equal(LogLevel.Information, r.LogLevel));
        Assert.DoesNotContain(options.Rules, r => r.CategoryName == "ModelContextProtocol.Server.McpServer");
        Assert.Contains(options.Rules, r => r.CategoryName == "Microsoft.AspNetCore" && r.LogLevel == LogLevel.Warning);
        Assert.Contains(options.Rules, r => r.ProviderName == "Console" && r.CategoryName == "ModelContextProtocol" && r.LogLevel == LogLevel.Information);
    }

    private sealed class ThrowingVerifier : IEmploymentVerifier
    {
        public Task<EmploymentCheck> VerifyAsync(string subject, string employerRef, CancellationToken ct) =>
            throw new HttpRequestException($"registry error for {employerRef} / {subject} / {Verifier}");
    }

    private sealed class BodyLoggingFilter(ILoggerFactory logs) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                logs.CreateLogger("regression").LogInformation("body: {Body}", await reader.ReadToEndAsync());
                context.Request.Body.Position = 0;
                await nextMiddleware();
            });
            next(app);
        };
    }
}
