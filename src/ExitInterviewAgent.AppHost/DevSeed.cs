using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExitInterviewAgent.AppHost;

/// <summary>
/// The development test account, created through the same public APIs a browser uses (authservice register and login,
/// interview-service credits), never by writing rows: the password hash and the ledger are owned by those services. The dashboard
/// command "Seed test user" on postgres, authdb and interviewdb calls this. It is idempotent: an existing account is reused and
/// the balance is topped up to the target, so a second click changes nothing. DEV ONLY: the credentials below are public and
/// the grant endpoint exists only in Development with the fake payment provider (ADR-0079).
/// </summary>
internal static class DevSeed
{
    public const string Email = "tester@example.com";
    // authservice wants upper and lower case, a digit and a symbol. Public on purpose: it opens an empty local account.
    public const string Password = "Dev-Tester-Pass-1";
    public const int TargetCredits = 10;
    private const int MaxGrant = 10;

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>The result of one run: what to tell the developer, and whether it worked.</summary>
    public sealed record Outcome(bool Success, string Message);

    public static async Task<Outcome> SeedAsync(HttpClient identity, HttpClient interviews, string email, string password, int targetCredits, CancellationToken ct)
    {
        try
        {
            using var versionsResponse = await identity.GetAsync("/api/v1/auth/consents/versions", ct).ConfigureAwait(false);
            if (!versionsResponse.IsSuccessStatusCode) return Fail($"authservice answered {(int)versionsResponse.StatusCode} for the consent versions. Is it healthy yet?");
            var versions = await versionsResponse.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false);

            // 200 with tokens when created; any 4xx when the account exists already. Login below decides whether the account is usable.
            using var register = await identity.PostAsJsonAsync("/api/v1/auth/register", new
            {
                email,
                password,
                acceptedTermsVersion = versions.GetProperty("terms").GetString(),
                acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
                locale = "pl",
            }, Json, ct).ConfigureAwait(false);
            var created = register.IsSuccessStatusCode;

            using var login = await identity.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, Json, ct).ConfigureAwait(false);
            if (login.StatusCode != HttpStatusCode.OK)
                return Fail($"{email} could not sign in (login answered {(int)login.StatusCode}; register answered {(int)register.StatusCode}). If an account with this address was created by hand with another password, sign in with that one or reset the databases.");
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false)).GetProperty("accessToken").GetString();
            if (string.IsNullOrEmpty(token)) return Fail("login answered without an access token.");

            var balance = await BalanceAsync(interviews, token, ct).ConfigureAwait(false);
            if (balance < targetCredits)
            {
                balance = await GrantAsync(interviews, token, Math.Min(MaxGrant, targetCredits - balance), ct).ConfigureAwait(false);
            }

            return new Outcome(true, $"{(created ? "Created" : "Reused")} the test user. Sign in at http://localhost:3000/login with {email} / {password}. Interview credits: {balance}.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Fail($"{e.GetType().Name}: {e.Message}. Are authservice and interview-service running?");
        }
    }

    private static Outcome Fail(string message) => new(false, message);

    private static async Task<int> BalanceAsync(HttpClient interviews, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/credits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await interviews.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"interview-service answered {(int)response.StatusCode} for the credit balance");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false)).GetProperty("balance").GetInt32();
    }

    private static async Task<int> GrantAsync(HttpClient interviews, string token, int quantity, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dev/grant-credits") { Content = JsonContent.Create(new { quantity }, options: Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await interviews.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("the development credit grant is not mounted (it needs the Development environment and the fake payment provider)");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"the credit grant answered {(int)response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false)).GetProperty("balance").GetInt32();
    }
}
