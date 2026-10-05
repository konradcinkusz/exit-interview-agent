using System.Net;
using System.Text.Json;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Tests.Support;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static ExitInterviewAgent.InterviewService.Tests.Signals.SignalsHarness;

namespace ExitInterviewAgent.InterviewService.Tests.Signals;

/// <summary>
/// Against a real PostgreSQL server (TEST_POSTGRES_CONNECTION), where CI already runs the T5 tests: the module's own migration in its own
/// schema with its own history table, the unique fingerprint, and the whole path from submission to the signals API. Without the
/// variable these are SKIPPED and reported as not run.
/// </summary>
public sealed class PostgresSignalsTests
{
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
    public async Task The_migration_creates_the_signals_schema_with_exactly_the_documented_columns_and_its_own_history()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(ManualPublishing(), postgres: pg.ConnectionString);
        await host.WaitReadyAsync();

        var columns = (await QueryAsync(pg.ConnectionString,
            "select table_name, column_name from information_schema.columns where table_schema='signals' and table_name <> '__EFMigrationsHistory' order by 1,2"))
            .GroupBy(r => r[0]!).ToDictionary(g => g.Key, g => g.Select(r => r[1]!).Order().ToArray());

        Assert.Equal(["EmployerSnapshots", "Snapshots"], columns.Keys.Order());
        Assert.Equal(["EmployerRef", "SnapshotId", "View"], columns["EmployerSnapshots"]);
        Assert.Equal(["EmployerCount", "Fingerprint", "Id", "IntervalHours", "MinimumGroupSize", "PeriodStart", "RulesVersion", "Seq"], columns["Snapshots"]);
        Assert.Equal([["1"]], await QueryAsync(pg.ConnectionString, "select count(*) from signals.\"__EFMigrationsHistory\""));
        var unique = await QueryAsync(pg.ConnectionString, "select indexname from pg_indexes where schemaname='signals' and indexdef like 'CREATE UNIQUE%'");
        Assert.Contains(unique, r => r[0] == "IX_Snapshots_Fingerprint");
        Assert.Contains(unique, r => r[0] == "IX_Snapshots_Id");
        Assert.False(await host.InScopeAsync(sp => Task.FromResult(sp.GetRequiredService<SignalsDbContext>().Database.HasPendingModelChanges())));
        // The submission side's schema is untouched: the interview tables stay in public, the module's in signals, and they share no key.
        Assert.Empty(await QueryAsync(pg.ConnectionString, "select 1 from pg_constraint where contype='f' and connamespace='signals'::regnamespace"));
        Assert.Empty(await QueryAsync(pg.ConnectionString, "select 1 from information_schema.tables where table_schema='public' and table_name in ('Snapshots','EmployerSnapshots')"));
    }

    [PostgresFact]
    public async Task From_submission_to_the_signals_api_on_postgresql()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(ManualPublishing(), postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "acme", 6, i => Record("acme", rating: 3 + i % 3));
        await SubmitManyAsync(host, "tiny", 4);

        Assert.Equal(ExitInterviewAgent.Signals.PublishOutcome.Published, await PublishNextBatchAsync(host));

        var list = JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers")).Body, JsonSerializerOptions.Web)!;
        Assert.Equal(["acme"], list.Employers);
        var detail = await GetAsync(host, "/api/v1/signals/employers/acme");
        Assert.Equal(HttpStatusCode.OK, detail.Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(host, "/api/v1/signals/employers/tiny")).Response.StatusCode);
        // Same period again: nothing to do, and nothing is rewritten.
        Assert.Equal(ExitInterviewAgent.Signals.PublishOutcome.Skipped, await host.InScopeAsync(sp => sp.GetRequiredService<ExitInterviewAgent.Signals.SnapshotPublisher>().RunDueAsync(default)));
        Assert.Equal([["1"]], await QueryAsync(pg.ConnectionString, "select count(*) from signals.\"Snapshots\""));
    }

    [PostgresFact]
    public async Task Two_publishers_racing_for_one_period_leave_exactly_one_snapshot()
    {
        await using var pg = await PostgresTestDatabase.CreateAsync();
        using var host = new TestHost(ManualPublishing(), postgres: pg.ConnectionString);
        await host.WaitReadyAsync();
        await SubmitManyAsync(host, "acme", 5);
        host.Clock.Advance(TimeSpan.FromDays(1));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            host.InScopeAsync(sp => sp.GetRequiredService<ExitInterviewAgent.Signals.SnapshotPublisher>().RunDueAsync(default)))));

        Assert.Equal(1, outcomes.Count(o => o == ExitInterviewAgent.Signals.PublishOutcome.Published));
        Assert.Equal([["1", "1"]], await QueryAsync(pg.ConnectionString, "select (select count(*) from signals.\"Snapshots\"), (select count(distinct \"SnapshotId\") from signals.\"EmployerSnapshots\")"));
    }
}
