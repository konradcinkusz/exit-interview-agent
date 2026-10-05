using System.Security.Claims;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// Which scope each tool needs. The HTTP policy (<c>mcp-submit</c>) already demands <c>interview:submit</c> for the whole
/// endpoint; this second check runs per tool call so that a future read-only scope, or a loosened mount, cannot silently
/// widen what a token may do. A tool that is not in the table is refused: new tools start with no access.
/// </summary>
public static class McpToolScopes
{
    public static readonly IReadOnlyDictionary<string, string> Required = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [McpNames.ValidateTool] = McpScopes.InterviewSubmit,
        [McpNames.SubmitTool] = McpScopes.InterviewSubmit,
    };

    public static bool Permits(ClaimsPrincipal? user, string? tool)
    {
        if (user is null || tool is null || !Required.TryGetValue(tool, out var scope))
        {
            return false;
        }
        // `scope` is one space-delimited string (ADR 0005 A9), the same reading as the HTTP policy's ScopeHandler.
        return user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(scope, StringComparer.Ordinal);
    }
}
