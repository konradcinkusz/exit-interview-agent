using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Tests.Support;

namespace ExitInterviewAgent.InterviewService.Tests.CostControls;

/// <summary>
/// W4 (web-app-plan §2): the cost controls on the session endpoints. Rate limits per account and per address, the emergency
/// switch, the verified-email gate, and the global daily start cap. Each test builds its own host, so limits do not leak.
/// </summary>
public sealed class CostControlTests : IDisposable
{
    private const string StartUrl = "/api/v1/interviews";
    private static readonly object StartBody = new { language = "pl", tenure = "1y_3y" };

    private readonly List<CostHost> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
    }

    private CostHost NewHost(params (string Key, string Value)[] overrides)
    {
        var host = new CostHost().With(overrides);
        _hosts.Add(host);
        return host;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string CodeOf(JsonElement body) => body.GetProperty("code").GetString()!;

    private static int RetryAfterSeconds(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Retry-After", out var values), "The 429 or 503 carries no Retry-After header.");
        return int.Parse(values!.Single());
    }

    // ---- per account and per address rate limits ---------------------------------------------------------------------------

    [Fact]
    public async Task Starts_are_limited_per_account_and_the_refusal_says_when_to_retry()
    {
        var host = NewHost(("Interviews:RateLimits:StartsPerAccountPerHour", "2"), ("Interviews:RateLimits:StartsPerIpPerHour", "100"));
        var client = host.As("account-start-limit");

        await client.PostAsJsonAsync(StartUrl, StartBody);
        await client.PostAsJsonAsync(StartUrl, StartBody); // refused as 409 (one open session), but still an attempt
        var third = await client.PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("rate_limited", (await Json(third)).GetProperty("error").GetString());
        Assert.True(RetryAfterSeconds(third) > 0);
    }

    [Fact]
    public async Task Starts_are_limited_per_address_across_accounts()
    {
        // Every test client is the same loopback address, so this is one address behind many accounts.
        var host = NewHost(("Interviews:RateLimits:StartsPerAccountPerHour", "100"), ("Interviews:RateLimits:StartsPerIpPerHour", "2"));

        Assert.Equal(HttpStatusCode.Created, (await host.As("address-a").PostAsJsonAsync(StartUrl, StartBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.As("address-b").PostAsJsonAsync(StartUrl, StartBody)).StatusCode);
        var third = await host.As("address-c").PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(RetryAfterSeconds(third) > 0);
    }

    [Fact]
    public async Task Replies_are_limited_per_account_and_answer_429_with_retry_after()
    {
        var host = NewHost(("Interviews:RateLimits:RepliesPerAccountPerMinute", "2"), ("Interviews:RateLimits:RepliesPerIpPerMinute", "100"));
        var client = host.As("account-reply-limit");
        var id = (await Json(await client.PostAsJsonAsync(StartUrl, StartBody))).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{StartUrl}/{id}/reply", new { text = "Yes, I consent." })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{StartUrl}/{id}/reply", new { text = "Na temat pierwszym." })).StatusCode);
        var third = await client.PostAsJsonAsync($"{StartUrl}/{id}/reply", new { text = "Na temat drugim." });

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("rate_limited", (await Json(third)).GetProperty("error").GetString());
        Assert.True(RetryAfterSeconds(third) > 0);
    }

    // ---- the emergency switch -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_emergency_switch_refuses_new_sessions_with_503_and_no_credit_is_spent()
    {
        var host = NewHost(("Interviews:Enabled", "false"));

        var response = await host.As("account-switched-off").PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("interviews_disabled", CodeOf(await Json(response)));
    }

    // ---- verified email ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_account_without_a_verified_email_gets_403_email_not_verified_and_no_session()
    {
        var host = NewHost(("Interviews:RequireVerifiedEmail", "true"));
        var client = host.As("account-unverified");

        var response = await client.PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_not_verified", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task An_email_flag_that_says_false_is_refused_the_same_way()
    {
        var host = NewHost(("Interviews:RequireVerifiedEmail", "true"));
        var client = host.As("account-flag-false", new Dictionary<string, object> { ["email_verified"] = false });

        var response = await client.PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_not_verified", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task A_verified_email_starts_an_interview_when_the_gate_is_on()
    {
        var host = NewHost(("Interviews:RequireVerifiedEmail", "true"));

        var response = await host.Verified("account-verified").PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task The_gate_off_lets_an_unverified_account_start()
    {
        var host = NewHost(("Interviews:RequireVerifiedEmail", "false"));

        var response = await host.As("account-gate-off").PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- the global daily cap ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Once_the_daily_cap_is_reached_starts_answer_503_until_midnight_utc()
    {
        var host = NewHost(("Interviews:MaxStartsPerDay", "1"));
        var client = host.As("account-cap");
        var first = await client.PostAsJsonAsync(StartUrl, StartBody);
        var id = (await Json(first)).GetProperty("id").GetString()!;
        await client.DeleteAsync($"{StartUrl}/{id}"); // free the one open slot, so the cap is the only refusal

        var refused = await client.PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("interviews_disabled", CodeOf(await Json(refused)));
        // 09:00 UTC now, midnight is 15 hours away.
        Assert.Equal(15 * 3600, RetryAfterSeconds(refused));
    }

    [Fact]
    public async Task The_daily_cap_resets_at_midnight_utc()
    {
        var host = NewHost(("Interviews:MaxStartsPerDay", "1"));
        var client = host.As("account-cap-reset");
        var id = (await Json(await client.PostAsJsonAsync(StartUrl, StartBody))).GetProperty("id").GetString()!;
        await client.DeleteAsync($"{StartUrl}/{id}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(StartUrl, StartBody)).StatusCode);

        host.Clock.Advance(TimeSpan.FromHours(15) + TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(StartUrl, StartBody)).StatusCode);
    }

    [Fact]
    public async Task A_refused_start_does_not_use_up_the_daily_cap()
    {
        var host = NewHost(("Interviews:MaxStartsPerDay", "1"));
        var client = host.As("account-cap-refusal");

        var invalid = await client.PostAsJsonAsync(StartUrl, new { language = "xx", tenure = "1y_3y" });
        var valid = await client.PostAsJsonAsync(StartUrl, StartBody);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
    }

    // ---- /health -----------------------------------------------------------------------------------------------------------

    /// <summary>Polls /health until the schema is applied (the same wait the kernel's health test uses), then returns the integrations.</summary>
    private static async Task<Dictionary<string, (bool Configured, string Detail)>> Integrations(CostHost host)
    {
        var client = host.CreateClient();
        HttpResponseMessage response;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            response = await client.GetAsync("/health");
        } while (response.StatusCode != HttpStatusCode.OK && DateTime.UtcNow < deadline);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await Json(response);
        return body.GetProperty("integrations").EnumerateArray().ToDictionary(
            i => i.GetProperty("name").GetString()!,
            i => (i.GetProperty("configured").GetBoolean(), i.GetProperty("detail").GetString()!));
    }

    [Fact]
    public async Task Health_shows_the_cost_controls_as_configured()
    {
        var integrations = await Integrations(NewHost());

        Assert.True(integrations["interview-cost-controls"].Configured);
    }

    [Fact]
    public async Task Health_shows_the_cost_controls_degraded_when_the_emergency_switch_is_off()
    {
        var integrations = await Integrations(NewHost(("Interviews:Enabled", "false")));

        Assert.False(integrations["interview-cost-controls"].Configured);
        Assert.Contains("off (disabled)", integrations["interview-cost-controls"].Detail);
    }

    [Fact]
    public async Task Health_detail_names_the_gate_and_the_cap_but_never_a_secret_or_its_variable()
    {
        var integrations = await Integrations(NewHost(("Interviews:ApiKeyEnv", "NAME_OF_A_SECRET_VARIABLE"), ("Interviews:MaxStartsPerDay", "7")));

        var detail = integrations["interview-cost-controls"].Detail;
        Assert.Contains("daily start cap: 7", detail);
        Assert.Contains("verified email required: no", detail);
        Assert.DoesNotContain("NAME_OF_A_SECRET_VARIABLE", detail);
    }

    // ---- the verified flag survives claim minimisation, the address does not (ADR-0078, ADR-0014) ----------------------

    [Fact]
    public void The_verified_flag_survives_minimisation_and_the_email_address_is_still_dropped()
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim("sub", "account-minimise"),
            new System.Security.Claims.Claim("email", "person@example.invalid"),
            new System.Security.Claims.Claim("email_verified", "True"),
        ], "test"));

        var kept = ExitInterviewAgent.InterviewService.Infrastructure.Auth.McpAuthenticationExtensions.MinimizeClaims(principal);

        Assert.Equal("True", kept.FindFirst("email_verified")?.Value);
        Assert.Null(kept.FindFirst("email"));
    }
}
