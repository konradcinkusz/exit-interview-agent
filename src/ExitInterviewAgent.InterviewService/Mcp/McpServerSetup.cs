using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// Registers the MCP server (ADR-0042): the official C# SDK, Streamable HTTP, stateless. Stateless means no session id, no
/// server-to-client messages and therefore no sampling, elicitation or roots: nothing in this server can depend on the
/// host offering them (brief section 4, OP-8). Tools, prompts and resources are listed by type, never discovered from
/// the assembly, so a new public member does not become part of the contract by accident.
/// </summary>
public static class McpServerSetup
{
    public static IServiceCollection AddInterviewMcp(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = McpNames.ServerName, Version = "1.0.0" };
                options.ServerInstructions = "Call the prompt conduct_exit_interview and follow it. Only the finished, user-confirmed record is submitted; the conversation itself never is.";
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, ct) =>
                context.Params?.Name is { } name && McpToolScopes.Required.ContainsKey(name) && !McpToolScopes.Permits(context.User, name)
                    ? McpResults.Refused("INSUFFICIENT_SCOPE")
                    : await next(context, ct)))
            .WithTools([typeof(InterviewTools)])
            .WithPrompts([typeof(InterviewPrompts)])
            .WithResources([typeof(InterviewResources)]);
        services.PostConfigure<LoggerFilterOptions>(ClampSdkLogging);
        return services;
    }

    /// <summary>The SDK category prefix whose Debug and Trace lines carry whole JSON-RPC messages.</summary>
    public const string SdkLogCategory = "ModelContextProtocol";

    /// <summary>
    /// At Trace the SDK logs every JSON-RPC message it sends or receives, which for this server means the submitted record
    /// and the receipt code. Verified on the pinned version (the lines are "sending message. Message: '...'" in category
    /// <c>ModelContextProtocol.Server.McpServer</c>). The floor is Information, which logs method and tool names only. It is
    /// applied after configuration is read and replaces any rule for the category, including provider-specific ones, so a
    /// diagnostic switch like <c>Logging:LogLevel:Default=Trace</c> cannot turn it back on (ContentCanaryTests, McpCanaryTests).
    /// </summary>
    public static void ClampSdkLogging(LoggerFilterOptions options)
    {
        var providers = options.Rules.Select(r => r.ProviderName).Where(p => p is not null).Distinct().ToList();
        foreach (var rule in options.Rules.Where(r => r.CategoryName?.StartsWith(SdkLogCategory, StringComparison.Ordinal) == true).ToList())
        {
            options.Rules.Remove(rule);
        }
        options.Rules.Add(new LoggerFilterRule(null, SdkLogCategory, LogLevel.Information, null));
        foreach (var provider in providers)
        {
            options.Rules.Add(new LoggerFilterRule(provider, SdkLogCategory, LogLevel.Information, null));
        }
    }
}
