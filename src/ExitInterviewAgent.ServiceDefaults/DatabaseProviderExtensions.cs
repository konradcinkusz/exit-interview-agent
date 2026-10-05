using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.ServiceDefaults;

/// <summary>
/// Provider-portable persistence (P4): <c>DATABASE_PROVIDER=PostgreSQL</c> with a connection string,
/// otherwise InMemory so tests and a fresh clone need no container. Only PostgreSQL and InMemory
/// are supported (ADR-006).
/// </summary>
public static class DatabaseProviderExtensions
{
    public const string PostgreSql = "PostgreSQL";

    public static IServiceCollection AddDatabaseContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string connectionName, string inMemoryName)
        where TContext : DbContext
    {
        var connection = configuration.GetConnectionString(connectionName);
        var requested = configuration["DATABASE_PROVIDER"];
        var usePostgres = !string.IsNullOrWhiteSpace(connection)
            && (string.IsNullOrWhiteSpace(requested) || requested.Equals(PostgreSql, StringComparison.OrdinalIgnoreCase));

        if (usePostgres)
        {
            services.AddDbContext<TContext>(o => o.UseNpgsql(NormalizeConnectionString(connection!),
                npgsql => npgsql.EnableRetryOnFailure(10, TimeSpan.FromSeconds(30), null).CommandTimeout(60)));
        }
        else
        {
            services.AddDbContext<TContext>(o => o.UseInMemoryDatabase(inMemoryName));
        }
        services.AddSingleton(new DatabaseMode(usePostgres));
        return services.AddIntegration("database", usePostgres,
            usePostgres ? "PostgreSQL" : "InMemory (no connection string): data is lost on restart");
    }

    /// <summary>Fly private addressing: <c>.flycast</c> does not wake machines for databases; use <c>.internal</c>. Raises the cold-start timeout.</summary>
    public static string NormalizeConnectionString(string connectionString)
    {
        var normalized = connectionString.Replace(".flycast", ".internal", StringComparison.OrdinalIgnoreCase);
        return normalized.Contains("Timeout=", StringComparison.OrdinalIgnoreCase) ? normalized : normalized.TrimEnd(';') + ";Timeout=30";
    }
}

/// <summary>Whether the real provider is in use; the migration service applies migrations only then.</summary>
public sealed record DatabaseMode(bool IsRelational);
