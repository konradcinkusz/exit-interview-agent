using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Tests.Support;

namespace ExitInterviewAgent.InterviewService.Tests.Interviews;

/// <summary>
/// W2 (web-app-plan §10): the interview session endpoints around the agent, on the InMemory store and the scripted mock.
/// Each test uses its own account, so one test's open session cannot collide with another's.
/// </summary>
public sealed class InterviewEndpointTests : IDisposable
{
    /// <summary>Consent, then sixteen Polish answers: the same script the CLI's Polish mock interview uses.</summary>
    private static readonly string[] PolishLines =
        ["Yes, I consent.", .. Enumerable.Range(1, 16).Select(i => $"Na temat {i} proces trwał 3 tygodnie i nikt nie wyjaśnił dlaczego, bo nie było rozmowy.")];

    private const string Canary = "kanarek-w2-zostawił-dokumenty-na-biurku";

    private readonly InterviewHost _host = new();

    public void Dispose() => _host.Dispose();

    private static object Start(string language = "pl", string tenure = "1y_3y") => new { language, tenure };

    private static async Task<HttpResponseMessage> PostStart(HttpClient client) =>
        await client.PostAsJsonAsync("/api/v1/interviews", Start());

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string CodeOf(JsonElement problem) => problem.GetProperty("code").GetString()!;

    /// <summary>Starts an interview and answers until the interview ends. Returns the session id and the last reply body.</summary>
    private async Task<(string Id, JsonElement LastReply)> RunToEnd(HttpClient client, string[] lines)
    {
        var started = await PostStart(client);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var body = await Json(started);
        var id = body.GetProperty("id").GetString()!;
        foreach (var line in lines)
        {
            var reply = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = line });
            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            var json = await Json(reply);
            if (json.GetProperty("turn").ValueKind == JsonValueKind.Object && json.GetProperty("turn").GetProperty("kind").GetString() is "close" or "stop")
            {
                await WaitForStatus(client, id, "completed", "stopped");
                return (id, json);
            }
        }
        Assert.Fail("The interview did not end within the scripted answers.");
        return default;
    }

    /// <summary>The closing turn is shown before the tiles are made; the status turns terminal when they are ready.</summary>
    private static async Task<string> WaitForStatus(HttpClient client, string id, params string[] wanted)
    {
        var status = "";
        for (var attempt = 0; attempt < 500; attempt++)
        {
            status = (await Json(await client.GetAsync($"/api/v1/interviews/{id}"))).GetProperty("status").GetString()!;
            if (wanted.Contains(status)) return status;
            await Task.Delay(10);
        }
        Assert.Fail($"The status did not reach {string.Join(" or ", wanted)} (last: {status}).");
        return status;
    }

    // ---- the full path ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_polish_interview_runs_from_consent_to_a_result_with_tiles()
    {
        var client = _host.As("account-full-path");

        var (id, last) = await RunToEnd(client, PolishLines);

        Assert.Equal("close", last.GetProperty("turn").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("ending").ValueKind);
        var state = await Json(await client.GetAsync($"/api/v1/interviews/{id}"));
        Assert.Equal("completed", state.GetProperty("status").GetString());

        var result = await Json(await client.GetAsync($"/api/v1/interviews/{id}/result"));
        Assert.Equal(JsonValueKind.Object, result.GetProperty("record").ValueKind);
        var tiles = result.GetProperty("tiles");
        Assert.Contains(tiles.GetProperty("items").EnumerateArray(), t => t.GetProperty("kind").GetString() == "facts");
        Assert.True(tiles.GetProperty("items").GetArrayLength() > 1);
        Assert.Equal(InterviewNoticeText("pl"), tiles.GetProperty("notice").GetString());
        var usage = result.GetProperty("usage");
        Assert.True(usage.GetProperty("modelCalls").GetInt32() > 0);
        Assert.True(usage.GetProperty("tokensEstimated").GetInt64() > 0);
        Assert.Empty(_host.Settlements.Settled.Where(s => s.Outcome is SessionOutcome.Failed or SessionOutcome.Lost).Select(s => s.Session).ToArray());
    }

    [Fact]
    public async Task The_opening_turn_is_the_disclosure_and_the_status_is_awaiting_consent()
    {
        var client = _host.As("account-opening");

        var started = await Json(await PostStart(client));

        Assert.Equal("awaiting_consent", started.GetProperty("status").GetString());
        Assert.Equal("pl", started.GetProperty("language").GetString());
        Assert.Equal("opening", started.GetProperty("turn").GetProperty("kind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(started.GetProperty("turn").GetProperty("text").GetString()));
        Assert.True(started.GetProperty("expiresAt").GetDateTimeOffset() > _host.Clock.GetUtcNow());
    }

    [Fact]
    public async Task The_result_is_not_available_before_the_interview_completes()
    {
        var client = _host.As("account-not-completed");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        var result = await client.GetAsync($"/api/v1/interviews/{id}/result");

        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("not_completed", CodeOf(await Json(result)));
    }

    // ---- ids and ownership -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Another_account_gets_404_for_every_route_of_a_session_it_does_not_own()
    {
        var owner = _host.As("account-owner");
        var stranger = _host.As("account-stranger");
        var id = (await Json(await PostStart(owner))).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/interviews/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/interviews/{id}/result")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Tak." })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/interviews/{id}")).StatusCode);
        // The owner's session is untouched by the stranger's attempts.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/interviews/{id}")).StatusCode);
    }

    [Fact]
    public async Task Session_ids_are_random_and_long_enough_to_be_unguessable()
    {
        var client = _host.As("account-ids");
        var first = (await Json(await PostStart(client))).GetProperty("id").GetString()!;
        await client.DeleteAsync($"/api/v1/interviews/{first}");
        var second = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 43, "an id needs at least 256 bits of entropy (43 base64url characters)");
    }

    [Fact]
    public async Task An_unknown_id_is_404()
    {
        var client = _host.As("account-unknown");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/interviews/does-not-exist")).StatusCode);
    }

    // ---- one open session per account ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_start_while_a_session_is_open_is_409()
    {
        var client = _host.As("account-second-start");
        await PostStart(client);

        var second = await PostStart(client);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("interview_in_progress", CodeOf(await Json(second)));
    }

    [Fact]
    public async Task Deleting_the_open_session_allows_a_new_start()
    {
        var client = _host.As("account-after-delete");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/interviews/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await PostStart(client)).StatusCode);
    }

    // ---- delete and wipe -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_wipes_the_session_so_that_later_reads_are_404()
    {
        var client = _host.As("account-delete");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        var deleted = await client.DeleteAsync($"/api/v1/interviews/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/interviews/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Tak." })).StatusCode);
    }

    [Fact]
    public async Task Delete_after_completion_wipes_the_result()
    {
        var client = _host.As("account-delete-result");
        var (id, _) = await RunToEnd(client, PolishLines);

        await client.DeleteAsync($"/api/v1/interviews/{id}");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/interviews/{id}/result")).StatusCode);
    }

    // ---- idle timeout and result lifetime (fake clock) -------------------------------------------------------------------

    [Fact]
    public async Task An_idle_session_expires_30_minutes_after_its_last_request_and_is_410()
    {
        var client = _host.As("account-idle");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        _host.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/interviews/{id}")).StatusCode);

        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        var expired = await client.GetAsync($"/api/v1/interviews/{id}");

        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("gone", CodeOf(await Json(expired)));
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Tak." })).StatusCode);
    }

    [Fact]
    public async Task A_reply_refreshes_the_idle_timer()
    {
        var client = _host.As("account-refresh");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        _host.Clock.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = PolishLines[0] })).StatusCode);
        // 25 minutes after the reply is inside the 30-minute idle window measured from the reply, not from the start.
        _host.Clock.Advance(TimeSpan.FromMinutes(25));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/interviews/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_completed_result_stays_readable_for_30_minutes_and_is_then_gone()
    {
        var client = _host.As("account-result-ttl");
        var (id, _) = await RunToEnd(client, PolishLines);

        _host.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/interviews/{id}/result")).StatusCode);

        _host.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Gone, (await client.GetAsync($"/api/v1/interviews/{id}/result")).StatusCode);
    }

    // ---- kill switch, provider and credit gate --------------------------------------------------------------------------

    [Fact]
    public async Task The_kill_switch_refuses_new_sessions_with_503()
    {
        _host.Settings["Interviews:Enabled"] = "false";
        var client = _host.As("account-kill-switch");

        var response = await PostStart(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("interviews_disabled", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task Without_a_configured_provider_the_service_answers_503_and_consumes_nothing()
    {
        _host.Settings.Remove("Interviews:Provider");
        var client = _host.As("account-no-provider");

        var response = await PostStart(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("interviews_disabled", CodeOf(await Json(response)));
        Assert.Empty(_host.Settlements.Settled.Where(s => s.Outcome is SessionOutcome.Failed or SessionOutcome.Lost).Select(s => s.Session).ToArray());
    }

    [Fact]
    public async Task With_credits_required_and_no_credit_the_start_is_402()
    {
        _host.Settings["Interviews:RequireCredit"] = "true";
        var client = _host.As("account-no-credit");

        var response = await PostStart(client);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal("payment_required", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task An_invalid_language_or_tenure_is_400_and_starts_nothing()
    {
        var client = _host.As("account-invalid-start");

        var badLanguage = await client.PostAsJsonAsync("/api/v1/interviews", Start(language: "de"));
        var badTenure = await client.PostAsJsonAsync("/api/v1/interviews", Start(tenure: "forever"));

        Assert.Equal(HttpStatusCode.BadRequest, badLanguage.StatusCode);
        Assert.Equal("invalid_request", CodeOf(await Json(badLanguage)));
        Assert.Equal(HttpStatusCode.BadRequest, badTenure.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostStart(client)).StatusCode);
    }

    // ---- reply limits ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_reply_is_422(string text)
    {
        var client = _host.As($"account-empty-{text.Length}");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("reply_invalid", CodeOf(await Json(response)));
    }

    [Fact]
    public async Task A_reply_over_2000_characters_is_422_and_2000_is_accepted()
    {
        var client = _host.As("account-length");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        var tooLong = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = new string('a', 2001) });
        var atLimit = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Tak, zgadzam się. " + new string('b', 1982) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);
        Assert.Equal("reply_invalid", CodeOf(await Json(tooLong)));
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
    }

    // ---- nothing the person says reaches logs, spans or metrics -----------------------------------------------------------

    [Fact]
    public async Task No_interview_text_reaches_the_logs_or_the_spans()
    {
        var spans = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("ExitInterviewAgent", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add(string.Join(';', activity.Tags.Select(t => $"{t.Key}={t.Value}")));
                    spans.AddRange(activity.Events.Select(e => string.Join(';', e.Tags.Select(t => $"{t.Key}={t.Value}"))));
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var client = _host.As("account-no-text");
        var lines = PolishLines.ToArray();
        lines[3] = $"Tu jest {Canary} i to jest prawdziwa odpowiedź.";

        await RunToEnd(client, lines);

        Assert.DoesNotContain(_host.Logs.Lines, line => line.Contains(Canary, StringComparison.Ordinal));
        lock (spans) Assert.DoesNotContain(spans, s => s.Contains(Canary, StringComparison.Ordinal));
    }

    // ---- the failure path -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_provider_failure_ends_the_session_as_failed_with_503_and_refunds_the_credit()
    {
        _host.ModelOverride = new FailingChatClient();
        var client = _host.As("account-provider-down");
        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;

        var reply = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Yes, I consent." });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.StatusCode);
        Assert.Equal("provider_unavailable", CodeOf(await Json(reply)));
        var state = await Json(await client.GetAsync($"/api/v1/interviews/{id}"));
        Assert.Equal("failed", state.GetProperty("status").GetString());
        Assert.Equal(new[] { id }, _host.Settlements.Settled.Where(s => s.Outcome is SessionOutcome.Failed or SessionOutcome.Lost).Select(s => s.Session).ToArray());
    }

    [Fact]
    public async Task A_withdrawn_consent_ends_as_stopped_and_is_not_refunded()
    {
        var client = _host.As("account-withdraw");

        var id = (await Json(await PostStart(client))).GetProperty("id").GetString()!;
        var reply = await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Nie, nie zgadzam się." });

        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Equal("stop", (await Json(reply)).GetProperty("turn").GetProperty("kind").GetString());
        await WaitForStatus(client, id, "stopped");
        Assert.Empty(_host.Settlements.Settled.Where(s => s.Outcome is SessionOutcome.Failed or SessionOutcome.Lost).Select(s => s.Session).ToArray());
    }

    [Fact]
    public void The_notice_is_the_same_text_the_cli_prints()
    {
        // The CLI owns the notice (TileRenderer, internal). The service repeats it, and this test keeps the two equal.
        Assert.Equal(TileNoticeFromCli("NoticePl"), InterviewNoticeText("pl"));
        Assert.Equal(TileNoticeFromCli("NoticeEn"), InterviewNoticeText("en"));
    }

    /// <summary>The CLI's renderer is internal to its assembly: reached by name, read only, never called.</summary>
    private static Type CliTileRenderer() =>
        typeof(ExitInterviewAgent.Cli.CliApp).Assembly.GetType("ExitInterviewAgent.Cli.Tiles.TileRenderer")!;

    private static string TileNoticeFromCli(string field) =>
        (string)CliTileRenderer().GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static string InterviewNoticeText(string language) => ExitInterviewAgent.InterviewService.Interviews.InterviewNotice.For(language);
}
