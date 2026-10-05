using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.Records;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// The two tools. Their descriptions are static text compiled into the assembly and pinned by the contract snapshot test:
/// a tool description is read by the host model as trusted instruction, so it is never built from data and every change
/// is reviewed. Neither tool takes an account, an employer-to-account link or free text other than the record itself, and
/// neither echoes any of it back.
/// </summary>
[McpServerToolType]
public static class InterviewTools
{
    [McpServerTool(Name = McpNames.ValidateTool, Title = "Check an interview record", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Dry-run check of an exit-interview record against the published schema and the personal-data scan. Stores nothing and records nothing. Returns only status, error codes, schema paths and kinds of personal data found, never the text. Use it to fix mistakes before asking the user to confirm.")]
    public static async Task<CallToolResult> ValidateInterviewRecord(
        [Description("The complete record as a JSON object, exactly as it would be submitted. Format: resource exit-interview://schema/record/v1.")] JsonElement record,
        SubmissionService submissions,
        CancellationToken ct)
    {
        var outcome = await submissions.CheckAsync(RawBytes(record), ct);
        return outcome is null ? McpResults.Valid() : McpResults.Invalid(outcome);
    }

    [McpServerTool(Name = McpNames.SubmitTool, Title = "Submit an interview record", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Submits one finished exit-interview record on behalf of the signed-in user. Only call it after the user has seen the full record and explicitly confirmed. Returns a receipt code once: show it to the user immediately, it cannot be recovered. Never returns submitted text. One submission per employer per user.")]
    public static async Task<CallToolResult> SubmitInterviewRecord(
        [Description("The complete record as a JSON object, exactly as shown to and confirmed by the user. Format: resource exit-interview://schema/record/v1.")] JsonElement record,
        RequestContext<CallToolRequestParams> context,
        SubmissionService submissions,
        CancellationToken ct)
    {
        if (context.User?.FindFirst("sub")?.Value is not { Length: > 0 } sub)
        {
            return McpResults.Refused("UNAUTHENTICATED");
        }
        var outcome = await submissions.SubmitAsync(RawBytes(record), sub, ct);
        return outcome.Accepted ? McpResults.Accepted(outcome.ReceiptCode!) : McpResults.Refused(outcome.Rejection!);
    }

    /// <summary>
    /// The record exactly as it arrived on the wire (not re-serialised), so that the strict checks of the record library
    /// (duplicate keys, depth, size) see what the client sent.
    /// </summary>
    private static byte[] RawBytes(JsonElement record) => Encoding.UTF8.GetBytes(record.GetRawText());
}
