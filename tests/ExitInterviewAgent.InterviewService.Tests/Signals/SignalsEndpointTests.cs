using System.Net;
using System.Text.Json;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ExitInterviewAgent.InterviewService.Tests.Signals.SignalsHarness;

namespace ExitInterviewAgent.InterviewService.Tests.Signals;

public sealed class SignalsEndpointTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;

    // ---- access -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_signals_endpoints_need_an_account_token()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();

        foreach (var path in new[] { "/api/v1/signals/employers", "/api/v1/signals/employers/acme" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client(host.McpToken("mcp-account")).GetAsync(path)).StatusCode); // the MCP audience is not this API's
        }
    }

    [Fact]
    public async Task Only_two_signals_routes_exist_both_reads_and_neither_takes_a_sort_or_filter()
    {
        using var host = new TestHost(ManualPublishing());
        var routes = host.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>().SelectMany(s => s.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Where(e => e.RoutePattern.RawText!.Contains("signals", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Equal(["/api/v1/signals/employers", "/api/v1/signals/employers/{employerRef}"], routes.Select(r => r.RoutePattern.RawText!).Order());
        Assert.All(routes, r => Assert.Equal(["GET"], r.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods));
        var parameters = routes.SelectMany(r => r.RoutePattern.Parameters.Select(p => p.Name)).Distinct().ToArray();
        Assert.Equal(["employerRef"], parameters); // the query parameters (page, limit) are bound from the handler, below
        var handlerArguments = routes.SelectMany(r => r.Metadata.OfType<System.Reflection.MethodInfo>()).SelectMany(m => m.GetParameters().Select(p => p.Name!)).Distinct().Order().ToArray();
        Assert.Contains("page", handlerArguments);   // the reflection really sees the handlers: the next line cannot pass vacuously
        Assert.Contains("limit", handlerArguments);
        Assert.DoesNotContain(handlerArguments, n => new[] { "sort", "order", "orderBy", "rank", "min", "max", "score", "filter", "q", "search" }.Contains(n, StringComparer.OrdinalIgnoreCase));
    }

    // ---- shape, snapshot, caching -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_employer_with_enough_records_is_served_with_n_interval_reliability_coverage_and_the_snapshot_header()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "acme", 6, i => Record("acme", rating: new[] { 4, 5, 3, 4, 5, 4 }[i], tenure: i < 3 ? "1y_3y" : "3y_5y"));
        Assert.Equal(PublishOutcomeOf.Published, await PublishNextBatchAsync(host));

        var (response, body) = await GetAsync(host, "/api/v1/signals/employers/acme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employer = JsonSerializer.Deserialize<SignalsEmployer>(body, Web)!;
        Assert.Equal("acme", employer.EmployerRef);
        Assert.Equal("5-9", employer.RespondentsBand);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), employer.Snapshot.GeneratedAt); // the start of the batch, not 12:00
        Assert.Equal(24, employer.Snapshot.PublicationIntervalHours);
        Assert.Equal(5, employer.Snapshot.MinimumGroupSize);
        Assert.Equal("1", employer.Snapshot.RulesVersion);
        Assert.True(employer.Snapshot.DeletionsAppearAtNextPublication);
        Assert.Equal(Topics, employer.Topics.Select(t => t.Topic));
        var culture = employer.Topics.Single(t => t.Topic == "culture");
        Assert.Equal(SignalsVocabulary.TopicOk, culture.Status);
        Assert.Equal(6, culture.Overall!.N);
        Assert.Equal(4.17, culture.Overall.Mean);
        Assert.True(culture.Overall.Interval.Lower < culture.Overall.Mean && culture.Overall.Mean < culture.Overall.Interval.Upper);
        Assert.Equal(0.95, culture.Overall.Interval.Level);
        Assert.Equal(SignalsVocabulary.ReliabilityLow, culture.Overall.Reliability);
        Assert.Equal(SignalsVocabulary.CoverageHigh, culture.Overall.Coverage);
        Assert.Equal(["tenure", "seniority", "function"], culture.Cuts.Select(c => c.Dimension));
        Assert.Equal(SignalsVocabulary.CutSuppressed, culture.Cuts[0].Status);  // tenure 3 + 3: both bands are below k = 5, so the cut is withheld
        Assert.Equal(SignalsVocabulary.CutPublished, culture.Cuts[1].Status);   // seniority: all six say "mid", a clean partition
        Assert.Equal(6, culture.Cuts[1].Cells.Single(c => c.Band == "mid").Stats!.N);
    }

    [Fact]
    public async Task A_cut_with_bands_below_k_is_reported_as_suppressed_with_no_cells()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "acme", 6, i => Record("acme", tenure: i < 3 ? "1y_3y" : "3y_5y"));
        await PublishNextBatchAsync(host);

        var employer = JsonSerializer.Deserialize<SignalsEmployer>((await GetAsync(host, "/api/v1/signals/employers/acme")).Body, Web)!;
        var tenure = employer.Topics[0].Cuts.Single(c => c.Dimension == "tenure");

        Assert.Equal(SignalsVocabulary.CutSuppressed, tenure.Status);
        Assert.Empty(tenure.Cells);
    }

    [Fact]
    public async Task Caching_headers_follow_the_batch_and_a_conditional_request_gets_304()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "acme", 5);
        await PublishNextBatchAsync(host);
        host.Clock.Advance(TimeSpan.FromHours(2)); // 14:00 on the 6th; the next batch starts at midnight

        var (first, _) = await GetAsync(host, "/api/v1/signals/employers/acme");

        Assert.True(first.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromHours(10), first.Headers.CacheControl.MaxAge); // 10 hours to the next batch
        Assert.Contains("Authorization", first.Headers.Vary);
        Assert.NotNull(first.Headers.ETag);
        Assert.Equal("Tue, 06 Oct 2026 00:00:00 GMT", first.Content.Headers.LastModified!.Value.UtcDateTime.ToString("R"));

        var (second, secondBody) = await GetAsync(host, "/api/v1/signals/employers/acme", configure: r => r.Headers.IfNoneMatch.Add(first.Headers.ETag!));
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(secondBody);

        await PublishNextBatchAsync(host);
        var (third, _) = await GetAsync(host, "/api/v1/signals/employers/acme", configure: r => r.Headers.IfNoneMatch.Add(first.Headers.ETag!));
        Assert.Equal(HttpStatusCode.OK, third.StatusCode); // a new snapshot has a new tag
    }

    // ---- k, and the answer that must not say why --------------------------------------------------------------------------

    [Fact]
    public async Task An_employer_below_k_and_an_unknown_employer_get_byte_identical_answers()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "few-records", 4);
        await SubmitManyAsync(host, "enough", 5);
        await PublishNextBatchAsync(host);

        var (below, belowBody) = await GetAsync(host, "/api/v1/signals/employers/few-records");
        var (unknown, unknownBody) = await GetAsync(host, "/api/v1/signals/employers/never-heard-of");

        Assert.Equal(HttpStatusCode.NotFound, below.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(unknownBody, belowBody);
        Assert.Equal(SignalsCodes.EmployerNotFound, TestRecords.Code(belowBody));
        Assert.Equal(unknown.Headers.CacheControl, below.Headers.CacheControl);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/api/v1/signals/employers/enough")).Response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_employer_reference_is_a_400_with_a_stable_code()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();

        var (response, body) = await GetAsync(host, "/api/v1/signals/employers/Not%20Valid!");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SignalsCodes.InvalidEmployerRef, TestRecords.Code(body));
    }

    [Fact]
    public async Task Before_the_first_publication_the_list_is_empty_and_says_there_is_no_snapshot()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();

        var (response, body) = await GetAsync(host, "/api/v1/signals/employers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = JsonSerializer.Deserialize<SignalsEmployerList>(body, Web)!;
        Assert.Null(list.Snapshot);
        Assert.Empty(list.Employers);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(host, "/api/v1/signals/employers/acme")).Response.StatusCode);
    }

    // ---- the list ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_holds_employers_with_a_displayable_cell_alphabetically_whatever_their_ratings()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "zulu-best", 5, _ => Record("zulu-best", rating: 5));
        await SubmitManyAsync(host, "alpha-worst", 5, _ => Record("alpha-worst", rating: 1));
        await SubmitManyAsync(host, "mid-employer", 5, _ => Record("mid-employer", rating: 3));
        await SubmitManyAsync(host, "tiny", 3);
        await PublishNextBatchAsync(host);

        var list = JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers")).Body, Web)!;

        Assert.Equal(["alpha-worst", "mid-employer", "zulu-best"], list.Employers);
        Assert.Equal(3, list.Total); // "tiny" is not counted, not hinted at
    }

    [Fact]
    public async Task List_inputs_are_clamped_and_paging_is_stable()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        foreach (var name in new[] { "emp-a", "emp-b", "emp-c" })
        {
            await SubmitManyAsync(host, name, 5, _ => Record(name));
        }
        await PublishNextBatchAsync(host);

        var huge = JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers?limit=2000000&page=-4")).Body, Web)!;
        var second = JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers?limit=2&page=2")).Body, Web)!;

        Assert.Equal((1, 100), (huge.Page, huge.Limit));
        Assert.Equal(["emp-c"], second.Employers);
        Assert.Equal(3, second.Total);
    }

    // ---- never a record, never a quote -------------------------------------------------------------------------------------

    [Fact]
    public async Task No_signals_response_contains_any_field_or_value_of_a_record()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        const string Canary = "Zebrafish canary sentence about whiteboard markers";
        var records = new List<System.Text.Json.Nodes.JsonObject>();
        var receipts = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var record = Record("acme", rating: 2 + i % 3, tenure: i % 2 == 0 ? "1y_3y" : "3y_5y", quote: Canary);
            records.Add(record);
            receipts.Add(await SubmitAsync(host, record));
        }
        await PublishNextBatchAsync(host);

        var bodies = new[] { (await GetAsync(host, "/api/v1/signals/employers")).Body, (await GetAsync(host, "/api/v1/signals/employers/acme")).Body, (await GetAsync(host, "/api/v1/signals/employers/zzz-none")).Body };

        foreach (var body in bodies)
        {
            Assert.DoesNotContain("Zebrafish", body);
            Assert.DoesNotContain("canary", body, StringComparison.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                Assert.DoesNotContain(record["interviewId"]!.GetValue<string>(), body);
            }
            foreach (var receipt in receipts)
            {
                Assert.DoesNotContain(receipt, body);
            }
            var names = PropertyNames(JsonDocument.Parse(body).RootElement).Select(n => n.ToLowerInvariant()).ToHashSet();
            Assert.False(names.Overlaps(["quotes", "quote", "interviewid", "receiptcode", "json", "sub", "email", "confidence", "piimasked", "protocolversion", "aidisclosed", "durationband", "turnband", "createdweek", "rating"]),
                $"a record field leaked: {string.Join(',', names)}");
        }
    }

    // ---- batching and deletion, honestly -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_submission_changes_nothing_until_the_next_batch_and_a_deletion_the_same()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        var receipts = await SubmitManyAsync(host, "acme", 5);
        await PublishNextBatchAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/api/v1/signals/employers/acme")).Response.StatusCode);

        // The sixth record arrives and the first is deleted by its receipt code, inside the same batch.
        await SubmitManyAsync(host, "acme", 1);
        var deleted = await host.Client().SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/receipts") { Headers = { { SubmissionHeaders.ReceiptCode, receipts[0] } } });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        host.Clock.Advance(TimeSpan.FromHours(3));
        await host.InScopeAsync(sp => sp.GetRequiredService<ExitInterviewAgent.Signals.SnapshotPublisher>().RunDueAsync(default));

        var during = JsonSerializer.Deserialize<SignalsEmployer>((await GetAsync(host, "/api/v1/signals/employers/acme")).Body, Web)!;
        Assert.Equal(5, during.Topics[0].Overall!.N); // honest: the published batch still says five, deletion and arrival alike

        await PublishNextBatchAsync(host);
        var after = JsonSerializer.Deserialize<SignalsEmployer>((await GetAsync(host, "/api/v1/signals/employers/acme")).Body, Web)!;
        Assert.Equal(5, after.Topics[0].Overall!.N); // 5 - 1 + 1: the next batch is rebuilt from what is stored
    }

    [Fact]
    public async Task A_deletion_that_takes_an_employer_below_k_removes_it_at_the_next_batch()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();
        var receipts = await SubmitManyAsync(host, "acme", 5);
        await PublishNextBatchAsync(host);

        await host.Client().SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/receipts") { Headers = { { SubmissionHeaders.ReceiptCode, receipts[0] } } });
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/api/v1/signals/employers/acme")).Response.StatusCode);

        await PublishNextBatchAsync(host);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(host, "/api/v1/signals/employers/acme")).Response.StatusCode);
        Assert.Empty(JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers")).Body, Web)!.Employers);
    }

    // ---- scraping ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Repeated_queries_are_rate_limited_per_account_with_the_uniform_429_body()
    {
        using var host = new TestHost(ManualPublishing(new() { ["Signals:RequestsPerMinute"] = "3" }));
        await host.WaitReadyAsync();
        var client = host.Client(host.WebToken("scraper"));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.GetAsync("/api/v1/signals/employers")).StatusCode);
        }
        var limited = await client.GetAsync("/api/v1/signals/employers/acme");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); // one budget for both endpoints
        Assert.Contains("rate_limited", await limited.Content.ReadAsStringAsync());
        Assert.NotNull(limited.Headers.RetryAfter);
        // Another account has its own budget.
        Assert.Equal(HttpStatusCode.OK, (await host.Client(host.WebToken("someone-else")).GetAsync("/api/v1/signals/employers")).StatusCode);
    }

    // ---- documentation ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_openapi_document_describes_both_endpoints_with_problem_responses_and_the_deletion_note()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();

        var document = JsonDocument.Parse(await host.Client().GetStringAsync("/openapi/v1.json")).RootElement;
        var paths = document.GetProperty("paths");

        var signals = paths.EnumerateObject().Where(p => p.Name.Contains("/signals/")).ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(["/api/v1/signals/employers", "/api/v1/signals/employers/{employerRef}"], signals.Keys.Order());
        var detail = signals["/api/v1/signals/employers/{employerRef}"].GetProperty("get");
        Assert.Contains("next snapshot", detail.GetProperty("description").GetString());
        var responses = detail.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray();
        Assert.Equal(["200", "304", "400", "404", "429"], responses);
        Assert.DoesNotContain(paths.EnumerateObject().Select(p => p.Name), n => new[] { "rank", "best", "worst", "top", "percentile", "compare", "score" }.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    // ---- operations -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Health_lists_the_signals_integration_and_the_module_check()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();

        var health = JsonDocument.Parse(await host.Client().GetStringAsync("/health")).RootElement;

        var integration = health.GetProperty("integrations").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "signals");
        Assert.False(integration.GetProperty("configured").GetBoolean()); // disabled in this host: degraded, and visible
        Assert.Contains("disabled", integration.GetProperty("detail").GetString());
        Assert.Equal("Healthy", health.GetProperty("checks").GetProperty("signals").GetString());
        Assert.Equal(1, health.GetProperty("integrations").EnumerateArray().Count(i => i.GetProperty("name").GetString() == "database")); // the second context did not add a second entry
    }

    [Fact]
    public async Task The_hosted_publisher_publishes_on_its_own_when_enabled()
    {
        using var host = new TestHost(new() { ["Signals:Enabled"] = "true", ["Signals:CheckInterval"] = "00:00:01" });
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "acme", 5);

        host.Clock.Advance(TimeSpan.FromDays(1));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        HttpStatusCode status;
        do
        {
            await Task.Delay(250);
            status = (await GetAsync(host, "/api/v1/signals/employers/acme")).Response.StatusCode;
        } while (status != HttpStatusCode.OK && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Publishing_and_reading_log_no_quote_no_record_id_and_no_employer_from_the_publisher()
    {
        var logs = new CaptureLoggerProvider();
        using var host = new TestHost(ManualPublishing(), logs: logs);
        await host.WaitReadyAsync();
        const string Quote = "Zebrafish canary sentence about whiteboard markers";
        var records = new List<System.Text.Json.Nodes.JsonObject>();
        for (var i = 0; i < 6; i++)
        {
            var record = Record("canary-employer-zzzq", quote: Quote);
            records.Add(record);
            await SubmitAsync(host, record);
        }
        logs.Clear();

        await PublishNextBatchAsync(host);
        await GetAsync(host, "/api/v1/signals/employers");

        Assert.NotEmpty(logs.Lines); // the capture sees the publication, so an empty result below would mean something
        Assert.DoesNotContain(logs.Lines, l => l.Contains("Zebrafish", StringComparison.OrdinalIgnoreCase));
        foreach (var record in records)
        {
            Assert.DoesNotContain(logs.Lines, l => l.Contains(record["interviewId"]!.GetValue<string>()));
        }
        Assert.DoesNotContain(logs.Lines.Where(l => l.Contains("signals", StringComparison.OrdinalIgnoreCase)), l => l.Contains("canary-employer"));
    }
}

/// <summary>Alias so the endpoint tests read as prose.</summary>
internal static class PublishOutcomeOf
{
    public const ExitInterviewAgent.Signals.PublishOutcome Published = ExitInterviewAgent.Signals.PublishOutcome.Published;
}
