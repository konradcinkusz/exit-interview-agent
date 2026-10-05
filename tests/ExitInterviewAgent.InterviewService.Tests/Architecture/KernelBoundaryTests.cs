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
}
