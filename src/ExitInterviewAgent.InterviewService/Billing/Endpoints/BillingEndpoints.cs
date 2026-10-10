using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Interviews;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Billing.Endpoints;

/// <summary>Operation names, one place (SERVICE-API-PATTERNS: named operations, greppable).</summary>
public static class BillingEndpointNames
{
    public const string GetCredits = "GetCredits";
    public const string StartCheckout = "StartCheckout";
    public const string PaymentWebhook = "PaymentWebhook";
}

/// <summary>Request of <c>POST /checkout</c>: how many credits to buy (1..10).</summary>
public sealed record CheckoutRequest(int Quantity);

/// <summary>Answer of <c>GET /credits</c>: the account's balance, the sum of its ledger rows.</summary>
public sealed record CreditsResponse(int Balance);

/// <summary>Answer of <c>POST /checkout</c>: the provider-hosted page to send the payer to.</summary>
public sealed record CheckoutResponse(string Url);

/// <summary>The codes this module adds to the contract (web-app-plan §10); the others are <see cref="InterviewCodes"/>.</summary>
public static class BillingCodes
{
    public const string BillingDisabled = "billing_disabled";
    public const string BadSignature = "bad_signature";
}

/// <summary>
/// The payment routes (web-app-plan §10, ADR-0077). <c>/credits</c> and <c>/checkout</c> sit in the authenticated group; the
/// webhook is the one anonymous route of this slice, and it is the only place money becomes credits. The rules live in
/// <see cref="CreditLedger"/> and <see cref="WebhookEventParser"/>; these endpoints only map their answers.
/// </summary>
public static class BillingEndpoints
{
    /// <summary>The largest webhook body read. A real event is a few kilobytes; anything bigger is refused before it is parsed.</summary>
    public const int MaxWebhookBytes = 64 * 1024;

    /// <summary>The rate-limit policy of the webhook: generous, one window for the provider (see <see cref="BillingServiceCollectionExtensions"/>).</summary>
    public const string WebhookRateLimit = "payment-webhook";

    private const string ProblemType = "urn:exit-interview-agent:problem:";

    public static RouteGroupBuilder MapCreditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/credits", async (HttpContext http, CreditLedger ledger, CancellationToken ct) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            return Results.Ok(new CreditsResponse(await ledger.BalanceAsync(owner, ct)));
        }).WithName(BillingEndpointNames.GetCredits)
          .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/checkout", async (CheckoutRequest body, HttpContext http, IOptionsMonitor<BillingOptions> options, ILogger<CreditLedger> logger, CancellationToken ct) =>
        {
            if (Owner(http) is not { } owner) return Results.Unauthorized();
            if (body.Quantity is < 1 or > WebhookEventParser.MaxQuantity) return Problem(InterviewCodes.InvalidRequest, StatusCodes.Status400BadRequest);

            var o = options.CurrentValue;
            var provider = http.RequestServices.GetService<IPaymentProvider>();
            if (provider is null || !o.Configured(out _)) return Problem(BillingCodes.BillingDisabled, StatusCodes.Status503ServiceUnavailable);

            try
            {
                var url = await provider.CreateCheckoutAsync(owner, body.Quantity, o.SuccessUrl!, o.CancelUrl!, ct);
                return Results.Ok(new CheckoutResponse(url));
            }
            catch (PaymentProviderException e)
            {
                // The status only: the provider's text may carry payer data.
                logger.LogWarning("Checkout could not be created: provider status {Status}", e.Status);
                return Problem(InterviewCodes.ProviderUnavailable, StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName(BillingEndpointNames.StartCheckout)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    public static RouteGroupBuilder MapPaymentWebhookEndpoint(this RouteGroupBuilder publicGroup)
    {
        publicGroup.MapPost("/webhooks/payments", async (HttpContext http, CreditLedger ledger, IOptionsMonitor<BillingOptions> options, ILogger<CreditLedger> logger, CancellationToken ct) =>
        {
            var o = options.CurrentValue;
            var provider = http.RequestServices.GetService<IPaymentProvider>();
            if (provider is null || !o.Configured(out _)) return Problem(BillingCodes.BillingDisabled, StatusCodes.Status503ServiceUnavailable);

            if (http.Request.ContentLength is > MaxWebhookBytes) return Problem(InterviewCodes.InvalidRequest, StatusCodes.Status400BadRequest);
            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length > MaxWebhookBytes) return Problem(InterviewCodes.InvalidRequest, StatusCodes.Status400BadRequest);

            var signature = http.Request.Headers["Stripe-Signature"];
            PaymentEvent evt;
            try
            {
                evt = provider.VerifyAndParseWebhook(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), signature.Count > 0 ? signature.ToString() : null);
            }
            catch (InvalidWebhookException refusal)
            {
                logger.LogWarning("Payment webhook refused: {Code}", refusal.Code);
                return Problem(refusal.Code, StatusCodes.Status400BadRequest);
            }

            if (evt.Kind == PaymentEventKind.Ignored)
            {
                // Acknowledged so the provider stops retrying; the reason names the rule, never the value.
                logger.LogInformation("Payment event ignored: {Reason}", evt.IgnoredReason);
                return Results.Ok(new { received = true });
            }

            var outcome = await ledger.RecordPurchaseAsync(evt, ct);
            logger.LogInformation("Payment event recorded: {Outcome}", outcome);
            return Results.Ok(new { received = true });
        }).WithName(BillingEndpointNames.PaymentWebhook)
          .RequireRateLimiting(WebhookRateLimit)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return publicGroup;
    }

    private static string? Owner(HttpContext http) => http.User.FindFirst("sub")?.Value;

    private static IResult Problem(string code, int status) =>
        Results.Problem(statusCode: status, title: code, type: ProblemType + code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
