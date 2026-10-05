using System.Net;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExitInterviewAgent.InterviewService.Tests.Persistence;

/// <summary>
/// Against a real PostgreSQL server (TEST_POSTGRES_CONNECTION): the migrations, the unique indexes and the atomic delete,
/// which the InMemory provider cannot exercise. Without the variable these are SKIPPED and reported as not run.
/// </summary>
public sealed class PostgresSubmissionTests
{
    private const int Parallel = 24;

    private static async Task<T[]> RaceAsync<T>(int count, Func<int, Task<T>> work)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () => { await start.Task; return await work(i); })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static async Task<List<string?[]>> QueryAsync(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string?[]>();
        while (await reader.ReadAsync())
        {
            rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i).ToString()).ToArray());
        }
        return rows;
    }

    [PostgresFact]
    public async Task The_migrations_create_exactly_the_documented_columns_and_the_model_has_no_pending_changes()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();

        var columns = (await QueryAsync(pg.ConnectionString,
            "select table_name, column_name from information_schema.columns where table_schema='public' and table_name <> '__EFMigrationsHistory' order by 1,2"))
            .GroupBy(r => r[0]!).ToDictionary(g => g.Key, g => g.Select(r => r[1]!).Order().ToArray());

        Assert.Equal(SchemaGolden.Columns.Keys.Order(), columns.Keys.Order());
        foreach (var (table, expected) in SchemaGolden.Columns)
        {
            Assert.Equal(expected.Order(), columns[table]);
        }
        var uniqueIndexes = await QueryAsync(pg.ConnectionString, "select indexname from pg_indexes where schemaname='public' and indexdef like 'CREATE UNIQUE%'");
        Assert.Contains(uniqueIndexes, r => r[0] == InterviewDbContext.LedgerTagIndex);
        Assert.Contains(uniqueIndexes, r => r[0] == "IX_Receipts_CodeHash");
        Assert.Contains(uniqueIndexes, r => r[0] == "IX_SubmissionTickets_TokenHash");
        Assert.False(await host.InScopeAsync(sp => Task.FromResult(sp.GetRequiredService<InterviewDbContext>().Database.HasPendingModelChanges())));
    }

    [PostgresFact]
    public async Task The_ledger_has_no_foreign_key_and_only_the_receipts_reference_the_records()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();

        var fks = await QueryAsync(pg.ConnectionString,
            "select conrelid::regclass::text, confrelid::regclass::text from pg_constraint where contype='f' and connamespace='public'::regnamespace");

        Assert.Equal([["\"Receipts\"", "\"Records\""]], fks.Select(r => r.Select(c => c!.Replace("public.", "")).ToArray()).ToArray());
    }

    [PostgresFact]
    public async Task Concurrent_duplicate_submissions_yield_exactly_one_success()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();
        var token = host.WebToken(sub);

        var results = await RaceAsync(Parallel, async _ =>
        {
            var (response, body) = await host.Client(token).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(employer))).ReadAsync();
            return (response.StatusCode, Code: response.IsSuccessStatusCode ? null : TestRecords.Code(body));
        });

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(Parallel - 1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict && r.Code == SubmissionCodes.AlreadySubmitted));
        var counts = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            return (await db.SubmissionLedger.CountAsync(), await db.Records.CountAsync(), await db.Receipts.CountAsync());
        });
        Assert.Equal((1, 1, 1), counts); // the losers left nothing behind: one transaction
    }

    [PostgresFact]
    public async Task Concurrent_submissions_by_the_web_and_mcp_paths_share_the_one_ledger()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();

        var results = await RaceAsync(Parallel, async i =>
        {
            var web = i % 2 == 0;
            if (!web)
            {
                var viaMcp = await McpWire.CallToolAsync(host.Client(host.McpToken(sub)), "submit_interview_record", TestRecords.Valid(employer));
                return viaMcp.Accepted ? HttpStatusCode.Created : viaMcp.Code == "ALREADY_SUBMITTED" ? HttpStatusCode.Conflict : viaMcp.Http;
            }
            var response = await host.Client(host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(employer)));
            return response.StatusCode;
        });

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(Parallel - 1, results.Count(s => s == HttpStatusCode.Conflict));
    }

    [PostgresFact]
    public async Task Concurrent_submissions_for_different_employers_all_succeed()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var token = host.WebToken(TestRecords.NewSub());

        var results = await RaceAsync(Parallel, async _ =>
            (await host.Client(token).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid()))).StatusCode);

        Assert.All(results, s => Assert.Equal(HttpStatusCode.Created, s));
    }

    [PostgresFact]
    public async Task A_clashing_interview_id_has_exactly_one_winner_and_the_losers_leave_no_ledger_entry()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var id = TestRecords.NewInterviewId();

        var results = await RaceAsync(Parallel, async _ =>
        {
            var record = TestRecords.Valid(change: r => r["interviewId"] = id);
            var (response, body) = await host.Client(host.WebToken(TestRecords.NewSub())).PostAsync("/api/v1/submissions", TestRecords.Json(record)).ReadAsync();
            return (response.StatusCode, Code: response.IsSuccessStatusCode ? null : TestRecords.Code(body));
        });

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(Parallel - 1, results.Count(r => r.Code == SubmissionCodes.InterviewIdTaken));
        Assert.Equal(1, await host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionLedger.CountAsync()));
    }

    [PostgresFact]
    public async Task A_ticket_redeemed_in_parallel_succeeds_exactly_once_and_its_row_is_deleted()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(new() { ["Submission:Limits:TicketedSubmitPerIpPerMinute"] = "1000", ["Submission:Limits:TicketedSubmitGlobalPerMinute"] = "1000" }, postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var (_, mintBody) = await host.Client(host.WebToken(TestRecords.NewSub())).PostAsync("/api/v1/tickets", null).ReadAsync();
        var ticket = TestRecords.TicketOf(mintBody).Ticket;

        var results = await RaceAsync(Parallel, async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions/ticketed") { Content = TestRecords.Json(TestRecords.Valid()) }; // a different employer each time
            request.Headers.Add(SubmissionHeaders.Ticket, ticket);
            var (response, body) = await host.Client().SendAsync(request).ReadAsync();
            return (response.StatusCode, Code: response.IsSuccessStatusCode ? null : TestRecords.Code(body));
        });

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(Parallel - 1, results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized && r.Code == SubmissionCodes.TicketInvalid));
        var counts = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            return (await db.SubmissionTickets.CountAsync(), await db.Records.CountAsync(), await db.SubmissionLedger.CountAsync());
        });
        Assert.Equal((0, 1, 1), counts);
    }

    [PostgresFact]
    public async Task The_atomic_delete_alone_lets_exactly_one_of_many_simultaneous_redeemers_win()
    {
        // Bypasses HTTP so every caller reaches the delete at once: the affected-row count, not request timing, decides.
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var sub = TestRecords.NewSub();
        var (_, mintBody) = await host.Client(host.WebToken(sub)).PostAsync("/api/v1/tickets", null).ReadAsync();
        var peek = await host.InScopeAsync(sp => sp.GetRequiredService<TicketStore>().PeekAsync(TestRecords.TicketOf(mintBody).Ticket, default));
        Assert.NotNull(peek);

        var results = await RaceAsync(Parallel, _ => host.InScopeAsync(sp => sp.GetRequiredService<TicketStore>().RedeemAsync(peek!, default)));

        Assert.Equal(sub, Assert.Single(results, r => r is not null));
    }

    [PostgresFact]
    public async Task A_ticket_rejected_for_a_duplicate_is_restored_by_the_rollback()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var sub = TestRecords.NewSub();
        var employer = TestRecords.NewEmployer();
        await host.Client(host.WebToken(sub)).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid(employer)));
        var (_, mintBody) = await host.Client(host.WebToken(sub)).PostAsync("/api/v1/tickets", null).ReadAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions/ticketed") { Content = TestRecords.Json(TestRecords.Valid(employer)) };
        request.Headers.Add(SubmissionHeaders.Ticket, TestRecords.TicketOf(mintBody).Ticket);

        var response = await host.Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().SubmissionTickets.CountAsync()));
    }

    [PostgresFact]
    public async Task Receipt_deletion_round_trips_and_parallel_deletes_all_get_the_same_answer()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(new() { ["Submission:Receipts:ResponseFloorMilliseconds"] = "0", ["Submission:Limits:ReceiptDeletePerIpPerMinute"] = "1000", ["Submission:Limits:ReceiptDeleteGlobalPerMinute"] = "1000" }, postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        var (_, body) = await host.Client(host.WebToken(TestRecords.NewSub())).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid())).ReadAsync();
        var code = TestRecords.ReceiptCodeOf(body);

        var results = await RaceAsync(Parallel, async _ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/receipts");
            request.Headers.Add(SubmissionHeaders.ReceiptCode, code);
            return (await host.Client().SendAsync(request)).StatusCode;
        });

        Assert.All(results, s => Assert.Equal(HttpStatusCode.NoContent, s));
        var counts = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            return (await db.Records.CountAsync(), await db.Receipts.CountAsync());
        });
        Assert.Equal((0, 0), counts);
    }

    [PostgresFact]
    public async Task The_purge_removes_old_rows_and_the_cascade_removes_receipts()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        await host.Client(host.WebToken(TestRecords.NewSub())).PostAsync("/api/v1/submissions", TestRecords.Json(TestRecords.Valid()));
        host.Clock.Advance(TimeSpan.FromDays(800));

        var counts = await host.InScopeAsync(sp => sp.GetRequiredService<RetentionPurger>().PurgeAsync(default));

        Assert.Equal(new PurgeCounts(1, 1, 0), counts);
        Assert.Equal(0, await host.InScopeAsync(async sp => await sp.GetRequiredService<InterviewDbContext>().Receipts.CountAsync()));
    }
}
