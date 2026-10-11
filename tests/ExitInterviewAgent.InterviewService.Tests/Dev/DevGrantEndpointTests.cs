using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Interviews.CostControls;
using ExitInterviewAgent.InterviewService.Tests.Billing;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ExitInterviewAgent.InterviewService.Tests.Dev;

/// <summary>
/// The dev credit grant over HTTP (ADR-0079): Development with the fake provider mounts it, anything else does not. The
/// development service runs the scripted mock and requires credits (as the AppHost configures it), so the grant can be
/// exercised end to end: grant, then start an interview that pays with it.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class DevGrantEndpointTests : IDisposable
{
    private const string Route = "/api/v1/dev/grant-credits";
    private readonly BillingHost _host = new();

    public void Dispose() => _host.Dispose();

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<int> Balance(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/credits");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("balance").GetInt32();
    }

    [Fact]
    public async Task A_grant_adds_the_credits_and_the_balance_shows_them()
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());

        var granted = await client.PostAsJsonAsync(Route, new { quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(2, (await Json(granted)).GetProperty("balance").GetInt32());
        Assert.Equal(2, await Balance(client));
    }

    [Fact]
    public async Task A_granted_credit_pays_for_an_interview_as_a_purchased_one_would()
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());
        await client.PostAsJsonAsync(Route, new { quantity = 1 });

        var started = await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(0, await Balance(client));
    }

    [PostgresFact]
    public async Task Two_grants_on_postgres_add_up_and_the_balance_reads_them_back()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new BillingHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var client = host.As(BillingTestIds.Account());

        var first = await client.PostAsJsonAsync(Route, new { quantity = 2 });
        var second = await client.PostAsJsonAsync(Route, new { quantity = 3 });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(5, await Balance(client));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task A_quantity_outside_one_to_ten_is_refused_and_grants_nothing(int quantity)
    {
        await _host.WaitReadyAsync();
        var client = _host.As(BillingTestIds.Account());

        var response = await client.PostAsJsonAsync(Route, new { quantity });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await Balance(client));
    }

    [Fact]
    public async Task Without_an_account_the_grant_is_refused()
    {
        await _host.WaitReadyAsync();

        var response = await _host.Anonymous().PostAsJsonAsync(Route, new { quantity = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task With_the_payment_provider_off_the_route_does_not_exist()
    {
        using var host = new BillingHost(provider: "none");
        await host.WaitReadyAsync();

        var response = await host.As(BillingTestIds.Account()).PostAsJsonAsync(Route, new { quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task In_production_the_route_does_not_exist_even_with_the_fake_provider_named()
    {
        using var host = new ProductionDevHost();
        await host.WaitReadyAsync();

        var response = await host.As(BillingTestIds.Account()).PostAsJsonAsync(Route, new { quantity = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_development_host_does_not_require_a_verified_email()
    {
        await _host.WaitReadyAsync();

        // Read from appsettings.Development.json: the AppHost also sets it explicitly, so the stack does not depend on the file alone.
        var options = _host.Services.GetRequiredService<IOptions<CostControlOptions>>().Value;

        Assert.False(options.RequireVerifiedEmail);
    }
}

/// <summary>
/// The service in the Production environment with the fake payment provider named, which Production must refuse (ADR-0077). It
/// needs its ledger keys set (outside Development the service refuses to start without them). Only what the tests need is set.
/// </summary>
public sealed class ProductionDevHost : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Jwt:Issuer", ServiceFactory.Issuer);
        builder.UseSetting("Jwt:Audience", ServiceFactory.Audience);
        builder.UseSetting("Mcp:Issuer", ServiceFactory.McpIssuer);
        builder.UseSetting("Mcp:Resource", ServiceFactory.McpResource);
        builder.UseSetting("Ledger:ActiveKeyId", "test-ledger");
        builder.UseSetting("Ledger:Keys:0:Id", "test-ledger");
        builder.UseSetting("Ledger:Keys:0:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Interviews:Provider", "mock");
        builder.UseSetting("Interviews:Model", "scripted");
        builder.UseSetting("Billing:Provider", "fake");
        builder.UseSetting("Billing:PriceMinorUnits", "1000");
        builder.UseSetting("Billing:Currency", "pln");
        builder.UseSetting("Billing:SuccessUrl", "https://app.test/interview");
        builder.UseSetting("Billing:CancelUrl", "https://app.test/interview");
        builder.UseSetting("Billing:FakeWebhookSecret", BillingHost.WebhookSecret);
        builder.ConfigureServices(services =>
        {
            var key = new RsaSecurityKey(_rsa) { KeyId = ServiceFactory.KeyId };
            foreach (var scheme in new[] { JwtBearerDefaults.AuthenticationScheme, "McpBearer" })
            {
                services.PostConfigure<JwtBearerOptions>(scheme, o => o.TokenValidationParameters.IssuerSigningKey = key);
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

    /// <summary>Waits until the service answers health.</summary>
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
}
