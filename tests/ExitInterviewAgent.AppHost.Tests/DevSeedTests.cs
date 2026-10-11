using System.Net;
using System.Text;
using ExitInterviewAgent.AppHost;

namespace ExitInterviewAgent.AppHost.Tests;

/// <summary>
/// The dashboard command "Seed test user" (DevSeed) against fake authservice and interview-service handlers: no Docker, so these run in
/// every build. They pin what a developer relies on: a fresh account gets credits, a second run changes nothing, and a failure says
/// what to look at instead of leaving the dashboard silent.
/// </summary>
public sealed class DevSeedTests
{
    private sealed class FakeServices(HttpStatusCode register, HttpStatusCode login, HttpStatusCode grant, int balance) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public int Balance { get; private set; } = balance;
        public int Granted { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add($"{request.Method} {path}");
            return path switch
            {
                "/api/v1/auth/consents/versions" => Json(HttpStatusCode.OK, """{"terms":"t1","privacy":"p1"}"""),
                "/api/v1/auth/register" => Json(register, "{}"),
                "/api/v1/auth/login" => Json(login, """{"accessToken":"token-1"}"""),
                "/api/v1/credits" => Json(HttpStatusCode.OK, $$"""{"balance":{{Balance}}}"""),
                "/api/v1/dev/grant-credits" => await GrantAsync(request),
                _ => Json(HttpStatusCode.NotFound, "{}"),
            };

            async Task<HttpResponseMessage> GrantAsync(HttpRequestMessage r)
            {
                if (grant != HttpStatusCode.OK) return Json(grant, "{}");
                Granted = System.Text.Json.JsonDocument.Parse(await r.Content!.ReadAsStringAsync(cancellationToken)).RootElement.GetProperty("quantity").GetInt32();
                Balance += Granted;
                return Json(HttpStatusCode.OK, $$"""{"balance":{{Balance}}}""");
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static Task<DevSeed.Outcome> RunAsync(FakeServices fake) =>
        DevSeed.SeedAsync(
            new HttpClient(fake, disposeHandler: false) { BaseAddress = new Uri("http://identity.test") },
            new HttpClient(fake, disposeHandler: false) { BaseAddress = new Uri("http://interviews.test") },
            DevSeed.Email, DevSeed.Password, DevSeed.TargetCredits, CancellationToken.None);

    [Fact]
    public async Task A_new_account_is_created_and_topped_up_to_the_target()
    {
        var fake = new FakeServices(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, balance: 0);

        var outcome = await RunAsync(fake);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(DevSeed.TargetCredits, fake.Balance);
        Assert.Contains("Created", outcome.Message);
        Assert.Contains(DevSeed.Email, outcome.Message);
        Assert.Contains($"credits: {DevSeed.TargetCredits}", outcome.Message);
    }

    [Fact]
    public async Task A_second_run_reuses_the_account_and_grants_nothing()
    {
        var fake = new FakeServices(HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.OK, balance: DevSeed.TargetCredits);

        var outcome = await RunAsync(fake);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(0, fake.Granted);
        Assert.DoesNotContain("POST /api/v1/dev/grant-credits", fake.Calls);
        Assert.Contains("Reused", outcome.Message);
    }

    [Fact]
    public async Task A_partly_used_balance_is_topped_up_by_the_difference()
    {
        var fake = new FakeServices(HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.OK, balance: 4);

        var outcome = await RunAsync(fake);

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(DevSeed.TargetCredits - 4, fake.Granted);
    }

    [Fact]
    public async Task An_account_that_cannot_sign_in_is_reported_with_both_answers()
    {
        var fake = new FakeServices(HttpStatusCode.Conflict, HttpStatusCode.Unauthorized, HttpStatusCode.OK, balance: 0);

        var outcome = await RunAsync(fake);

        Assert.False(outcome.Success);
        Assert.Contains("401", outcome.Message);
        Assert.Contains("409", outcome.Message);
    }

    [Fact]
    public async Task A_missing_grant_endpoint_says_what_it_needs()
    {
        var fake = new FakeServices(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound, balance: 0);

        var outcome = await RunAsync(fake);

        Assert.False(outcome.Success);
        Assert.Contains("Development", outcome.Message);
    }
}
