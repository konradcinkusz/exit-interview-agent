using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExitInterviewAgent.InterviewService.Persistence;

/// <summary>`dotnet ef` entry point: always PostgreSQL, because InMemory has no migrations. The connection string is a placeholder, never used to connect.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InterviewDbContext>
{
    public InterviewDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<InterviewDbContext>()
            .UseNpgsql("Host=localhost;Database=interviewdb;Username=design-time").Options);
}
