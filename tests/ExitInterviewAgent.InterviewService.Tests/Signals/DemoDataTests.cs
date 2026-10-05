using System.Net;
using System.Text.Json;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Infrastructure;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.InterviewService.Signals;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static ExitInterviewAgent.InterviewService.Tests.Signals.SignalsHarness;

namespace ExitInterviewAgent.InterviewService.Tests.Signals;

public sealed class DemoDataTests
{
    private static Dictionary<string, string?> Demo(string mode = "Seed") => ManualPublishing(new() { ["Signals:Demo:Mode"] = mode });

    private static async Task<SignalsEmployerList> WaitForListAsync(TestHost host, Func<SignalsEmployerList, bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        SignalsEmployerList list;
        do
        {
            await Task.Delay(200);
            list = JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers")).Body, JsonSerializerOptions.Web)!;
        } while (!ready(list) && DateTime.UtcNow < deadline);
        return list;
    }

    [Fact]
    public void The_generator_is_deterministic_stays_in_its_namespace_and_tells_six_stories()
    {
        var first = DemoRecords.Generate(42);
        var again = DemoRecords.Generate(42);

        Assert.Equal(first.Select(r => (r.Sub, r.Employer, Convert.ToHexString(r.Json))), again.Select(r => (r.Sub, r.Employer, Convert.ToHexString(r.Json))));
        Assert.All(first, r => Assert.StartsWith(DemoRecords.Prefix, r.Employer));
        Assert.Equal(first.Count, first.Select(r => r.Sub).Distinct().Count());
        Assert.Equal(
            new Dictionary<string, int> { ["demo-harbor-logistics"] = 42, ["demo-birchwood-software"] = 18, ["demo-quill-and-ink"] = 5, ["demo-tiny-bakery"] = 4, ["demo-skewed-bands"] = 26, ["demo-unanimous-works"] = 12 },
            first.GroupBy(r => r.Employer).ToDictionary(g => g.Key, g => g.Count()));
        Assert.NotEqual(Convert.ToHexString(first[0].Json), Convert.ToHexString(DemoRecords.Generate(7)[0].Json));
    }

    [Fact]
    public async Task Seeding_goes_through_the_real_pipeline_and_the_demo_tells_each_rule_of_the_aggregation()
    {
        using var host = new TestHost(Demo());
        await host.WaitReadyAsync();

        var list = await WaitForListAsync(host, l => l.Employers.Count > 0);

        // The tiny bakery (4 records) is simply not there; the other five are, alphabetically.
        Assert.Equal(["demo-birchwood-software", "demo-harbor-logistics", "demo-quill-and-ink", "demo-skewed-bands", "demo-unanimous-works"], list.Employers);

        async Task<SignalsEmployer> Get(string employer) => JsonSerializer.Deserialize<SignalsEmployer>((await GetAsync(host, $"/api/v1/signals/employers/{employer}")).Body, JsonSerializerOptions.Web)!;

        var harbor = await Get("demo-harbor-logistics");
        Assert.Equal("25-49", harbor.RespondentsBand);
        Assert.All(harbor.Topics.Single(t => t.Topic == "management").Cuts, c => Assert.Equal(SignalsVocabulary.CutPublished, c.Status)); // every band populated: clean cuts
        Assert.True(harbor.Topics.Single(t => t.Topic == "management").Overall!.Mean < harbor.Topics.Single(t => t.Topic == "culture").Overall!.Mean);

        var skewed = await Get("demo-skewed-bands");
        var skewedTopic = skewed.Topics[0];
        Assert.Equal(SignalsVocabulary.CutSuppressed, skewedTopic.Cuts.Single(c => c.Dimension == "tenure").Status); // one band of three: the whole cut is withheld
        Assert.Equal(SignalsVocabulary.CutPublished, skewedTopic.Cuts.Single(c => c.Dimension == "seniority").Status);

        Assert.Equal(5, (await Get("demo-quill-and-ink")).Topics[0].Overall!.N); // exactly k

        var unanimous = (await Get("demo-unanimous-works")).Topics.Single(t => t.Topic == "culture").Overall!;
        Assert.Equal(5.0, unanimous.Mean);
        Assert.True(unanimous.Interval.Upper - unanimous.Interval.Lower > 0.5);   // a wide interval, not a point

        var birchwood = await Get("demo-birchwood-software");
        Assert.NotEqual(SignalsVocabulary.CoverageHigh, birchwood.Topics.Single(t => t.Topic == "growth").Overall!.Coverage); // the counter-metric shows the skipped topic
    }

    [Fact]
    public async Task The_seeded_records_are_real_records_with_receipts_and_ledger_entries_of_synthetic_accounts()
    {
        using var host = new TestHost(Demo());
        await host.WaitReadyAsync();
        await WaitForListAsync(host, l => l.Employers.Count > 0);

        var (records, receipts, ledger) = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            return (await db.Records.CountAsync(), await db.Receipts.CountAsync(), await db.SubmissionLedger.CountAsync());
        });

        Assert.Equal(107, records);
        Assert.Equal(107, receipts);
        Assert.Equal(107, ledger);
    }

    [Fact]
    public async Task Reset_removes_only_the_demo_namespace_from_every_store_and_the_snapshot()
    {
        using var host = new TestHost(Demo());
        await host.WaitReadyAsync();
        await WaitForListAsync(host, l => l.Employers.Count > 0);
        await SubmitManyAsync(host, "real-employer", 5); // a record outside the namespace must survive

        var removed = await host.InScopeAsync(sp => sp.GetRequiredService<DemoDataService>().ResetAsync(default));
        await host.InScopeAsync(sp => sp.GetRequiredService<ExitInterviewAgent.Signals.SnapshotPublisher>().RepublishAsync(default));

        Assert.Equal(107, removed);
        var (records, receipts, ledger, demoLeft) = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<InterviewDbContext>();
            return (await db.Records.CountAsync(), await db.Receipts.CountAsync(), await db.SubmissionLedger.CountAsync(), await db.Records.CountAsync(r => r.EmployerRef.StartsWith("demo-")));
        });
        Assert.Equal((5, 5, 5, 0), (records, receipts, ledger, demoLeft));
        Assert.Equal(["real-employer"], JsonSerializer.Deserialize<SignalsEmployerList>((await GetAsync(host, "/api/v1/signals/employers")).Body, JsonSerializerOptions.Web)!.Employers);
    }

    [Fact]
    public async Task Seeding_twice_converges_instead_of_accumulating()
    {
        using var host = new TestHost(Demo());
        await host.WaitReadyAsync();
        await WaitForListAsync(host, l => l.Employers.Count > 0);

        var service = host.Services.GetRequiredService<DemoDataService>();
        await host.InScopeAsync(async _ => await service.ResetAsync(default));
        var accepted = await service.SeedAsync(42, default);

        Assert.Equal(107, accepted);
        Assert.Equal(107, await host.InScopeAsync(sp => sp.GetRequiredService<InterviewDbContext>().Records.CountAsync()));
    }

    [Fact]
    public async Task Demo_data_is_visible_on_health_and_is_a_degraded_state()
    {
        using var host = new TestHost(Demo());
        await host.WaitReadyAsync();

        var health = JsonDocument.Parse(await host.Client().GetStringAsync("/health")).RootElement;
        var demo = health.GetProperty("integrations").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "signals-demo-data");

        Assert.False(demo.GetProperty("configured").GetBoolean());
        Assert.Contains("DEMO DATA ACTIVE", demo.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Without_the_setting_there_is_no_demo_service_and_no_demo_entry()
    {
        using var host = new TestHost(ManualPublishing());
        await host.WaitReadyAsync();

        var health = JsonDocument.Parse(await host.Client().GetStringAsync("/health")).RootElement;

        Assert.DoesNotContain(health.GetProperty("integrations").EnumerateArray(), i => i.GetProperty("name").GetString() == "signals-demo-data");
        Assert.Null(host.Services.GetService<DemoDataService>());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, "/api/v1/signals/employers")).Response.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Demo_mode_outside_development_stops_the_service_at_startup(string environmentName)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Signals:Demo:Mode"] = "Seed" }).Build();
        var environment = new Environment(environmentName);

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddSignalsModule(configuration, environment));

        Assert.Contains("Development", error.Message);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
