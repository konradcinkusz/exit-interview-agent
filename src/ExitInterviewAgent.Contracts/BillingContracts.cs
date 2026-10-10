namespace ExitInterviewAgent.Contracts;

// Payment routes (web-app-plan §10, ADR-0077). The BFF mirrors these shapes in web/app/lib/interview-contract.ts; a change here
// changes that file and tests/contracts in the same pull request. Responses carry counts and a provider URL, never card or contact data.

/// <summary><c>POST /api/v1/checkout</c>: how many interview credits to buy (1..10).</summary>
public sealed record CheckoutRequest(int Quantity);

/// <summary>Response to <c>GET /api/v1/credits</c>: the account's balance, the sum of its ledger rows.</summary>
public sealed record CreditsResponse(int Balance);

/// <summary>Response to <c>POST /api/v1/checkout</c>: the provider-hosted payment page to send the payer to.</summary>
public sealed record CheckoutResponse(string Url);

/// <summary>The codes of the payment routes; the session codes are <see cref="InterviewCodes"/>.</summary>
public static class BillingCodes
{
    public const string BillingDisabled = "billing_disabled";
    public const string BadSignature = "bad_signature";
}
