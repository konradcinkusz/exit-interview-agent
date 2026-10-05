using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>
/// Minimal Streamable HTTP client for tests that need the raw wire (status codes, headers, malformed input). The SDK
/// client is used where the protocol itself is under test; this one is for authentication, guard and canary scenarios.
/// A response is JSON or a server-sent-event stream; both are read to the one JSON-RPC message they carry.
/// </summary>
public static class McpWire
{
    public const string Path = "/mcp";
    public const string ListToolsBody = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

    public static HttpRequestMessage Post(string json, string path = Path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    public static HttpRequestMessage ListTools() => Post(ListToolsBody);

    /// <summary>A tools/call body. <paramref name="record"/> becomes the tool's single argument, <c>record</c>.</summary>
    public static string CallToolBody(string tool, JsonNode record, int id = 1) => CallToolBodyWithArguments(tool, new JsonObject { ["record"] = record }, id);

    public static string CallToolBodyWithArguments(string tool, JsonObject arguments, int id = 1) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = arguments },
        }.ToJsonString();

    public static async Task<JsonElement?> ReadMessageAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            text = string.Join('\n', text.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].Trim()).Where(l => l.Length > 0));
        }
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A tool call over the raw wire; the decoded text of the tool's first content block is parsed as JSON.</summary>
    public static async Task<ToolOutcome> CallToolAsync(HttpClient client, string tool, JsonNode record)
    {
        var response = await client.SendAsync(Post(CallToolBody(tool, record)));
        var message = await ReadMessageAsync(response);
        if (message is not { } m || !m.TryGetProperty("result", out var result) || !result.TryGetProperty("content", out var content))
        {
            return new ToolOutcome(response.StatusCode, null, false);
        }
        var text = content[0].GetProperty("text").GetString()!;
        var isError = result.TryGetProperty("isError", out var e) && e.GetBoolean();
        return new ToolOutcome(response.StatusCode, JsonDocument.Parse(text).RootElement.Clone(), isError);
    }

    public sealed record ToolOutcome(System.Net.HttpStatusCode Http, JsonElement? Body, bool IsError)
    {
        public string? Status => Body?.GetProperty("status").GetString();
        public string? Code => Body is { } b && b.TryGetProperty("code", out var c) ? c.GetString() : null;
        public bool Accepted => Status == "accepted";
        public string ReceiptCode => Body!.Value.GetProperty("receiptCode").GetString()!;
    }
}
