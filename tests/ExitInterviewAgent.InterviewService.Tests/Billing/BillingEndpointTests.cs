using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Tests.Interviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The W3 endpoints over HTTP (web-app-plan §10): the balance, checkout, the anonymous signed webhook, and the credit gate on
/// starting an interview (402 without a credit, one credit consumed, a failed session refunded, a withdrawn one not).
/// </summary>
public sealed class BillingEndpointTests : IDisposable
{
    private readonly BillingHost _host = new();

    public void Dispose() => _host.Dispose();

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string CodeOf(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static async Task<int> Balance(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/credits");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("balance").GetInt32();
    }

    private static async Task<HttpResponseMessage> PostStart(HttpClient client) =>
        await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });

    [Fact]
    public async Task A_new_account_has_no_credits()
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());

        Assert.Equal(0, await Balance(client));
    }

    [Fact]
    public async Task A_signed_paid_event_adds_its_credits_and_a_redelivery_adds_nothing()
    {
        await _host.WaitReadyAsync();
        var account = BillingTestIds.Account();
        var eventId = BillingTestIds.EventId();
        var client = _host.As(account);

        var first = await _host.PostPaidAsync(eventId, account, 2);
        var replay = await _host.PostPaidAsync(eventId, account, 2);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(2, await Balance(client));
    }

    [Fact]
    public async Task A_webhook_with_a_wrong_signature_is_refused_and_adds_nothing()
    {
        await _host.WaitReadyAsync();
        var account = BillingTestIds.Account();
        var body = BillingHost.PaidEventJson(BillingTestIds.EventId(), account, 3);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", _host.SignatureFor(body, secret: "not-the-secret"));

        var response = await _host.Anonymous().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_signature", CodeOf(await Json(response)));
        Assert.Equal(0, await Balance(_host.As(account)));
    }

    [Fact]
    public async Task A_webhook_without_any_signature_is_refused()
    {
        await _host.WaitReadyAsync();
        var body = BillingHost.PaidEventJson(BillingTestIds.EventId(), BillingTestIds.Account(), 1);

        var response = await _host.Anonymous().PostAsync("/api/v1/webhooks/payments", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("bad_signature", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task A_paid_event_for_an_unpaid_checkout_is_acknowledged_but_adds_nothing()
    {
        await _host.WaitReadyAsync();
        var account = BillingTestIds.Account();
        var body = BillingHost.PaidEventJson(BillingTestIds.EventId(), account, 1, paymentStatus: "unpaid");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", _host.SignatureFor(body));

        var response = await _host.Anonymous().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await Balance(_host.As(account)));
    }

    [Fact]
    public async Task An_interview_without_a_credit_answers_402_and_starts_nothing()
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());

        var response = await PostStart(client);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal("payment_required", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task Starting_an_interview_takes_one_credit_and_withdrawing_it_gives_nothing_back()
    {
        await _host.WaitReadyAsync();
        var account = BillingTestIds.Account();
        await _host.PostPaidAsync(BillingTestIds.EventId(), account, 1);
        var client = _host.As(account);

        var started = await PostStart(client);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = (await Json(started)).GetProperty("id").GetString()!;
        Assert.Equal(0, await Balance(client));

        var withdrawn = await client.DeleteAsync($"/api/v1/interviews/{id}");
        Assert.Equal(HttpStatusCode.NoContent, withdrawn.StatusCode);

        Assert.Equal(0, await Balance(client));
        Assert.Equal("payment_required", CodeOf(await Json(await PostStart(client))));
    }

    [Fact]
    public async Task A_session_that_fails_on_the_service_side_returns_its_credit()
    {
        _host.ModelOverride = new FailingChatClient();
        await _host.WaitReadyAsync();
        var account = BillingTestIds.Account();
        await _host.PostPaidAsync(BillingTestIds.EventId(), account, 1);
        var client = _host.As(account);
        // The opening turn is scripted, so the start succeeds and the provider failure surfaces on the first model call (the reply).
        var started = await PostStart(client);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var id = (await Json(started)).GetProperty("id").GetString()!;
        Assert.Equal(0, await Balance(client));

        var reply = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Yes, I consent." });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.StatusCode);
        Assert.Equal("provider_unavailable", CodeOf(await Json(reply)));
        var balance = 0;
        for (var attempt = 0; attempt < 500 && balance == 0; attempt++)
        {
            balance = await Balance(client);
            if (balance == 0) await Task.Delay(10);
        }
        Assert.Equal(1, balance);
    }

    [Fact]
    public async Task Checkout_with_no_provider_answers_503_billing_disabled()
    {
        using var disabled = new BillingHost(provider: "none");
        await disabled.WaitReadyAsync();
        var client = disabled.As(BillingTestIds.Account());

        var response = await client.PostAsJsonAsync("/api/v1/checkout", new { quantity = 1 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("billing_disabled", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task Checkout_with_no_provider_refuses_the_webhook_too()
    {
        using var disabled = new BillingHost(provider: "none");
        await disabled.WaitReadyAsync();
        var body = BillingHost.PaidEventJson(BillingTestIds.EventId(), BillingTestIds.Account(), 1);

        var response = await disabled.Anonymous().PostAsync("/api/v1/webhooks/payments", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("billing_disabled", CodeOf(await Json(response)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public async Task Checkout_refuses_a_quantity_outside_one_to_ten(int quantity)
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());

        var response = await client.PostAsJsonAsync("/api/v1/checkout", new { quantity });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task Checkout_returns_the_provider_url_for_a_valid_quantity()
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());

        var response = await client.PostAsJsonAsync("/api/v1/checkout", new { quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = (await Json(response)).GetProperty("url").GetString()!;
        Assert.StartsWith(BillingHost.SuccessUrl, url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_card_data_or_contact_details_reach_the_database_or_the_logs()
    {
        await _host.WaitReadyAsync();
        const string canary = "CANARY-4242424242424242-jan.kowalski@example.test";
        var account = BillingTestIds.Account();
        var eventId = BillingTestIds.EventId();
        var body = """{"id":"@ID@","object":"event","type":"checkout.session.completed","data":{"object":{"id":"cs_x","client_reference_id":"@ACCOUNT@","amount_total":2000,"currency":"pln","payment_status":"paid","metadata":{"credits":"2"},"customer_details":{"email":"@C@"},"payment_method_details":{"card":{"number":"@C@","last4":"4242"}}}}}"""
            .Replace("@ID@", eventId).Replace("@ACCOUNT@", account).Replace("@C@", canary);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", _host.SignatureFor(body));
        var response = await _host.Anonymous().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        var stored = JsonSerializer.Serialize(new
        {
            credits = await db.CreditEntries.AsNoTracking().ToListAsync(),
            payments = await db.PaymentEvents.AsNoTracking().ToListAsync(),
        });

        Assert.Contains(eventId, stored);
        Assert.DoesNotContain("CANARY", stored);
        Assert.DoesNotContain("4242424242424242", stored);
        Assert.DoesNotContain("kowalski", stored);
        Assert.DoesNotContain("CANARY", string.Join('\n', _host.Logs.Lines));
        Assert.DoesNotContain("4242424242424242", string.Join('\n', _host.Logs.Lines));
    }
}
