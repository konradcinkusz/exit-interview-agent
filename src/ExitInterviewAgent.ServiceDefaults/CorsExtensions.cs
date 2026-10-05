using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.ServiceDefaults;

public static class CorsPolicies
{
    public const string Frontend = "frontend";
}

public static class CorsExtensions
{
    /// <summary>One named policy from <c>Cors:AllowedOrigins</c>. The BFF is same-origin, so an empty list allows nothing.</summary>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration, string policyName)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
            .Where(o => !string.IsNullOrWhiteSpace(o)).ToArray() ?? [];
        return services.AddCors(o => o.AddPolicy(policyName, policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
            }
        }));
    }
}
