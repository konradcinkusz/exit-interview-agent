using System.ComponentModel;
using ExitInterviewAgent.Agent.Protocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>The one prompt: the protocol, as instructions for the host model. Static text plus two reduced arguments.</summary>
[McpServerPromptType]
public static class InterviewPrompts
{
    [McpServerPrompt(Name = McpNames.ConductPrompt, Title = "Conduct an exit interview")]
    [Description("Instructions for conducting a structured exit interview with the user, who is a former employee, and for building and submitting the short record. Start here.")]
    public static GetPromptResult ConductExitInterview(
        [Description("Interview language as a two or three letter code, for example en or pl. Defaults to en.")] string? language = null,
        [Description("Optional employer name to pre-fill. Short plain text; it is confirmed with the user.")] string? employerHint = null) => new()
        {
            Description = "Exit interview protocol and record-building instructions.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = InterviewInstructions.Build(InterviewProtocol.Current, language, employerHint) },
                },
            ],
        };
}
