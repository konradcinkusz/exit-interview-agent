using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.AppHost.Tests;

/// <summary>
/// The composition root builds under the test host without starting a single container (no Docker needed), so these run in
/// every build. They check the resource graph the full-stack scenarios depend on: names, the wiring between them, and that the
/// configuration the fixture passes reaches the model.
/// </summary>
public sealed class CompositionTests
{
    [Fact]
    public async Task The_model_has_every_resource_the_full_stack_scenarios_call()
    {
        await using var app = await BuildModelAsync([]);
        var names = Resources(app).Select(r => r.Name).ToArray();

        foreach (var expected in new[] { "postgres", "pgadmin", "interviewdb", "authdb", "interview-service", "web", "authservice" })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public async Task The_identity_resource_is_left_out_when_identity_is_switched_off()
    {
        await using var app = await BuildModelAsync(["--Identity:Enabled=false"]);

        Assert.DoesNotContain("authservice", Resources(app).Select(r => r.Name));
    }

    private static IEnumerable<IResource> Resources(DistributedApplication app) =>
        app.Services.GetRequiredService<DistributedApplicationModel>().Resources;

    /// <summary>The composition as the full-stack fixture builds it, without the start: the same arguments, the same root.</summary>
    private static async Task<DistributedApplication> BuildModelAsync(string[] extra)
    {
        string[] args = ["--Parameters:interviews-provider=mock", "--Parameters:interviews-model=scripted", .. extra];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ExitInterviewAgent_AppHost>(args);
        return await builder.BuildAsync();
    }
}
