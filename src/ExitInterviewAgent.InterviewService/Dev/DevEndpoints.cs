using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Dev;

/// <summary>The one request body of the dev grant.</summary>
public sealed record DevGrantRequest(int Quantity);

/// <summary>Operation names of the development routes (greppable, as in the other endpoint files).</summary>
public static class DevEndpointNames
{
    public const string GrantCredits = "DevGrantCredits";
}

/// <summary>
/// <c>POST /api/v1/dev/grant-credits {quantity}</c> (ADR-0079): lets a developer practise the paid path (credits required, a session
/// consumes one) without a payment. It records a purchase row under a <c>dev-grant:</c> event id, so it is visibly not a provider
/// event, and it returns the new balance. Mounted only when <see cref="DevStack.CreditGrantEnabled"/> holds; otherwise the route
/// does not exist and answers 404. It needs an ordinary account, like the rest of the account API.
/// </summary>
public static class DevEndpoints
{
    private const string ProblemType = "urn:exit-interview-agent:problem:";

    public static WebApplication MapDevEndpoints(this WebApplication app)
    {
        var billing = app.Services.GetRequiredService<IOptions<BillingOptions>>().Value;
        if (!DevStack.CreditGrantEnabled(app.Environment, billing)) return app;

        app.MapGroup("/api/v1/dev")
            .RequireAuthorization(AuthPolicies.Account)
            .RequireRateLimiting(ApiExtensions.ApiPolicy)
            .WithValidation()
            .MapPost("/grant-credits", GrantCredits)
            .WithName(DevEndpointNames.GrantCredits)
            .ProducesProblem(StatusCodes.Status400BadRequest);
        return app;
    }

    private static async Task<IResult> GrantCredits(
        DevGrantRequest body, HttpContext http, CreditLedger ledger, IOptions<BillingOptions> options, ILogger<CreditLedger> logger, CancellationToken ct)
    {
        if (http.User.FindFirst("sub")?.Value is not { } owner) return Results.Unauthorized();
        if (body.Quantity is < 1 or > WebhookEventParser.MaxQuantity)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: InterviewCodes.InvalidRequest, type: ProblemType + InterviewCodes.InvalidRequest,
                extensions: new Dictionary<string, object?> { ["code"] = InterviewCodes.InvalidRequest });
        }

        // Configured() guarantees the currency is set whenever the grant is mounted.
        var currency = options.Value.Currency!;
        var outcome = await ledger.RecordPurchaseAsync(
            new PaymentEvent($"dev-grant:{Guid.NewGuid():N}", PaymentEventKind.Purchase, owner, body.Quantity, 0, currency), ct);
        logger.LogInformation("Development credit grant recorded: {Quantity} credit(s), {Outcome}", body.Quantity, outcome);
        return Results.Ok(new CreditsResponse(await ledger.BalanceAsync(owner, ct)));
    }
}
