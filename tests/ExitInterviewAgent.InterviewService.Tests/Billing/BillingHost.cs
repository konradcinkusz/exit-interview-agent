using System.Security.Cryptography;
using System.Text;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The service in-process for the credit and payment tests (W3). The payment provider is the fake one, whose webhook secret is
/// known to the test; the interview provider is the scripted mock. Credits are required, as in production. Pass a PostgreSQL
/// connection string to run the same host against the migrated database. Every test uses its own accounts and event ids: the
/// InMemory store is shared by name across hosts in one process.
/// </summary>
public sealed class BillingHost : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "billing-test-webhook-secret";
    public const int PriceMinorUnits = 1000;
    public const string Currency = "pln";
    public const string SuccessUrl = "https://app.test/interview/paid";
    public const string CancelUrl = "https://app.test/interview/cancelled";

    private readonly RSA _rsa = RSA.Create(2048);

    public BillingHost(string provider = "fake", string? postgres = null)
    {
        Provider = provider;
        Postgres = postgres;
    }

    public string Provider { get; }
    public string? Postgres { get; }
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    public CaptureLoggerProvider Logs { get; } = new();

    /// <summary>When set, the interview model fails on every call (a fatal failure): the session ends as failed and its credit comes back.</summary>
    public Microsoft.Extensions.AI.IChatClient? ModelOverride { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Issuer", ServiceFactory.Issuer);
        builder.UseSetting("Jwt:Audience", ServiceFactory.Audience);
        builder.UseSetting("Mcp:Issuer", ServiceFactory.McpIssuer);
        builder.UseSetting("Mcp:Resource", ServiceFactory.McpResource);
        builder.UseSetting("Interviews:Provider", "mock");
        builder.UseSetting("Interviews:Model", "scripted");
        builder.UseSetting("Interviews:RequireCredit", "true");
        builder.UseSetting("Billing:Provider", Provider);
        builder.UseSetting("Billing:PriceMinorUnits", PriceMinorUnits.ToString());
        builder.UseSetting("Billing:Currency", Currency);
        builder.UseSetting("Billing:SuccessUrl", SuccessUrl);
        builder.UseSetting("Billing:CancelUrl", CancelUrl);
        builder.UseSetting("Billing:FakeWebhookSecret", WebhookSecret);
        if (Postgres is not null) builder.UseSetting("ConnectionStrings:interviewdb", Postgres);

        builder.ConfigureServices(services =>
        {
            var key = new RsaSecurityKey(_rsa) { KeyId = ServiceFactory.KeyId };
            foreach (var scheme in new[] { JwtBearerDefaults.AuthenticationScheme, "McpBearer" })
            {
                services.PostConfigure<JwtBearerOptions>(scheme, o => o.TokenValidationParameters.IssuerSigningKey = key);
            }
            services.AddSingleton<TimeProvider>(Clock);
            services.AddLogging(l => l.AddProvider(Logs));
            if (ModelOverride is { } model)
            {
                services.AddSingleton<IInterviewModelFactory>(new FixedModel(model));
            }
        });
    }

    /// <summary>An HTTP client that calls the API as the given account.</summary>
    public HttpClient As(string account)
    {
        var client = CreateClient();
        var token = new TokenBuilder(_rsa) { Subject = account, Issuer = ServiceFactory.Issuer, Audience = ServiceFactory.Audience, Type = "JWT" }.Build();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    /// <summary>An anonymous client, as the payment provider calls the webhook.</summary>
    public HttpClient Anonymous() => CreateClient();

    /// <summary>The provider's event for a paid checkout of <paramref name="credits"/> credits, signed with the test secret at the fake clock's time.</summary>
    public static string PaidEventJson(string eventId, string account, int credits, long? amount = null, string currency = Currency, string paymentStatus = "paid") =>
        """{"id":"@ID@","object":"event","type":"checkout.session.completed","data":{"object":{"id":"cs_@ID@","object":"checkout.session","client_reference_id":"@ACCOUNT@","amount_total":@AMOUNT@,"currency":"@CURRENCY@","payment_status":"@STATUS@","metadata":{"credits":"@CREDITS@"}}}}"""
            .Replace("@ID@", eventId)
            .Replace("@ACCOUNT@", account)
            .Replace("@AMOUNT@", (amount ?? PriceMinorUnits * credits).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("@CURRENCY@", currency)
            .Replace("@STATUS@", paymentStatus)
            .Replace("@CREDITS@", credits.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The signature header for a body, as the provider sends it (Stripe-Signature format) at the fake clock's time.</summary>
    public string SignatureFor(string body, string secret = WebhookSecret) =>
        StripeWebhookSignature.Header(Encoding.UTF8.GetBytes(body), secret, Clock.GetUtcNow().ToUnixTimeSeconds());

    /// <summary>Posts a provider event to the anonymous webhook with a valid signature.</summary>
    public async Task<HttpResponseMessage> PostPaidAsync(string eventId, string account, int credits)
    {
        var body = PaidEventJson(eventId, account, credits);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", SignatureFor(body));
        return await Anonymous().SendAsync(request);
    }

    /// <summary>Waits until the schema is applied (health answers 200 once the migrations have run; PostgreSQL runs them at start).</summary>
    public async Task WaitReadyAsync()
    {
        var client = CreateClient();
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if ((await client.GetAsync("/health")).IsSuccessStatusCode) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("The service did not become ready.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rsa.Dispose();
        base.Dispose(disposing);
    }

    private sealed class FixedModel(Microsoft.Extensions.AI.IChatClient client) : IInterviewModelFactory
    {
        public InterviewModel Create() => new(client, null);
    }
}

/// <summary>Helpers shared by the billing tests: a random account and a random event id, so tests never share rows.</summary>
public static class BillingTestIds
{
    public static string Account() => "acc-" + Guid.NewGuid().ToString("N");
    public static string EventId() => "evt_" + Guid.NewGuid().ToString("N");
}
