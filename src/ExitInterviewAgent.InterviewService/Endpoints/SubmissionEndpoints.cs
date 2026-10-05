using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Infrastructure;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.InterviewService.Endpoints;

/// <summary>
/// Submission, tickets and receipt deletion. All failures are problem details with a stable <c>code</c> extension and
/// never echo submitted text. Secrets (ticket, receipt code) are read from headers, never from the URL.
/// </summary>
public static class SubmissionEndpoints
{
    private const string ProblemType = "urn:exit-interview-agent:problem:";

    /// <summary>Authenticated account endpoints (policy <c>account</c>): submit, and mint a CLI ticket.</summary>
    public static RouteGroupBuilder MapAccountSubmissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/submissions", async (HttpContext http, SubmissionService submissions, CancellationToken ct) =>
            {
                if (http.User.FindFirst("sub")?.Value is not { Length: > 0 } sub)
                {
                    return Results.Unauthorized();
                }
                var body = await BoundedBody.ReadAsync(http.Request, RecordLimits.Default.MaxPayloadBytes, ct);
                return ToResult(body is null
                    ? SubmissionOutcome.Reject(SubmissionCodes.PayloadTooLarge)
                    : await submissions.SubmitAsync(body, sub, ct));
            })
            .WithName(EndpointNames.SubmitRecord)
            .WithSummary("Submit one interview record for the signed-in account. Returns the receipt code, once.")
            .Accepts<object>("application/json")
            .Produces<SubmissionAccepted>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/tickets", async (HttpContext http, TicketStore tickets, CancellationToken ct) =>
            {
                if (http.User.FindFirst("sub")?.Value is not { Length: > 0 } sub)
                {
                    return Results.Unauthorized();
                }
                var minted = await tickets.MintAsync(sub, ct);
                return minted is { } t
                    ? Results.Json(new TicketIssued(t.Ticket, t.ExpiresAt), statusCode: StatusCodes.Status201Created)
                    : Problem(SubmissionCodes.TicketLimit, StatusCodes.Status429TooManyRequests);
            })
            .RequireRateLimiting(AnonymousLimits.TicketMint)
            .WithName(EndpointNames.MintTicket)
            .WithSummary("Mint a single-use, short-lived CLI submission ticket. Not bound to an employer. Returned once.")
            .Produces<TicketIssued>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    /// <summary>Endpoints with no account: ticketed submission and receipt deletion. The anonymous list is in the architecture test.</summary>
    public static RouteGroupBuilder MapAnonymousSubmissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/submissions/ticketed", async (HttpContext http, SubmissionService submissions, CancellationToken ct) =>
            {
                var body = await BoundedBody.ReadAsync(http.Request, RecordLimits.Default.MaxPayloadBytes, ct);
                return ToResult(body is null
                    ? SubmissionOutcome.Reject(SubmissionCodes.PayloadTooLarge)
                    : await submissions.SubmitWithTicketAsync(body, http.Request.Headers[SubmissionHeaders.Ticket].ToString(), ct));
            })
            .AllowAnonymous()
            .RequireRateLimiting(AnonymousLimits.TicketedSubmit)
            .WithGlobalBudget(AnonymousLimits.TicketedSubmit)
            .WithName(EndpointNames.SubmitTicketed)
            .WithSummary($"Submit one record with a CLI ticket in the {SubmissionHeaders.Ticket} header. Returns the receipt code, once.")
            .Accepts<object>("application/json")
            .Produces<SubmissionAccepted>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/receipts", async (HttpContext http, ReceiptService receipts, CancellationToken ct) =>
            await receipts.DeleteAsync(http.Request.Headers[SubmissionHeaders.ReceiptCode].ToString(), ct)
                ? Results.NoContent()
                : Problem(SubmissionCodes.InvalidReceiptCode, StatusCodes.Status400BadRequest))
            .AllowAnonymous()
            .WithGlobalBudget(AnonymousLimits.ReceiptDelete)
            .WithName(EndpointNames.DeleteReceipt)
            .WithSummary($"Delete the record behind the receipt code in the {SubmissionHeaders.ReceiptCode} header. 204 for every well-formed code, found or not.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    /// <summary>
    /// TEMPORARY, development and tests only (same status as the <c>_probe</c> next to it): proves that an MCP principal
    /// reaches <see cref="SubmissionService"/> with the same account subject a web token carries, until T8 mounts the
    /// MCP submit tool on the same service. Not mapped in Production.
    /// </summary>
    public static RouteGroupBuilder MapMcpSubmissionProbe(this RouteGroupBuilder mcp)
    {
        mcp.MapPost("/_submit", async (HttpContext http, SubmissionService submissions, CancellationToken ct) =>
        {
            var body = await BoundedBody.ReadAsync(http.Request, RecordLimits.Default.MaxPayloadBytes, ct);
            return ToResult(body is null
                ? SubmissionOutcome.Reject(SubmissionCodes.PayloadTooLarge)
                : await submissions.SubmitAsync(body, http.User.FindFirst("sub")!.Value, ct));
        });
        return mcp;
    }

    public static int StatusFor(string code) => code switch
    {
        SubmissionCodes.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        SubmissionCodes.AlreadySubmitted or SubmissionCodes.InterviewIdTaken => StatusCodes.Status409Conflict,
        SubmissionCodes.TicketInvalid => StatusCodes.Status401Unauthorized,
        SubmissionCodes.EmploymentNotVerified => StatusCodes.Status403Forbidden,
        SubmissionCodes.TicketLimit => StatusCodes.Status429TooManyRequests,
        RecordErrorCodes.NotJson or RecordErrorCodes.DuplicateKey or RecordErrorCodes.NestingTooDeep => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    internal static IResult ToResult(SubmissionOutcome outcome)
    {
        if (outcome.Accepted)
        {
            return Results.Json(new SubmissionAccepted(outcome.ReceiptCode!), statusCode: StatusCodes.Status201Created);
        }
        var rejection = outcome.Rejection!;
        return Problem(rejection.Code, StatusFor(rejection.Code), rejection.Errors, rejection.PiiKinds);
    }

    private static IResult Problem(string code, int status, IReadOnlyList<FieldError>? errors = null, IReadOnlyList<string>? kinds = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (errors is { Count: > 0 })
        {
            extensions["errors"] = errors;
        }
        if (kinds is { Count: > 0 })
        {
            extensions["kinds"] = kinds;
        }
        // The title is the code itself: fixed vocabulary, nothing derived from the request.
        return Results.Problem(statusCode: status, title: code, type: ProblemType + code.ToLowerInvariant(), extensions: extensions);
    }
}

/// <summary>Reads a request body up to a hard cap without ever buffering more than the cap plus one byte.</summary>
public static class BoundedBody
{
    /// <returns>The bytes, or null when the body is larger than <paramref name="maxBytes"/>.</returns>
    public static async Task<byte[]?> ReadAsync(HttpRequest request, int maxBytes, CancellationToken ct)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
