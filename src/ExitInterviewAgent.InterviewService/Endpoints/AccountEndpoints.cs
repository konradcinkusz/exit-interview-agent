using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.InterviewService.Endpoints;

public static class EndpointNames
{
    public const string GetMe = "GetMe";
}

public static class AccountEndpoints
{
    /// <summary>The scaffold's one thin vertical slice: proves token validation end to end. No domain model yet.</summary>
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me", (HttpContext http) =>
        {
            var subject = http.User.FindFirst("sub")?.Value;
            return subject is null ? Results.Unauthorized() : Results.Ok(new MeResponse(subject));
        }).WithName(EndpointNames.GetMe);
        return group;
    }
}
