using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.InterviewService.Tests.Support;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ExitInterviewAgent.InterviewService.Tests.Mcp;

/// <summary>The official SDK client wired to the in-process service: what a real MCP host speaks, minus the network.</summary>
internal static class McpTestClient
{
    public static readonly Uri Endpoint = new(ServiceFactory.McpResource);

    public static Task<McpClient> ConnectAsync(HttpClient http, McpClientOptions? options = null) =>
        McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = Endpoint, TransportMode = HttpTransportMode.StreamableHttp }, http, loggerFactory: null, ownsHttpClient: false),
            options);

    public static async Task<CallToolResult> CallAsync(this McpClient client, string tool, JsonNode record) =>
        await client.CallToolAsync(tool, new Dictionary<string, object?> { ["record"] = JsonDocument.Parse(record.ToJsonString()).RootElement.Clone() });

    /// <summary>The tool result's single text block parsed as JSON (the tools' only output form).</summary>
    public static JsonElement Body(this CallToolResult result) =>
        JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement.Clone();
}
