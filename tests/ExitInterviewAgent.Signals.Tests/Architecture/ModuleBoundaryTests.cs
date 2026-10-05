using System.Reflection;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ExitInterviewAgent.Signals.Tests.Architecture;

/// <summary>
/// ADR-0052 made mechanical: the module can be extracted because it shares nothing with the submission side but the kernel, and its
/// store cannot hold, or be sorted by, anything about a single record.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly Signals = typeof(Observation).Assembly;

    [Fact]
    public void The_module_references_the_kernel_and_no_other_project_of_the_solution()
    {
        var referenced = Signals.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("ExitInterviewAgent.", StringComparison.Ordinal)).ToArray();

        Assert.Equal(["ExitInterviewAgent.ServiceDefaults"], referenced);
    }

    [Fact]
    public void No_public_type_of_the_module_is_named_like_a_record_or_a_quote_or_a_person()
    {
        var names = Signals.GetExportedTypes().Select(t => t.Name).ToArray();

        Assert.DoesNotContain(names, n => new[] { "Record", "Quote", "Interview", "Receipt", "Ledger", "Account", "Person" }.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_observation_carries_exactly_what_the_rules_read_and_nothing_that_identifies_a_record()
    {
        var properties = typeof(Observation).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["EmployerRef", "FunctionBand", "Ratings", "SeniorityBand", "TenureBand", "Verification"], properties);
        Assert.Equal(["Rating", "Topic"], typeof(TopicRating).GetProperties().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public void The_store_holds_published_aggregates_only_and_no_column_a_query_could_rank_by()
    {
        using var db = Support.Data.NewDb();
        var columns = db.Model.GetEntityTypes().ToDictionary(
            e => e.GetTableName()!, e => e.GetProperties().Select(p => p.Name).Order().ToArray());

        Assert.Equal(["EmployerSnapshots", "Snapshots"], columns.Keys.Order());
        Assert.Equal(["EmployerRef", "SnapshotId", "View"], columns["EmployerSnapshots"]);
        Assert.Equal(["EmployerCount", "Fingerprint", "Id", "IntervalHours", "MinimumGroupSize", "PeriodStart", "RulesVersion", "Seq"], columns["Snapshots"]);
        var all = columns.Values.SelectMany(c => c).ToArray();
        Assert.DoesNotContain(all, c => new[] { "Json", "Quote", "Interview", "Score", "Rank", "Rating", "Mean", "Receipt" }.Any(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)));
        Assert.All(db.Model.GetEntityTypes(), e => Assert.Equal(SignalsDbContext.Schema, e.GetSchema()));
    }

    [Fact]
    public void The_reader_exposes_no_query_by_value()
    {
        var methods = typeof(SnapshotReader).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name).Order().ToArray();

        Assert.Equal(["CurrentAsync", "GetAsync", "ListAsync"], methods);
    }

    [Fact]
    public void The_published_shape_has_no_free_text_field()
    {
        var types = new[] { typeof(EmployerView), typeof(TopicView), typeof(CutView), typeof(BandView), typeof(StatsView), typeof(GroupView) };
        var strings = types.SelectMany(t => t.GetProperties()).Where(p => p.PropertyType == typeof(string)).Select(p => $"{p.DeclaringType!.Name}.{p.Name}").Order().ToArray();

        // Only closed vocabularies: the employer reference, topic and band names, statuses, and three banded labels.
        Assert.Equal(
            ["BandView.Band", "BandView.Status", "CutView.Dimension", "EmployerView.EmployerRef", "EmployerView.RespondentsBand", "GroupView.Key",
             "StatsView.Coverage", "StatsView.Reliability", "TopicView.Topic"], strings);
    }
}
