using System.Security.Claims;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>
/// An authorization handler that only observes: it records the principal the policy sees (after claim minimisation) and
/// never succeeds or fails a requirement, so the real handler still decides.
/// </summary>
public sealed class ClaimCapture : AuthorizationHandler<ScopeRequirement>
{
    public List<ClaimsPrincipal> Principals { get; } = [];

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        lock (Principals)
        {
            Principals.Add(context.User);
        }
        return Task.CompletedTask;
    }
}
