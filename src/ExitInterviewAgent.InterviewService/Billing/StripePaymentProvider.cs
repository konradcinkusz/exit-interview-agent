using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// Stripe Checkout over plain HTTP (ADR-0077): one hosted checkout for N credits, and a signature-checked webhook. No SDK package:
/// the surface used is one form POST and one signed JSON event. The client is registered with redirects off (a redirect would turn
/// the POST into a GET, PAYMENTS-AND-MONETIZATION §4 and SERVICE-API-PATTERNS §5), and provider error text never leaves this class.
/// </summary>
public sealed class StripePaymentProvider(HttpClient http, IOptions<BillingOptions> options, TimeProvider clock) : IPaymentProvider
{
    public string Name => "stripe";

    public async Task<string> CreateCheckoutAsync(string account, int quantity, string successUrl, string cancelUrl, CancellationToken ct)
    {
        var o = options.Value;
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["client_reference_id"] = account,
            ["line_items[0][price]"] = o.StripePriceId ?? "",
            ["line_items[0][quantity]"] = quantity.ToString(CultureInfo.InvariantCulture),
            // The service's own count, carried in the signed event so the webhook can check the amount against it.
            ["metadata[credits]"] = quantity.ToString(CultureInfo.InvariantCulture),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/checkout/sessions") { Content = form };
        request.Headers.Authorization = new("Bearer", o.StripeSecretKey);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new PaymentProviderException((int)response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        if (doc.RootElement.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && url.GetString() is { Length: > 0 } link)
        {
            return link;
        }
        throw new PaymentProviderException((int)HttpStatusCode.BadGateway);
    }

    public PaymentEvent VerifyAndParseWebhook(ReadOnlySpan<byte> body, string? signatureHeader)
    {
        var o = options.Value;
        // Fail closed: no webhook secret means no webhook is trusted (PAYMENTS-AND-MONETIZATION §4).
        if (string.IsNullOrEmpty(o.StripeWebhookSecret)
            || !StripeWebhookSignature.Verify(body, signatureHeader, o.StripeWebhookSecret, clock.GetUtcNow(), o.WebhookTolerance))
        {
            throw new InvalidWebhookException("bad_signature");
        }
        return WebhookEventParser.Parse(body, o);
    }
}
