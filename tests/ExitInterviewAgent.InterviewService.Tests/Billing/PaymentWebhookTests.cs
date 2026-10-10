using System.Text;
using ExitInterviewAgent.InterviewService.Billing;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// What the webhook may turn into credits (PAYMENTS-AND-MONETIZATION §4): only a paid checkout of the expected amount, in the
/// expected currency, for 1..10 credits, becomes a purchase. Everything else is acknowledged and ignored, and nothing from the
/// body is kept except the event id, the account and the quantity. Tested on the fake provider, which shares the Stripe parser.
/// </summary>
public sealed class PaymentWebhookTests
{
    private const string Secret = "fake-unit-secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static FakePaymentProvider Provider(TimeProvider? clock = null) =>
        new(Options.Create(new BillingOptions { Provider = "fake", PriceMinorUnits = 1000, Currency = "pln", FakeWebhookSecret = Secret }), clock ?? new Clock(Now));

    private static byte[] Signed(string json) => Encoding.UTF8.GetBytes(json);

    private static string Header(string json, string secret = Secret, long? at = null) =>
        StripeWebhookSignature.Header(Encoding.UTF8.GetBytes(json), secret, at ?? Now.ToUnixTimeSeconds());

    private static string Event(string id = "evt_w1", string type = "checkout.session.completed", string paymentStatus = "paid",
        string account = "acc-w1", long amount = 2000, string currency = "pln", string credits = "2", string extra = "") =>
        """{"id":"@ID@","object":"event","type":"@TYPE@","data":{"object":{"id":"cs_@ID@","object":"checkout.session","client_reference_id":"@ACCOUNT@","amount_total":@AMOUNT@,"currency":"@CURRENCY@","payment_status":"@STATUS@","metadata":{"credits":"@CREDITS@"}@EXTRA@}}}"""
            .Replace("@ID@", id)
            .Replace("@TYPE@", type)
            .Replace("@ACCOUNT@", account)
            .Replace("@AMOUNT@", amount.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("@CURRENCY@", currency)
            .Replace("@STATUS@", paymentStatus)
            .Replace("@CREDITS@", credits)
            .Replace("@EXTRA@", extra);

    [Fact]
    public void A_paid_checkout_of_the_expected_amount_is_a_purchase()
    {
        var json = Event();

        var parsed = Provider().VerifyAndParseWebhook(Signed(json), Header(json));

        Assert.Equal(PaymentEventKind.Purchase, parsed.Kind);
        Assert.Equal("evt_w1", parsed.ProviderEventId);
        Assert.Equal("acc-w1", parsed.AccountRef);
        Assert.Equal(2, parsed.Quantity);
        Assert.Equal(2000, parsed.AmountMinorUnits);
        Assert.Equal("pln", parsed.Currency);
        Assert.Null(parsed.IgnoredReason);
    }

    [Theory]
    [InlineData("amount_mismatch", 1999, "pln", "paid")]
    [InlineData("currency_mismatch", 2000, "eur", "paid")]
    [InlineData("not_paid", 2000, "pln", "unpaid")]
    public void A_checkout_that_does_not_match_what_was_sold_is_ignored_with_a_reason(string reason, long amount, string currency, string status)
    {
        var json = Event(amount: amount, currency: currency, paymentStatus: status);

        var parsed = Provider().VerifyAndParseWebhook(Signed(json), Header(json));

        Assert.Equal(PaymentEventKind.Ignored, parsed.Kind);
        Assert.Equal(reason, parsed.IgnoredReason);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("-1")]
    public void A_quantity_outside_one_to_ten_is_ignored_even_when_signed(string credits)
    {
        var json = Event(credits: credits, amount: 1000L * (long.TryParse(credits, out var n) ? n : 0));

        var parsed = Provider().VerifyAndParseWebhook(Signed(json), Header(json));

        Assert.Equal(PaymentEventKind.Ignored, parsed.Kind);
        Assert.Equal("quantity_out_of_range", parsed.IgnoredReason);
    }

    [Fact]
    public void An_event_of_another_type_is_acknowledged_and_ignored()
    {
        var json = Event(type: "invoice.paid");

        var parsed = Provider().VerifyAndParseWebhook(Signed(json), Header(json));

        Assert.Equal(PaymentEventKind.Ignored, parsed.Kind);
        Assert.Equal("unhandled_type", parsed.IgnoredReason);
    }

    [Fact]
    public void A_body_without_a_signature_is_refused_as_bad_signature()
    {
        var json = Event();

        var refusal = Assert.Throws<InvalidWebhookException>(() => Provider().VerifyAndParseWebhook(Signed(json), null));

        Assert.Equal("bad_signature", refusal.Code);
    }

    [Fact]
    public void A_body_changed_after_signing_is_refused_as_bad_signature()
    {
        var signed = Event(amount: 2000);
        var sent = Event(amount: 1);

        var refusal = Assert.Throws<InvalidWebhookException>(() => Provider().VerifyAndParseWebhook(Signed(sent), Header(signed)));

        Assert.Equal("bad_signature", refusal.Code);
    }

    [Fact]
    public void A_signed_body_that_is_not_json_is_refused_as_invalid_request_not_as_a_signature_failure()
    {
        const string notJson = "this is not json";

        var refusal = Assert.Throws<InvalidWebhookException>(() => Provider().VerifyAndParseWebhook(Signed(notJson), Header(notJson)));

        Assert.Equal("invalid_request", refusal.Code);
    }

    [Fact]
    public void A_replayed_signature_outside_the_tolerance_is_refused()
    {
        var json = Event();
        var tenMinutesAgo = Now.AddMinutes(-10).ToUnixTimeSeconds();

        var refusal = Assert.Throws<InvalidWebhookException>(() => Provider().VerifyAndParseWebhook(Signed(json), Header(json, at: tenMinutesAgo)));

        Assert.Equal("bad_signature", refusal.Code);
    }

    [Fact]
    public void Card_and_contact_data_in_the_body_never_reach_the_parsed_event()
    {
        const string canary = "CANARY-4242424242424242-jan.kowalski@example.test";
        var json = Event(extra: ""","customer_details":{"email":"@C@"},"payment_method_details":{"card":{"last4":"4242","number":"@C@"}}""".Replace("@C@", canary));

        var parsed = Provider().VerifyAndParseWebhook(Signed(json), Header(json));

        Assert.DoesNotContain("CANARY", parsed.ToString());
        Assert.DoesNotContain("4242", parsed.ToString());
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
