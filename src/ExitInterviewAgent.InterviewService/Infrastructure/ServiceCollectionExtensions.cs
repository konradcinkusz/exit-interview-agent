using ExitInterviewAgent.InterviewService.Endpoints;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.ServiceDefaults;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>This service's own wiring; Program.cs only lists capabilities.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInterviewPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseContext<InterviewDbContext>(configuration, "interviewdb", "InterviewInMemory");
        services.AddMigrationOnStartup<InterviewDbContext>();
        return services;
    }

    public static WebApplication UseInterviewPipeline(this WebApplication app)
    {
        app.UseCors(CorsPolicies.Frontend);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        return app;
    }

    /// <summary>The authorization triad (SERVICE-API-PATTERNS §2): public and authenticated groups, visible here.</summary>
    public static WebApplication MapInterviewEndpoints(this WebApplication app)
    {
        var authApi = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting(ApiExtensions.ApiPolicy).WithValidation();
        authApi.MapAccountEndpoints();
        return app;
    }
}
