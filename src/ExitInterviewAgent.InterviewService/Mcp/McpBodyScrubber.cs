using System.Text;
using System.Text.Json;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// Closed vocabulary at the transport boundary. The SDK writes the method, tool, prompt or resource URI a client asked for
/// into its log lines (Information when served, Error when not found) and into the tags of its metrics and spans, and those
/// names are client-chosen text: a host steered by injected text could use them to smuggle conversation content into the
/// operator's telemetry. So before the SDK sees a request body, any <c>method</c>, <c>params.name</c> or <c>params.uri</c>
/// outside this server's fixed set is replaced by a constant, and the SDK's ordinary "not found" handling answers it. Known
/// names pass through, and every other byte of the body, the record included, is left exactly as it was (the record
/// library's duplicate-key and depth checks depend on that). The scrub works on the token positions of a forward-only
/// reader, so it neither rebuilds the document nor trusts it to be well formed: a body that is not JSON is returned unchanged
/// and fails in the SDK's parser, whose errors carry no values.
/// </summary>
public static class McpBodyScrubber
{
    public const string UnknownName = "unknown";
    public const string UnknownMethod = "unknown/method";
    public const string UnknownUri = "exit-interview://unknown";

    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal)
    {
        "initialize", "ping", "server/discover",
        "tools/list", "tools/call", "prompts/list", "prompts/get",
        "resources/list", "resources/read", "resources/templates/list",
        "notifications/initialized", "notifications/cancelled", "notifications/progress",
    };

    private static readonly HashSet<string> Names = new(McpToolScopes.Required.Keys.Append(McpNames.ConductPrompt), StringComparer.Ordinal);
    private static readonly HashSet<string> Uris = new([McpNames.RecordSchemaUri, McpNames.ProtocolUri, McpNames.TopicsUri], StringComparer.Ordinal);

    /// <summary>The body with unknown identifiers replaced, or the same array when nothing needed replacing.</summary>
    public static byte[] Scrub(byte[] body)
    {
        var edits = new List<(int Start, int End, string Replacement)>();
        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 64 });
            var depth = 0;
            string? top = null, inner = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject or JsonTokenType.StartArray:
                        depth++;
                        break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray:
                        depth--;
                        break;
                    case JsonTokenType.PropertyName:
                        if (depth == 1) { top = reader.GetString(); inner = null; }
                        else if (depth == 2) { inner = reader.GetString(); }
                        break;
                    case JsonTokenType.String:
                        var value = reader.GetString();
                        var replacement =
                            depth == 1 && top == "method" ? Pick(value, Methods, UnknownMethod)
                            : depth == 2 && top == "params" && inner == "name" ? Pick(value, Names, UnknownName)
                            : depth == 2 && top == "params" && inner == "uri" ? Pick(value, Uris, UnknownUri)
                            : null;
                        if (replacement is not null && replacement != value)
                        {
                            edits.Add(((int)reader.TokenStartIndex, (int)reader.BytesConsumed, replacement));
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return body;
        }
        if (edits.Count == 0)
        {
            return body;
        }

        using var result = new MemoryStream(body.Length);
        var position = 0;
        foreach (var (start, end, replacement) in edits)
        {
            result.Write(body, position, start - position);
            result.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(replacement)));
            position = end;
        }
        result.Write(body, position, body.Length - position);
        return result.ToArray();
    }

    private static string? Pick(string? value, HashSet<string> known, string fallback) => value is not null && known.Contains(value) ? value : fallback;
}
