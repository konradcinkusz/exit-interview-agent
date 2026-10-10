using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Architecture;

/// <summary>
/// SECURITY-REVIEW §8, made mechanical: deny by default, with an anonymous list short enough to read aloud. A new endpoint
/// that is neither behind a named policy nor on this list fails the build, so "I forgot the group" cannot reach review.
/// </summary>
public sealed class EndpointAuthorizationMatrixTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private static readonly string[] Anonymous =
    [
        "/health", "/alive",
        "/.well-known/oauth-protected-resource",
        "/.well-known/oauth-protected-resource/mcp",
        // T5: no account by design. Both are rate limited per client and globally (ADR-0029, ADR-0030) and read their secret from a header.
        "/api/v1/receipts",
        "/api/v1/submissions/ticketed",
    ];

    private static readonly string[] DevelopmentOnlyAnonymous = ["/openapi/{documentName}.json"];

    private IEnumerable<RouteEndpoint> Endpoints() =>
        factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>();

    private static string Route(RouteEndpoint e) => e.RoutePattern.RawText ?? "";

    [Fact]
    public void The_anonymous_endpoints_are_exactly_the_known_list()
    {
        var anonymous = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(Route)
            .Order()
            .ToArray();

        Assert.Equal(Anonymous.Concat(DevelopmentOnlyAnonymous).Order().ToArray(), anonymous);
    }

    [Fact]
    public void Every_anonymous_domain_endpoint_has_its_own_rate_limit_policy()
    {
        var anonymousApi = Endpoints().Where(e => Route(e).StartsWith("/api/", StringComparison.Ordinal) && e.Metadata.GetMetadata<IAllowAnonymous>() is not null).ToList();

        Assert.NotEmpty(anonymousApi);
        foreach (var endpoint in anonymousApi)
        {
            var policy = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName;
            Assert.False(string.IsNullOrEmpty(policy), $"{Route(endpoint)} is anonymous and has no rate-limit policy");
            Assert.NotEqual(ExitInterviewAgent.ServiceDefaults.ApiExtensions.ApiPolicy, policy);
        }
    }

    [Fact]
    public void Every_other_endpoint_names_the_policy_for_its_audience()
    {
        // A route can carry several methods (GET and DELETE on one session), so the matrix is one row per distinct route and policy.
        var matrix = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0)
            .Select(e => (Route: Route(e), Policy: string.Join(',', e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy))))
            .Distinct()
            .ToList();

        Assert.NotEmpty(matrix);
        foreach (var (route, policy) in matrix)
        {
            Assert.True(policy is AuthPolicies.Account or AuthPolicies.McpSubmit, $"{route} requires '{policy}', not a named audience policy");
            Assert.Equal(route.StartsWith("/mcp", StringComparison.Ordinal) ? AuthPolicies.McpSubmit : AuthPolicies.Account, policy);
        }
    }
}
