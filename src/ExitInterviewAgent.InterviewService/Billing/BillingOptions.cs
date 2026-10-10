namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// <c>Billing</c> section (web-app-plan §4, ADR-0077). <see cref="Provider"/> is <c>none</c> (checkout and webhook answer 503),
/// <c>fake</c> (development and tests: no network, no money) or <c>stripe</c>. The secrets are NEVER in configuration files: they
/// come from environment variables (<c>Billing__StripeSecretKey</c>, <c>Billing__StripeWebhookSecret</c>, <c>Billing__FakeWebhookSecret</c>)
/// and are documented as placeholders in <c>secrets.env.example</c>. The price has no default here: with no
/// <see cref="PriceMinorUnits"/> and <see cref="Currency"/> the checkout is refused (503), so no charge is ever guessed.
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>The provider id: <c>none</c> (default), <c>fake</c> or <c>stripe</c>.</summary>
    public string Provider { get; set; } = "none";

    /// <summary>Price of one credit in minor units (for example grosz). Set by configuration, after the W9 cost measurement.</summary>
    public int PriceMinorUnits { get; set; }

    /// <summary>Lower-case ISO 4217 code of the price, for example <c>pln</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>Public web address the provider returns the payer to after a successful payment (the web app's page).</summary>
    public string? SuccessUrl { get; set; }

    /// <summary>Public web address the provider returns the payer to when they leave the checkout.</summary>
    public string? CancelUrl { get; set; }

    /// <summary>Stripe API secret key. Environment only.</summary>
    public string? StripeSecretKey { get; set; }

    /// <summary>Stripe webhook signing secret (<c>whsec_...</c>). Environment only. Unset: the webhook refuses every request (fail closed).</summary>
    public string? StripeWebhookSecret { get; set; }

    /// <summary>The Stripe Price for one credit (the amount is set in Stripe; the service checks the amount it receives against the configured price).</summary>
    public string? StripePriceId { get; set; }

    /// <summary>The fake provider's webhook secret. Environment only. Unset: a random secret per process (a fake webhook can then be sent only by the process itself).</summary>
    public string? FakeWebhookSecret { get; set; }

    /// <summary>How far the signed timestamp may differ from the server clock (replay window).</summary>
    public TimeSpan WebhookTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The provider is usable: it is named, and the price, currency and return addresses are set.</summary>
    public bool Configured(out string reason)
    {
        if (Provider is not ("fake" or "stripe"))
        {
            reason = "no Billing:Provider configured: checkout and the webhook answer 503";
            return false;
        }
        if (PriceMinorUnits <= 0 || string.IsNullOrWhiteSpace(Currency))
        {
            reason = "Billing:PriceMinorUnits and Billing:Currency are not set: checkout answers 503";
            return false;
        }
        if (string.IsNullOrWhiteSpace(SuccessUrl) || string.IsNullOrWhiteSpace(CancelUrl))
        {
            reason = "Billing:SuccessUrl and Billing:CancelUrl are not set: checkout answers 503";
            return false;
        }
        reason = $"provider '{Provider}', price {PriceMinorUnits} {Currency} per credit";
        return true;
    }
}
