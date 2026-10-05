namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// The public MCP contract's names. Renaming one is a breaking change for every connected client and shows up in the
/// contract snapshot test. Resource URIs are versioned: <c>v1</c> content is never edited in place, only superseded.
/// </summary>
public static class McpNames
{
    public const string ServerName = "exit-interview-agent";

    public const string ConductPrompt = "conduct_exit_interview";

    public const string ValidateTool = "validate_interview_record";
    public const string SubmitTool = "submit_interview_record";

    public const string RecordSchemaUri = "exit-interview://schema/record/v1";
    public const string ProtocolUri = "exit-interview://protocol/v1";
    public const string TopicsUri = "exit-interview://topics/v1";
}
