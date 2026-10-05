using System.ComponentModel;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Records;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>Read-only, static, versioned documents the host model needs to conduct the interview and build the record.</summary>
[McpServerResourceType]
public static class InterviewResources
{
    [McpServerResource(UriTemplate = McpNames.RecordSchemaUri, Name = "record_schema_v1", Title = "Exit interview record, JSON Schema v1", MimeType = "application/schema+json")]
    [Description("The JSON Schema (draft 2020-12) every submitted record must satisfy.")]
    public static TextResourceContents RecordSchema() => new() { Uri = McpNames.RecordSchemaUri, MimeType = "application/schema+json", Text = Records.RecordSchema.JsonText };

    [McpServerResource(UriTemplate = McpNames.ProtocolUri, Name = "interview_protocol_v1", Title = "Interview protocol v1", MimeType = "application/json")]
    [Description("The interview protocol document: the fixed opening, the six topics with neutral opening questions, wording for probes, limits and rules.")]
    public static TextResourceContents Protocol() => new() { Uri = McpNames.ProtocolUri, MimeType = "application/json", Text = InterviewProtocol.CurrentJson };

    [McpServerResource(UriTemplate = McpNames.TopicsUri, Name = "topics_v1", Title = "Topics and rating anchors v1", MimeType = "text/markdown")]
    [Description("The six topics in order, what each covers, and how to set rating and confidence.")]
    public static TextResourceContents Topics() => new() { Uri = McpNames.TopicsUri, MimeType = "text/markdown", Text = InterviewInstructions.TopicsDocument(InterviewProtocol.Current) };
}
