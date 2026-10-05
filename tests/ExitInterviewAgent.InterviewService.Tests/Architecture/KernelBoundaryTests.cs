using System.Reflection;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace ExitInterviewAgent.InterviewService.Tests.Architecture;

/// <summary>P2 made mechanical: the shared kernel is plumbing, never domain.</summary>
public sealed class KernelBoundaryTests
{
    private static readonly Assembly Kernel = typeof(Extensions).Assembly;

    [Fact]
    public void Kernel_references_neither_the_contracts_nor_any_service()
    {
        var referenced = Kernel.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(referenced, n => n.StartsWith("ExitInterviewAgent.", StringComparison.Ordinal)
            && n != "ExitInterviewAgent.ServiceDefaults");
    }

    [Fact]
    public void Kernel_declares_no_db_context_and_no_entity_like_type()
    {
        var offenders = Kernel.GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t)
                || t.GetCustomAttributes().Any(a => a.GetType().Name is "TableAttribute" or "OwnedAttribute")
                || t.GetProperties().Any(p => p.GetCustomAttributes().Any(a => a.GetType().Name is "KeyAttribute")))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Kernel_exposes_no_public_enum_or_user_facing_resource()
    {
        var offenders = Kernel.GetExportedTypes().Where(t => t.IsEnum).Select(t => t.FullName).ToArray();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The identity wiring (schemes, scopes, policies, MCP options) and the log scrubber live in the service that
    /// owns them. The kernel's public surface is an allowlist: a new type needs a deliberate edit here, in review.
    /// </summary>
    [Fact]
    public void Kernel_public_surface_is_the_known_plumbing_and_nothing_else()
    {
        string[] plumbing =
        [
            "ApiExtensions", "AuthenticationExtensions", "CorsPolicies", "CorsExtensions", "DatabaseProviderExtensions",
            "DatabaseMode", "Extensions", "IntegrationStatus", "IntegrationExtensions", "MigrationCompletionSignal",
            "MigrationExtensions",
        ];

        var unexpected = Kernel.GetExportedTypes().Select(t => t.Name).Except(plumbing).ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void Kernel_knows_nothing_about_mcp_consent_or_accounts()
    {
        var offenders = Kernel.GetTypes().SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => $"{t.Name}.{m.Name}").Append(t.Name))
            .Where(n => new[] { "Mcp", "Consent", "Account", "Interview", "Employer", "Receipt", "Ledger" }
                .Any(word => n.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(offenders);
    }
}
