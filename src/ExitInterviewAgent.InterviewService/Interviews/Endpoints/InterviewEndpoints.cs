using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Interviews.CostControls;

namespace ExitInterviewAgent.InterviewService.Interviews.Endpoints;

/// <summary>Operation names, one place (SERVICE-API-PATTERNS: named operations, greppable).</summary>
public static class InterviewEndpointNames
{
    public const string StartInterview = "StartInterview";
    public const string ReplyInterview = "ReplyInterview";
    public const string GetInterview = "GetInterview";
    public const string GetInterviewResult = "GetInterviewResult";
    public const string DeleteInterview = "DeleteInterview";
}

/// <summary>
/// The session routes, mounted in the authenticated group (<c>AuthPolicies.Account</c>). Each answer maps one-to-one onto the
/// contract's status codes; the rules live in <see cref="InterviewSessions"/>.
/// </summary>
public static class InterviewEndpoints
{
    private const string ProblemType = "urn:exit-interview-agent:problem:";

    public static RouteGroupBuilder MapInterviewSessionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/interviews", async (CreateInterviewRequest body, HttpContext http, InterviewSessions sessions, CancellationToken ct) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            var result = await sessions.StartAsync(owner, body, ct);
            return result.Value is { } started
                ? Results.Created($"/api/v1/interviews/{started.Id}", started)
                : Problem(result);
        }).WithInterviewStartControls()
          .WithName(InterviewEndpointNames.StartInterview)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status402PaymentRequired)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/interviews/{id}/reply", async (string id, InterviewReplyRequest body, HttpContext http, InterviewSessions sessions, CancellationToken ct) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            var result = await sessions.ReplyAsync(owner, id, body, ct);
            return result.Value is { } reply ? Results.Ok(reply) : Problem(result);
        }).WithInterviewReplyControls()
          .WithName(InterviewEndpointNames.ReplyInterview)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status410Gone)
          .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
          .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/interviews/{id}", (string id, HttpContext http, InterviewSessions sessions) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            var result = sessions.Get(owner, id);
            return result.Value is { } state ? Results.Ok(state) : Problem(result);
        }).WithName(InterviewEndpointNames.GetInterview)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status410Gone);

        group.MapGet("/interviews/{id}/result", (string id, HttpContext http, InterviewSessions sessions) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            var result = sessions.Result(owner, id);
            return result.Value is { } payload ? Results.Ok(payload) : Problem(result);
        }).WithName(InterviewEndpointNames.GetInterviewResult)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status410Gone);

        group.MapDelete("/interviews/{id}", async (string id, HttpContext http, InterviewSessions sessions, CancellationToken ct) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            return await sessions.DeleteAsync(owner, id, ct) ? Results.NoContent() : Problem(InterviewCodes.NotFound, StatusCodes.Status404NotFound);
        }).WithName(InterviewEndpointNames.DeleteInterview)
          .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static string? Owner(HttpContext http) => http.User.FindFirst("sub")?.Value;

    private static IResult Problem<T>(Api<T> refusal) => Problem(refusal.Code ?? InterviewCodes.InvalidRequest, refusal.Status);

    private static IResult Problem(string code, int status) =>
        Results.Problem(statusCode: status, title: code, type: ProblemType + code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
