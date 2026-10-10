namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// The one seam to a payment provider (PAYMENTS-AND-MONETIZATION §2, P11): the checkout and the verified webhook. Provider event
/// names, statuses and DTOs stop at the implementation; what comes out is a <see cref="PaymentEvent"/> in this vocabulary.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>The provider id, for logs and <c>/health</c> (<c>fake</c>, <c>stripe</c>).</summary>
    string Name { get; }

    /// <summary>Creates a provider-hosted checkout for <paramref name="quantity"/> credits and returns its address. No card data passes through here.</summary>
    Task<string> CreateCheckoutAsync(string account, int quantity, string successUrl, string cancelUrl, CancellationToken ct);

    /// <summary>
    /// Verifies the signature over the raw bytes, then reads the event. Throws <see cref="InvalidWebhookException"/> when the
    /// signature is missing, wrong or stale (<c>bad_signature</c>) or the signed body is not an event (<c>invalid_request</c>).
    /// </summary>
    PaymentEvent VerifyAndParseWebhook(ReadOnlySpan<byte> body, string? signatureHeader);
}

public enum PaymentEventKind
{
    /// <summary>A paid checkout of the expected amount: credits are added once for the event id.</summary>
    Purchase = 0,

    /// <summary>Acknowledged and not turned into credits (another event type, not paid, a mismatch). See <see cref="PaymentEvent.IgnoredReason"/>.</summary>
    Ignored = 1,
}

/// <summary>
/// The normalized event. It holds the provider's event id, the account and the quantity, and the amount and currency as they
/// were received. No card data, no contact data and no body.
/// </summary>
public sealed record PaymentEvent(
    string ProviderEventId,
    PaymentEventKind Kind,
    string? AccountRef = null,
    int Quantity = 0,
    long AmountMinorUnits = 0,
    string? Currency = null,
    string? IgnoredReason = null);

/// <summary>The webhook was refused. The <see cref="Code"/> is one of the contract's stable codes; the message names no content.</summary>
public sealed class InvalidWebhookException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>A provider call failed. The message carries no provider text (it may contain payer data); only the status is kept.</summary>
public sealed class PaymentProviderException(int status) : Exception("payment provider call failed (status " + status + ")")
{
    public int Status { get; } = status;
}

/// <summary>What recording a purchase did: credits added, or the event was already known (a redelivery).</summary>
public enum PurchaseOutcome
{
    Applied = 0,
    Replay = 1,
}
