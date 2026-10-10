using System.Threading.RateLimiting;
using ExitInterviewAgent.InterviewService.Billing.Endpoints;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// The payments module (ADR-0077): the ledger, the two credit seams, one provider chosen by <c>Billing:Provider</c>, and the
/// webhook's rate limit. Optional and visible (P8): <c>/health</c> and the startup banner say whether checkout can run, and why not.
/// A provider is registered only when its settings are complete, so an incomplete one answers 503 <c>billing_disabled</c> instead
/// of a half-working checkout. The <c>fake</c> provider is refused in Production: it would hand out a return address with no payment.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<BillingOptions>().Bind(configuration.GetSection(BillingOptions.SectionName));
        services.AddSingleton<CreditLedger>();
        services.AddSingleton<LedgerCreditGate>();
        services.AddSingleton<LedgerCreditRefund>();

        var options = configuration.GetSection(BillingOptions.SectionName).Get<BillingOptions>() ?? new BillingOptions();
        var ready = options.Configured(out var reason);
        var providerName = options.Provider;
        if (environment.IsProduction() && providerName == "fake")
        {
            ready = false;
            reason = "the fake provider is refused in Production: checkout would return an address with no payment";
        }

        if (ready && providerName == "fake")
        {
            services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        }
        else if (ready && providerName == "stripe" && HasStripeSecrets(options))
        {
            services.AddHttpClient<IPaymentProvider, StripePaymentProvider>(c =>
                {
                    c.BaseAddress = new Uri("https://api.stripe.com/");
                    c.Timeout = TimeSpan.FromSeconds(15);
                })
                // A redirect would turn the checkout POST into a GET (SERVICE-API-PATTERNS §5): redirects are refused.
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        }
        else if (ready)
        {
            ready = false;
            reason = "Billing:Provider is 'stripe' but its secrets are not all set (StripeSecretKey, StripeWebhookSecret, StripePriceId): checkout answers 503";
        }

        services.Configure<RateLimiterOptions>(o => o.AddPolicy<string>(BillingEndpoints.WebhookRateLimit, _ =>
            RateLimitPartition.GetFixedWindowLimiter("payment-webhook", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })));

        services.AddIntegration("billing", ready, ready ? $"{reason}; checkout and the webhook are live" : reason);
        return services;
    }

    private static bool HasStripeSecrets(BillingOptions options) =>
        !string.IsNullOrEmpty(options.StripeSecretKey) && !string.IsNullOrEmpty(options.StripeWebhookSecret) && !string.IsNullOrEmpty(options.StripePriceId);
}
