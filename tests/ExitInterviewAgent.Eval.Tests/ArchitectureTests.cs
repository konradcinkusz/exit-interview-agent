using System.Reflection;
using System.Xml.Linq;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Scenarios;

namespace ExitInterviewAgent.Eval.Tests;

/// <summary>The anti-goals of METHODOLOGY §1, enforced by an absence in the code and not only by prose.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Eval = typeof(ModelProfile).Assembly;

    [Fact]
    public void The_eval_project_references_only_the_agent_personas_records_and_privacy_projects()
    {
        var csproj = XDocument.Load(Path.Combine(RepoLayout.Root, "src", "ExitInterviewAgent.Eval", "ExitInterviewAgent.Eval.csproj"));

        var refs = csproj.Descendants("ProjectReference").Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/'))).Order().ToList();

        Assert.Equal(["ExitInterviewAgent.Agent", "ExitInterviewAgent.Personas", "ExitInterviewAgent.Privacy", "ExitInterviewAgent.Records"], refs);
    }

    [Fact]
    public void The_harness_has_no_employer_entity_and_does_not_reference_signals_the_service_the_kernel_or_the_cli()
    {
        var referenced = Eval.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(Eval.GetTypes(), t => t.Name.Contains("Employer", StringComparison.OrdinalIgnoreCase) && !t.Name.StartsWith("<", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.Contains("Signals", StringComparison.OrdinalIgnoreCase) || n.Contains("InterviewService", StringComparison.Ordinal) || n.Contains("ServiceDefaults", StringComparison.Ordinal) || n.EndsWith(".Cli", StringComparison.Ordinal) || n.Contains("Contracts", StringComparison.Ordinal));
    }

    [Fact]
    public void The_harness_makes_no_network_call_of_its_own()
    {
        var fields = Eval.GetTypes().SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));

        Assert.DoesNotContain(fields, f => f.FieldType.FullName == "System.Net.Http.HttpClient");
        Assert.DoesNotContain(Eval.GetReferencedAssemblies(), a => a.Name is "System.Net.Sockets" or "System.Net.WebClient");
    }

    [Fact]
    public void No_type_in_the_harness_models_a_person_a_sentiment_or_an_emotion()
    {
        var names = Eval.GetTypes().SelectMany(t => t.GetProperties().Select(p => p.Name).Prepend(t.Name)).ToList();

        Assert.DoesNotContain(names, n => new[] { "Sentiment", "Emotion", "Mood", "Affect", "Frustration" }.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_metric_catalogue_pairs_every_gated_pressurable_metric_with_a_counter_metric_that_exists()
    {
        var ids = Reporting.MetricCatalog.All.Select(m => m.Id).ToHashSet();

        foreach (var m in Reporting.MetricCatalog.All.Where(m => m.Id is "coverage" or "lqr" or "fuv" or "tf"))
        {
            Assert.NotNull(m.Counter);
            Assert.Contains(m.Counter!, ids);
        }
    }

    [Fact]
    public void Heuristics_about_people_have_no_place_in_the_gate_the_gated_metrics_are_all_about_the_interview()
    {
        var gated = Reporting.MetricCatalog.All.Where(m => m.Gated).Select(m => m.Id).ToList();

        Assert.DoesNotContain(gated, id => id.Contains("sentiment", StringComparison.Ordinal) || id.Contains("emotion", StringComparison.Ordinal) || id.Contains("frustration", StringComparison.Ordinal));
    }
}
