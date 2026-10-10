using System.Net;
using System.Security.Cryptography;
using System.Text;
using ExitInterviewAgent.InterviewService.Billing;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The signature check and the provider boundary (PAYMENTS-AND-MONETIZATION §4): constant-time HMAC over the raw bytes, a
/// timestamp tolerance, one vocabulary out, and a checkout that never leaks the provider's error text. No network: the Stripe
/// client talks to a stub handler.
/// </summary>
public sealed class PaymentProviderTests
{
    private const string Secret = "whsec_unit_test_secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <summary>The expected signature, computed here from the published scheme (t.payload, HMAC-SHA256), not by the code under test.</summary>
    private static string Expected(string body, long t)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{t}.{body}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    [Fact]
    public void A_correctly_signed_body_verifies()
    {
        const string body = """{"id":"evt_1"}""";
        var t = Now.ToUnixTimeSeconds();
        var header = $"t={t},v1={Expected(body, t)}";

        Assert.True(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes(body), header, Secret, Now, Tolerance));
    }

    [Fact]
    public void Any_one_of_several_v1_signatures_is_enough_for_rotation()
    {
        const string body = """{"id":"evt_2"}""";
        var t = Now.ToUnixTimeSeconds();
        var header = $"t={t},v1=00ff,v1={Expected(body, t)}";

        Assert.True(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes(body), header, Secret, Now, Tolerance));
    }

    [Fact]
    public void A_changed_byte_in_the_body_fails_the_check()
    {
        const string signed = """{"amount":2000}""";
        const string sent = """{"amount":1}""";
        var t = Now.ToUnixTimeSeconds();

        Assert.False(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes(sent), $"t={t},v1={Expected(signed, t)}", Secret, Now, Tolerance));
    }

    [Fact]
    public void A_wrong_secret_fails_the_check()
    {
        const string body = """{"id":"evt_3"}""";
        var t = Now.ToUnixTimeSeconds();
        var header = StripeWebhookSignature.Header(Encoding.UTF8.GetBytes(body), "some-other-secret", t);

        Assert.False(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes(body), header, Secret, Now, Tolerance));
    }

    [Fact]
    public void A_timestamp_outside_the_tolerance_fails_in_both_directions()
    {
        const string body = """{"id":"evt_4"}""";
        var stale = Now.AddMinutes(-6).ToUnixTimeSeconds();
        var future = Now.AddMinutes(6).ToUnixTimeSeconds();

        Assert.False(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes(body), $"t={stale},v1={Expected(body, stale)}", Secret, Now, Tolerance));
        Assert.False(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes(body), $"t={future},v1={Expected(body, future)}", Secret, Now, Tolerance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abcd")]
    [InlineData("t=notanumber,v1=abcd")]
    [InlineData("t=1781082000")]
    [InlineData("t=1781082000,v1=zz-not-hex")]
    public void A_missing_or_malformed_header_fails_without_throwing(string? header)
    {
        Assert.False(StripeWebhookSignature.Verify(Encoding.UTF8.GetBytes("{}"), header, Secret, Now, Tolerance));
    }

    [Fact]
    public async Task Checkout_asks_the_provider_for_a_session_with_server_side_price_and_account_and_returns_its_url()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"cs_test_1","url":"https://checkout.stripe.test/c/cs_test_1"}"""));
        var provider = Stripe(handler);

        var url = await provider.CreateCheckoutAsync("acc-42", 2, "https://app.test/ok", "https://app.test/cancel", CancellationToken.None);

        Assert.Equal("https://checkout.stripe.test/c/cs_test_1", url);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/v1/checkout/sessions", sent.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", sent.AuthScheme);
        Assert.Equal("sk_test_unit", sent.AuthParameter);
        var form = Form(sent.Body);
        Assert.Equal("payment", form["mode"]);
        Assert.Equal("acc-42", form["client_reference_id"]);
        Assert.Equal("price_unit_1", form["line_items[0][price]"]);
        Assert.Equal("2", form["line_items[0][quantity]"]);
        Assert.Equal("2", form["metadata[credits]"]);
        Assert.Equal("https://app.test/ok", form["success_url"]);
        Assert.Equal("https://app.test/cancel", form["cancel_url"]);
    }

    [Fact]
    public async Task A_provider_error_becomes_a_typed_failure_that_carries_no_provider_text()
    {
        const string providerText = "card 4242 4242 4242 4242 declined for customer jan@example.test";
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":{"message":"@TEXT@"}}""".Replace("@TEXT@", providerText)));
        var provider = Stripe(handler);

        var failure = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            provider.CreateCheckoutAsync("acc-43", 1, "https://app.test/ok", "https://app.test/cancel", CancellationToken.None));

        Assert.DoesNotContain("4242", failure.Message);
        Assert.DoesNotContain("example.test", failure.Message);
    }

    [Fact]
    public void A_stripe_webhook_with_a_valid_signature_maps_to_one_purchase_event()
    {
        var provider = Stripe(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));
        var body = Encoding.UTF8.GetBytes(BillingHost.PaidEventJson("evt_stripe_1", "acc-44", 2));

        var parsed = provider.VerifyAndParseWebhook(body, StripeWebhookSignature.Header(body, Secret, Now.ToUnixTimeSeconds()));

        Assert.Equal(PaymentEventKind.Purchase, parsed.Kind);
        Assert.Equal("evt_stripe_1", parsed.ProviderEventId);
        Assert.Equal("acc-44", parsed.AccountRef);
        Assert.Equal(2, parsed.Quantity);
    }

    private static StripePaymentProvider Stripe(StubHandler handler)
    {
        var options = Options.Create(new BillingOptions
        {
            Provider = "stripe",
            PriceMinorUnits = 1000,
            Currency = "pln",
            StripeSecretKey = "sk_test_unit",
            StripeWebhookSecret = Secret,
            StripePriceId = "price_unit_1",
        });
        return new StripePaymentProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.stripe.test/") }, options, new BillingFakeClock(Now));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> Form(string body) =>
        body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    private sealed class BillingFakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Records each request (with its body read now) and answers from a function. No network.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter, body));
            return answer(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri RequestUri, string? AuthScheme, string? AuthParameter, string Body);
}
