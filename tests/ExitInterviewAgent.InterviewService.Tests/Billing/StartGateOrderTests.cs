using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Billing;

namespace ExitInterviewAgent.InterviewService.Tests.Billing;

/// <summary>
/// The order of the checks on <c>POST /interviews</c> (ADR-0078, ADR-0077): the emergency switch first, then the verified-email
/// gate, then the rate limit, and the credit last, only after every other gate has passed. Each test isolates one pair of gates
/// so that the wrong order would give a different answer, and the rate-limited case proves that no credit is taken for a start
/// that was refused.
/// </summary>
public sealed class StartGateOrderTests : IDisposable
{
    private readonly List<BillingHost> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
    }

    private BillingHost NewHost(params (string Key, string? Value)[] overrides)
    {
        var host = new BillingHost();
        foreach (var (key, value) in overrides) host.Settings[key] = value;
        _hosts.Add(host);
        return host;
    }

    private static async Task<HttpResponseMessage> PostStart(HttpClient client) =>
        await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });

    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task The_emergency_switch_is_checked_before_the_rate_limit()
    {
        // The limit is one start per account per hour. With the switch off, the second start must still say "disabled", not "rate limited".
        var host = NewHost(("Interviews:Enabled", "false"), ("Interviews:RateLimits:StartsPerAccountPerHour", "1"));
        await host.WaitReadyAsync();
        var client = host.As(BillingTestIds.Account());

        var first = await PostStart(client);
        var second = await PostStart(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal("interviews_disabled", await CodeOf(first));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("interviews_disabled", await CodeOf(second));
    }

    [Fact]
    public async Task The_verified_email_gate_is_checked_before_the_rate_limit()
    {
        // An unverified account is refused with the email answer every time, even past the limit of one start per hour.
        var host = NewHost(("Interviews:RequireVerifiedEmail", "true"), ("Interviews:RateLimits:StartsPerAccountPerHour", "1"));
        await host.WaitReadyAsync();
        var client = host.As(BillingTestIds.Account());

        var first = await PostStart(client);
        var second = await PostStart(client);

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal("email_not_verified", await CodeOf(first));
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Equal("email_not_verified", await CodeOf(second));
    }

    [Fact]
    public async Task A_start_refused_by_the_rate_limit_takes_no_credit()
    {
        var host = NewHost(("Interviews:RequireVerifiedEmail", "false"), ("Interviews:RateLimits:StartsPerAccountPerHour", "1"));
        await host.WaitReadyAsync();
        var account = BillingTestIds.Account();
        var client = host.As(account);
        await host.PostPaidAsync(BillingTestIds.EventId(), account, 2);

        var admitted = await PostStart(client);
        Assert.Equal(HttpStatusCode.Created, admitted.StatusCode);
        var id = (await JsonDocument.ParseAsync(await admitted.Content.ReadAsStreamAsync())).RootElement.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/interviews/{id}")).StatusCode);

        var refused = await PostStart(client);

        Assert.Equal((HttpStatusCode)429, refused.StatusCode);
        var balance = (await JsonDocument.ParseAsync(await (await client.GetAsync("/api/v1/credits")).Content.ReadAsStreamAsync())).RootElement.GetProperty("balance").GetInt32();
        Assert.Equal(1, balance);
    }
}
