using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// The provider for development and tests (ADR-0077): no network and no money. The checkout returns the success address at once,
/// and the webhook is verified and classified exactly as the Stripe one is (same signature scheme, same <see cref="WebhookEventParser"/>),
/// so a fake purchase runs the production path. Its secret is <c>Billing:FakeWebhookSecret</c>, or a random one per process when unset.
/// </summary>
public sealed class FakePaymentProvider(IOptions<BillingOptions> options, TimeProvider clock) : IPaymentProvider
{
    /// <summary>The secret that signs this provider's webhooks. Tests sign with it; in development only the process holds it when unset.</summary>
    public string WebhookSecret { get; } = string.IsNullOrWhiteSpace(options.Value.FakeWebhookSecret)
        ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()
        : options.Value.FakeWebhookSecret;

    public string Name => "fake";

    public Task<string> CreateCheckoutAsync(string account, int quantity, string successUrl, string cancelUrl, CancellationToken ct) =>
        Task.FromResult(successUrl + (successUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "checkout=fake");

    public PaymentEvent VerifyAndParseWebhook(ReadOnlySpan<byte> body, string? signatureHeader)
    {
        if (!StripeWebhookSignature.Verify(body, signatureHeader, WebhookSecret, clock.GetUtcNow(), options.Value.WebhookTolerance))
        {
            throw new InvalidWebhookException("bad_signature");
        }
        return WebhookEventParser.Parse(body, options.Value);
    }
}
