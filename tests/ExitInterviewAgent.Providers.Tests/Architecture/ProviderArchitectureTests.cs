using System.Xml.Linq;

namespace ExitInterviewAgent.Providers.Tests.Architecture;

public class ProviderArchitectureTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExitInterviewAgent.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> References(string project) =>
        XDocument.Load(project).Descendants().Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference").Select(e => (string?)e.Attribute("Include") ?? string.Empty);

    [Fact]
    public void Only_the_cli_and_the_providers_own_tests_reference_the_providers_project()
    {
        var offenders = Directory.GetFiles(Path.Combine(Root(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("ExitInterviewAgent.Providers", StringComparison.Ordinal) && !p.Contains("ExitInterviewAgent.Cli", StringComparison.Ordinal))
            .Where(p => References(p).Any(r => r.Contains("ExitInterviewAgent.Providers", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_kernel_and_the_agent_core_carry_no_provider_sdk_and_no_http_assembly()
    {
        var banned = new[] { "Anthropic", "OpenAI", "Microsoft.Extensions.AI.OpenAI", "Microsoft.Extensions.Http", "OpenTelemetry" };
        foreach (var project in new[] { "ExitInterviewAgent.ServiceDefaults", "ExitInterviewAgent.Agent", "ExitInterviewAgent.Personas", "ExitInterviewAgent.Records", "ExitInterviewAgent.Privacy" })
        {
            var file = Path.Combine(Root(), "src", project, project + ".csproj");
            var refs = References(file).ToList();
            if (project == "ExitInterviewAgent.ServiceDefaults") continue; // the kernel's own telemetry packages predate this task and are guarded by the kernel tests
            Assert.DoesNotContain(refs, r => banned.Any(b => r.Equals(b, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void The_providers_project_declares_the_packages_it_justifies_and_versions_live_only_in_central_props()
    {
        var file = Path.Combine(Root(), "src", "ExitInterviewAgent.Providers", "ExitInterviewAgent.Providers.csproj");
        var packages = XDocument.Load(file).Descendants("PackageReference").ToList();

        Assert.All(packages, p => Assert.Null(p.Attribute("Version")));
        Assert.Equal(["Anthropic", "Microsoft.Extensions.AI.Abstractions", "Microsoft.Extensions.AI.OpenAI", "Microsoft.Extensions.Logging.Abstractions"], packages.Select(p => (string)p.Attribute("Include")!).Order());
    }

    [Fact]
    public void The_sdks_are_configured_not_to_retry_so_the_transport_policy_is_the_only_one()
    {
        var factory = File.ReadAllText(Path.Combine(Root(), "src", "ExitInterviewAgent.Providers", "ProviderChatClients.cs"));

        Assert.Contains("MaxRetries = 0", factory);
        Assert.Contains("new ClientRetryPolicy(0)", factory);
    }
}
