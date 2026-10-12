using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ExitInterviewAgent.AppHost.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExitInterviewAgent.AppHost.Tests;

/// <summary>
/// The whole development stack, started by the AppHost and driven the way a browser and a developer would drive it (ADR-0081).
/// Each scenario owns its account and its interview, so they share one stack and run in any order. Every scenario says what it
/// protects in its name; a failure carries the last log lines of each resource (see <see cref="Stack.DiagnoseOnFailureAsync"/>).
/// </summary>
public sealed class FullStackTests(StackFixture fixture) : IClassFixture<StackFixture>
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>
    /// The answers the scripted interviewer takes in Polish. None is a stop phrase or a streak of "I do not know", and one carries
    /// the unique marker that the database check looks for. The list cycles if the interview asks for more.
    /// </summary>
    private static readonly string[] Answers =
    [
        "Krótko: zadania były niejasne.",
        "Przełożony był rzadko dostępny.",
        "Jedno spotkanie 1:1 odwołano trzy razy z rzędu.",
        "Ogólnie zespół pomagał.",
        "Tak.",
        "Polecam poprawić onboarding.",
        "Dziękuję, to wszystko.",
    ];

    private const string Consent = "Tak, zgadzam się.";

    [FullStackFact]
    public async Task Every_resource_is_up_and_answers_its_probe()
    {
        var stack = await fixture.StackAsync();
        await stack.DiagnoseOnFailureAsync(async () =>
        {
            using var identity = stack.NewClient(stack.Authority);
            using var ready = await identity.GetAsync("/health/ready");
            Assert.True(ready.IsSuccessStatusCode, "authservice /health/ready: " + await ready.BodyAsync());
            using var api = stack.NewClient(stack.InterviewService);
            using var health = await api.GetAsync("/health");
            await health.EnsureStatusAsync(HttpStatusCode.OK, "interview-service /health");
            Assert.Contains("interview-model", await health.Content.ReadAsStringAsync()); // the model row is listed (ADR-0079)
            using var web = stack.NewClient(stack.Web);
            using var probe = await web.GetAsync("/healthz");
            await probe.EnsureStatusAsync(HttpStatusCode.OK, "web /healthz");
            var model = stack.App.Services.GetRequiredService<Aspire.Hosting.ApplicationModel.DistributedApplicationModel>();
            var names = model.Resources.Select(r => r.Name).ToArray();
            foreach (var expected in new[] { "postgres", "pgadmin", "authservice", "interview-service", "web" })
            {
                Assert.Contains(expected, names);
            }
        });
    }

    [FullStackFact]
    public async Task The_seeded_test_user_signs_in_with_credits_and_a_second_seed_changes_nothing()
    {
        var stack = await fixture.StackAsync();
        await stack.DiagnoseOnFailureAsync(async () =>
        {
            using var identity = stack.NewClient(stack.Authority);
            using var interviews = stack.NewClient(stack.InterviewService);
            var first = await ExitInterviewAgent.AppHost.DevSeed.SeedAsync(identity, interviews, ExitInterviewAgent.AppHost.DevSeed.Email, ExitInterviewAgent.AppHost.DevSeed.Password, ExitInterviewAgent.AppHost.DevSeed.TargetCredits, CancellationToken.None);
            Assert.True(first.Success, first.Message);

            using var web = stack.NewClient(stack.Web);
            var session = await SignInAsync(web, ExitInterviewAgent.AppHost.DevSeed.Email, ExitInterviewAgent.AppHost.DevSeed.Password);
            Assert.Equal(ExitInterviewAgent.AppHost.DevSeed.TargetCredits, await BalanceAsync(web, session));

            var second = await ExitInterviewAgent.AppHost.DevSeed.SeedAsync(identity, interviews, ExitInterviewAgent.AppHost.DevSeed.Email, ExitInterviewAgent.AppHost.DevSeed.Password, ExitInterviewAgent.AppHost.DevSeed.TargetCredits, CancellationToken.None);
            Assert.True(second.Success, second.Message);
            Assert.Equal(ExitInterviewAgent.AppHost.DevSeed.TargetCredits, await BalanceAsync(web, session));
        });
    }

    [FullStackFact]
    public async Task An_account_registers_at_authservice_and_signs_in_through_the_bff()
    {
        var stack = await fixture.StackAsync();
        await stack.DiagnoseOnFailureAsync(async () =>
        {
            var (email, password) = await RegisterAsync(stack);
            using var web = stack.NewClient(stack.Web);

            var session = new BffSession();
            using var login = await SendAsync(web, session, HttpMethod.Post, "/api/auth/login", new { email, password });
            await login.EnsureStatusAsync(HttpStatusCode.OK, "BFF login");
            Assert.False(string.IsNullOrEmpty(session.Get(BffSession.Names.Access)), "the access token is an HttpOnly cookie");
            Assert.False(string.IsNullOrEmpty(session.Get(BffSession.Names.Refresh)), "the refresh token is an HttpOnly cookie");
            Assert.False(string.IsNullOrEmpty(session.Get(BffSession.Names.Consent)), "the terms accepted at sign-up are in force");

            // The page's call through the BFF carries the cookie, and the service answers for the verified subject.
            using var me = await SendAsync(web, session, HttpMethod.Get, "/api/proxy/v1/me");
            await me.EnsureStatusAsync(HttpStatusCode.OK, "GET /me through the BFF");
            Assert.Equal(JsonValueKind.Object, (await ReadJsonAsync(me)).ValueKind);

            // A wrong password is refused and sets no session.
            var refused = new BffSession();
            using var wrong = await SendAsync(web, refused, HttpMethod.Post, "/api/auth/login", new { email, password = password + "x" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Null(refused.Get(BffSession.Names.Access));
        });
    }

    [FullStackFact]
    public async Task A_web_interview_runs_end_to_end_through_the_bff_and_is_deleted()
    {
        var stack = await fixture.StackAsync();
        await stack.DiagnoseOnFailureAsync(async () =>
        {
            using var web = stack.NewClient(stack.Web);
            var (session, _) = await SignedInWithCreditAsync(stack, web, credits: 1);

            var (id, _) = await RunInterviewToResultAsync(stack, web, session, marker: null);

            // The result carries the record, the generic tiles and the platform drafts, as the web page shows them (ADR-0083, WP4). The Facts
            // tile is built by code; the others come from the writer, and a short interview can legitimately yield fewer (the product drops a
            // tile it will not show, and says so in "dropped"). Items carry the generic kinds only. The family tiles reach the page only as
            // drafts, so a family that has no draft must be accounted for by a dropped code, never silently absent.
            using var result = await SendAsync(web, session, HttpMethod.Get, $"/api/proxy/v1/interviews/{id}/result");
            var payload = await ReadJsonAsync(result);
            Assert.True(payload.TryGetProperty("record", out var record), "the result carries the record");
            Assert.Equal(JsonValueKind.Object, record.ValueKind);
            var tiles = payload.GetProperty("tiles");
            var kinds = tiles.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("kind").GetString()!).ToArray();
            var droppedCount = tiles.GetProperty("dropped").GetArrayLength();
            var generic = new[] { "facts", "overview", "what_worked", "what_could_improve", "for_the_next_person" };
            Assert.NotEmpty(kinds);
            Assert.Contains("facts", kinds);
            Assert.All(kinds, kind => Assert.Contains(kind, generic));
            Assert.True(kinds.Contains("overview") || droppedCount > 0,
                $"the tile overview is neither shown nor accounted for by a dropped code (shown: {string.Join(", ", kinds)})");

            // The drafts: one per enabled profile whose family tile was accepted. A result with no draft says so only through a dropped code.
            // The ids are the enabled profiles of platform-profiles.v1.json (kununu ships disabled).
            var enabledPlatforms = new[] { "glassdoor", "indeed", "gowork", "linkedin", "facebook_group", "reddit", "wykop", "blind", "x" };
            var drafts = tiles.GetProperty("drafts").EnumerateArray().ToArray();
            Assert.True(drafts.Length > 0 || droppedCount > 0,
                $"no platform draft and no dropped code (shown: {string.Join(", ", kinds)})");
            foreach (var draft in drafts)
            {
                Assert.Contains(draft.GetProperty("platform").GetString()!, enabledPlatforms);
            }
            Assert.False(string.IsNullOrWhiteSpace(tiles.GetProperty("notice").GetString()), "the tiles carry the legal notice");

            // Delete, then the id is unknown: the BFF answers 404 (the stage answers are never kept after a delete).
            using var deleted = await SendAsync(web, session, HttpMethod.Delete, $"/api/proxy/v1/interviews/{id}");
            await deleted.EnsureStatusAsync(HttpStatusCode.NoContent, "DELETE the interview");
            using var gone = await SendAsync(web, session, HttpMethod.Get, $"/api/proxy/v1/interviews/{id}");
            await gone.EnsureStatusAsync(HttpStatusCode.NotFound, "GET after delete");
        });
    }

    [FullStackFact]
    public async Task The_credit_gate_refuses_a_start_without_credit_and_the_balance_matches_the_interview()
    {
        var stack = await fixture.StackAsync();
        await stack.DiagnoseOnFailureAsync(async () =>
        {
            using var web = stack.NewClient(stack.Web);
            var (email, password) = await RegisterAsync(stack);
            var session = await SignInAsync(web, email, password);

            using var refused = await SendAsync(web, session, HttpMethod.Post, "/api/proxy/v1/interviews", StartBody);
            await refused.EnsureStatusAsync(HttpStatusCode.PaymentRequired, "start without credit");
            Assert.Contains("payment_required", await refused.Content.ReadAsStringAsync());
            Assert.Equal(0, await BalanceAsync(web, session));

            Assert.Equal(1, await GrantCreditAsync(stack, session, 1));
            Assert.Equal(1, await BalanceAsync(web, session));

            var (id, _) = await StartAsync(web, session);
            Assert.Equal(0, await BalanceAsync(web, session));

            await FinishAsync(web, session, id, marker: null);
            Assert.Equal(0, await BalanceAsync(web, session));
        });
    }

    [FullStackFact]
    public async Task The_interview_database_has_the_migrated_tables_and_no_interview_text()
    {
        var stack = await fixture.StackAsync();
        await stack.DiagnoseOnFailureAsync(async () =>
        {
            using var web = stack.NewClient(stack.Web);
            var marker = "L6MARK" + Guid.NewGuid().ToString("N");
            var (session, _) = await SignedInWithCreditAsync(stack, web, credits: 1);
            await RunInterviewToResultAsync(stack, web, session, marker);

            await using var db = await stack.OpenInterviewDbAsync();
            await WaitForMigrationsAsync(db);
            var tables = await BaseTablesAsync(db);
            foreach (var expected in new[] { "CreditEntries", "PaymentEvents", "SessionSettlements", "Records", "SubmissionLedger", "Receipts", "SubmissionTickets" })
            {
                Assert.Contains(expected, tables);
            }
            Assert.True(await ScalarAsync(db, "SELECT count(*) FROM \"CreditEntries\"") > 0, "the grant and the start are in the credit ledger");

            // The interview's text (the marker is in one of the answers) appears in no table. The answers are held in memory
            // only (ADR-0076), so a row carrying them would be a privacy regression.
            foreach (var table in tables)
            {
                var found = await ScalarAsync(db,
                    $"SELECT count(*) FROM \"{table.Replace("\"", "\"\"")}\" AS t WHERE position(@m in t::text) > 0",
                    new NpgsqlParameter("m", marker));
                Assert.True(found == 0, $"the interview text must not be stored in {table}, rows found: {found}");
            }
        });
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static readonly object StartBody = new { language = "pl", tenure = "1y_3y" };

    /// <summary>Registers an account at authservice (its own endpoint), with the terms and privacy versions it publishes.</summary>
    private static async Task<(string Email, string Password)> RegisterAsync(Stack stack)
    {
        using var identity = stack.NewClient(stack.Authority);
        using var versionsResponse = await identity.GetAsync("/api/v1/auth/consents/versions");
        await versionsResponse.EnsureStatusAsync(HttpStatusCode.OK, "consent versions");
        var versions = await ReadJsonAsync(versionsResponse);

        var email = $"l6-{Guid.NewGuid():N}@example.invalid";
        // authservice requires a digit and a non-alphanumeric character: both are fixed here, the rest is random.
        var password = "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        using var register = await identity.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            acceptedTermsVersion = versions.GetProperty("terms").GetString(),
            acceptedPrivacyVersion = versions.GetProperty("privacy").GetString(),
            locale = "pl",
        }, Json);
        // 200 with tokens, or 202 when a verification mail must be confirmed first. This stack sends no mail, so 200 is expected.
        if (!register.IsSuccessStatusCode) await register.EnsureStatusAsync(HttpStatusCode.OK, "register at authservice");
        return (email, password);
    }

    private static async Task<BffSession> SignInAsync(HttpClient web, string email, string password)
    {
        var session = new BffSession();
        using var login = await SendAsync(web, session, HttpMethod.Post, "/api/auth/login", new { email, password });
        if (login.StatusCode != HttpStatusCode.OK)
        {
            // The same web-to-authservice path, with a GET that needs no account: tells a network failure from a failure of the POST.
            using var probe = await SendAsync(web, new BffSession(), HttpMethod.Get, "/api/auth/consent-versions");
            throw new InvalidOperationException(
                $"BFF login: expected 200, got {(int)login.StatusCode}. Body: {await login.BodyAsync()}. " +
                $"Probe, the BFF's GET /api/auth/consent-versions (web to authservice): {(int)probe.StatusCode} {await probe.BodyAsync()}");
        }
        return session;
    }

    /// <summary>A new account, signed in through the BFF, with the given number of development credits granted.</summary>
    private static async Task<(BffSession Session, string Email)> SignedInWithCreditAsync(Stack stack, HttpClient web, int credits)
    {
        var (email, password) = await RegisterAsync(stack);
        var session = await SignInAsync(web, email, password);
        if (credits > 0) Assert.Equal(credits, await GrantCreditAsync(stack, session, credits));
        return (session, email);
    }

    /// <summary>The development grant, called straight on interview-service with the access token the BFF holds (ADR-0079).</summary>
    private static async Task<int> GrantCreditAsync(Stack stack, BffSession session, int quantity)
    {
        using var api = stack.NewClient(stack.InterviewService);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dev/grant-credits")
        {
            Content = JsonContent.Create(new { quantity }, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await api.SendAsync(request);
        await response.EnsureStatusAsync(HttpStatusCode.OK, "dev credit grant");
        return (await ReadJsonAsync(response)).GetProperty("balance").GetInt32();
    }

    private static async Task<int> BalanceAsync(HttpClient web, BffSession session)
    {
        using var response = await SendAsync(web, session, HttpMethod.Get, "/api/proxy/v1/credits");
        await response.EnsureStatusAsync(HttpStatusCode.OK, "GET credits");
        return (await ReadJsonAsync(response)).GetProperty("balance").GetInt32();
    }

    private static async Task<(string Id, JsonElement Started)> StartAsync(HttpClient web, BffSession session)
    {
        using var response = await SendAsync(web, session, HttpMethod.Post, "/api/proxy/v1/interviews", StartBody);
        await response.EnsureStatusAsync(HttpStatusCode.Created, "start the interview");
        var started = await ReadJsonAsync(response);
        Assert.Equal("pl", started.GetProperty("language").GetString());
        Assert.False(string.IsNullOrWhiteSpace(started.GetProperty("turn").GetProperty("text").GetString()), "the opening turn has text");
        return (started.GetProperty("id").GetString()!, started);
    }

    /// <summary>
    /// Starts an interview, gives consent, answers until the interviewer closes, and waits for the record. The marker, when given,
    /// is put in the second answer. Returns the id and the result payload.
    /// </summary>
    private static async Task<(string Id, JsonElement Result)> RunInterviewToResultAsync(Stack stack, HttpClient web, BffSession session, string? marker)
    {
        var (id, _) = await StartAsync(web, session);
        await FinishAsync(web, session, id, marker);
        using var response = await PollResultAsync(web, session, id);
        return (id, await ReadJsonAsync(response));
    }

    /// <summary>Consent, then the answers, until the interviewer's closing turn. Fails if it does not close within the budget.</summary>
    private static async Task FinishAsync(HttpClient web, BffSession session, string id, string? marker)
    {
        using (var consent = await SendAsync(web, session, HttpMethod.Post, $"/api/proxy/v1/interviews/{id}/reply", new { text = Consent }))
        {
            await consent.EnsureStatusAsync(HttpStatusCode.OK, "consent to the interview");
        }
        for (var i = 0; ; i++)
        {
            if (i >= 25) throw new InvalidOperationException("The interview did not close within 25 answers.");
            var text = Answers[i % Answers.Length];
            if (marker is not null && i == 1) text += " " + marker;
            using var reply = await SendAsync(web, session, HttpMethod.Post, $"/api/proxy/v1/interviews/{id}/reply", new { text });
            await reply.EnsureStatusAsync(HttpStatusCode.OK, $"answer {i + 1}");
            var body = await ReadJsonAsync(reply);
            if (body.TryGetProperty("ending", out var ending) && ending.ValueKind == JsonValueKind.Object) return;
            // The closing turn is an interviewer turn of kind "close": the session ends after it, and a reply after it answers 409
            // (interview_ended). The ending comes with the next event, so the closing turn is the signal (run 38142954680).
            if (body.TryGetProperty("turn", out var turn) && turn.ValueKind == JsonValueKind.Object
                && turn.TryGetProperty("kind", out var kind) && kind.GetString() == "close") return;
        }
    }

    /// <summary>The record is written after the closing turn; the result answers 409 not_completed until then.</summary>
    private static async Task<HttpResponseMessage> PollResultAsync(HttpClient web, BffSession session, string id)
    {
        for (var i = 0; i < 60; i++)
        {
            var response = await SendAsync(web, session, HttpMethod.Get, $"/api/proxy/v1/interviews/{id}/result");
            if (response.StatusCode == HttpStatusCode.OK) return response;
            if (response.StatusCode != HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException($"result: expected 200 or 409 while the record is written, got {(int)response.StatusCode}. {await response.BodyAsync()}");
            }
            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw new InvalidOperationException("The record was not ready within 60 seconds.");
    }

    /// <summary>A request as the browser would send it: the cookies the session holds and the same-origin Origin header (ADR-0047).</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient web, BffSession session, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", web.BaseAddress!.GetLeftPart(UriPartial.Authority));
        if (session.CookieHeader.Length > 0) request.Headers.Add("Cookie", session.CookieHeader);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        var response = await web.SendAsync(request);
        session.Absorb(response);
        return response;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task WaitForMigrationsAsync(NpgsqlConnection db)
    {
        // Migrations run in a hosted service after the server is up (ADR-0006), so the tables may follow the health check by seconds.
        for (var i = 0; i < 90; i++)
        {
            if (await ScalarAsync(db, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'") == 1
                && await ScalarAsync(db, "SELECT count(*) FROM \"__EFMigrationsHistory\"") > 0)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw new InvalidOperationException("The interviewdb migrations were not recorded within 90 seconds.");
    }

    private static async Task<List<string>> BaseTablesAsync(NpgsqlConnection db)
    {
        await using var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name", db);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection db, string sql, params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
