using Npgsql;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>
/// A throwaway database on a real PostgreSQL server named by <c>TEST_POSTGRES_CONNECTION</c> (a connection string with
/// the rights to create databases). Without it, tests marked <see cref="PostgresFactAttribute"/> are SKIPPED, and the
/// skip is reported as such: a gate that did not run is not a gate that passed.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    public const string Variable = "TEST_POSTGRES_CONNECTION";
    private readonly string _admin;
    private readonly string _name;

    private PostgresTestDatabase(string admin, string name, string connectionString)
    {
        _admin = admin;
        _name = name;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable));

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var admin = Environment.GetEnvironmentVariable(Variable) ?? throw new InvalidOperationException(Variable + " is not set.");
        var name = "t5_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = true, MaxPoolSize = 60 };
        return new PostgresTestDatabase(admin, name, builder.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresTestDatabase.Available)
        {
            Skip = $"NOT RUN: {PostgresTestDatabase.Variable} is not set (needs a PostgreSQL server).";
        }
    }
}
