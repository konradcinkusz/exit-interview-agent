using System.Text.Json;
using System.Text.Json.Serialization;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Submissions;
using ModelContextProtocol.Protocol;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// What the tools return. Fixed vocabulary only: a status, stable codes, schema paths and PII kinds. Nothing derived from
/// the submitted record is ever copied into a result, so a host cannot be tricked into repeating, and a log cannot
/// capture, content by way of a tool response. The receipt code is the one secret; it is returned once.
/// </summary>
internal static class McpResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public const string ReceiptNotice = "Show this receipt code to the user now. It is shown once and cannot be recovered. It is the only way to delete the stored record.";
    public const string NotStored = "Nothing was stored and the interview record was not submitted.";

    public static CallToolResult Valid() => Of(new Body("valid", Valid: true, Stored: false), isError: false);

    public static CallToolResult Invalid(Rejection rejection) => Of(Rejected("invalid", rejection, valid: false), isError: false);

    public static CallToolResult Accepted(string receiptCode) => Of(new Body("accepted", ReceiptCode: receiptCode, Notice: ReceiptNotice, Stored: true), isError: false);

    public static CallToolResult Refused(Rejection rejection) => Of(Rejected("rejected", rejection, valid: null) with { Notice = NotStored }, isError: true);

    public static CallToolResult Refused(string code) => Refused(Rejection.Of(code));

    private static Body Rejected(string status, Rejection r, bool? valid) => new(status, Valid: valid, Stored: false, Code: r.Code,
        Errors: r.Errors.Count == 0 ? null : [.. r.Errors.Select(e => new Err(e.Code, e.Path))],
        PiiKinds: r.PiiKinds.Count == 0 ? null : r.PiiKinds);

    private static CallToolResult Of(Body body, bool isError) => new()
    {
        IsError = isError,
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(body, Json) }],
    };

    private sealed record Err(string Code, string Path);

    private sealed record Body(
        string Status,
        bool? Valid = null,
        bool Stored = false,
        string? Code = null,
        IReadOnlyList<Err>? Errors = null,
        IReadOnlyList<string>? PiiKinds = null,
        string? ReceiptCode = null,
        string? Notice = null);
}
