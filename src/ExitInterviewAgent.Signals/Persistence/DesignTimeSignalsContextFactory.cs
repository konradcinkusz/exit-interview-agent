using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExitInterviewAgent.Signals.Persistence;

/// <summary>`dotnet ef` entry point: always PostgreSQL, because InMemory has no migrations. The connection string is a placeholder, never used to connect.</summary>
public sealed class DesignTimeSignalsContextFactory : IDesignTimeDbContextFactory<SignalsDbContext>
{
    public SignalsDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SignalsDbContext>()
            .UseNpgsql("Host=localhost;Database=interviewdb;Username=design-time", SignalsServiceCollectionExtensions.ConfigureNpgsql).Options);
}
