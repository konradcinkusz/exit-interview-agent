using System.Diagnostics.Metrics;
using ExitInterviewAgent.Signals.Persistence;
using ExitInterviewAgent.Signals.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.Signals.Tests.Publishing;

public sealed class PublisherTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 15, 37, 12, TimeSpan.Zero);

    private sealed class Rig : IDisposable
    {
        public Rig(int k = 5, TimeSpan? interval = null, string? dbName = null)
        {
            Name = dbName ?? Guid.NewGuid().ToString("N");
            Options = new SignalsOptions { MinimumGroupSize = k, PublishInterval = interval ?? TimeSpan.FromDays(1) };
            Clock = new FakeClock(Noon);
            State = new PublicationState();
            Metrics = new SignalsMetrics();
            Logs = new CaptureLogger();
            Factory = LoggerFactory.Create(b => b.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        }

        public string Name { get; }
        public SignalsOptions Options { get; }
        public FakeClock Clock { get; }
        public PublicationState State { get; }
        public SignalsMetrics Metrics { get; }
        public CaptureLogger Logs { get; }
        public ILoggerFactory Factory { get; }
        public ListSource Source { get; } = new();

        /// <summary>A publisher as the host would construct it for one scope: a fresh context on the same store.</summary>
        public SnapshotPublisher Publisher() => new(Data.NewDb(Name), Source, Microsoft.Extensions.Options.Options.Create(Options), Clock, Metrics, State, Factory.CreateLogger<SnapshotPublisher>());

        public SnapshotReader Reader() => new(Data.NewDb(Name));

        public void Dispose()
        {
            Metrics.Dispose();
            Factory.Dispose();
        }
    }

    private static IEnumerable<Observation> Employer(string name, int count, int rating = 4)
        => Data.Many(count, i => Data.Rated(name, rating, Data.Tenures[i % 2], Data.Seniorities[i % 2], Data.Functions[i % 2]));

    [Fact]
    public async Task The_first_run_publishes_and_the_reader_serves_it()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 10));

        Assert.Equal(PublishOutcome.Published, await rig.Publisher().RunDueAsync(default));

        var page = await rig.Reader().ListAsync(1, 25, default);
        Assert.Equal(["acme"], page.Employers);
        Assert.Equal(1, page.Total);
        var (snapshot, view) = (await rig.Reader().GetAsync("acme", default))!.Value;
        Assert.Equal(DisclosureRules.Version, snapshot.RulesVersion);
        Assert.Equal(5, snapshot.MinimumGroupSize);
        Assert.Equal(10, view.Topics[0].Overall!.N);
    }

    [Fact]
    public async Task A_second_run_in_the_same_period_does_nothing_and_does_not_even_read_the_feed()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 10));
        await rig.Publisher().RunDueAsync(default);

        Assert.Equal(PublishOutcome.Skipped, await rig.Publisher().RunDueAsync(default));
        rig.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(PublishOutcome.Skipped, await rig.Publisher().RunDueAsync(default));
        Assert.Equal(1, rig.Source.Reads);
    }

    [Fact]
    public async Task A_restart_is_safe_because_only_the_database_remembers()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 10));
        await rig.Publisher().RunDueAsync(default);

        // A new process: new state, new metrics, same store.
        using var restarted = new Rig(dbName: rig.Name);
        restarted.Source.Items.AddRange(Employer("acme", 10));

        Assert.Equal(PublishOutcome.Skipped, await restarted.Publisher().RunDueAsync(default));
        Assert.Equal(0, restarted.Source.Reads);
    }

    [Fact]
    public async Task Submissions_between_batches_change_nothing_and_the_next_period_picks_them_up()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 4));
        await rig.Publisher().RunDueAsync(default);
        Assert.Null(await rig.Reader().GetAsync("acme", default)); // k - 1: nothing

        rig.Source.Items.AddRange(Employer("acme", 2)); // six records now
        rig.Clock.Advance(TimeSpan.FromHours(6));
        await rig.Publisher().RunDueAsync(default);
        Assert.Null(await rig.Reader().GetAsync("acme", default)); // same batch: the timing of a submission shows nothing

        rig.Clock.Set(new DateTimeOffset(2026, 10, 6, 0, 0, 5, TimeSpan.Zero));
        Assert.Equal(PublishOutcome.Published, await rig.Publisher().RunDueAsync(default));
        Assert.NotNull(await rig.Reader().GetAsync("acme", default));
    }

    [Fact]
    public async Task A_deletion_after_publication_takes_effect_at_the_next_batch_not_before()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 5));
        await rig.Publisher().RunDueAsync(default);

        rig.Source.Items.RemoveAt(0); // the record is deleted (receipt code): four are left
        rig.Clock.Advance(TimeSpan.FromHours(2));
        await rig.Publisher().RunDueAsync(default);
        Assert.NotNull(await rig.Reader().GetAsync("acme", default)); // honest: still in the published batch

        rig.Clock.Set(new DateTimeOffset(2026, 10, 6, 0, 0, 1, TimeSpan.Zero));
        await rig.Publisher().RunDueAsync(default);
        Assert.Null(await rig.Reader().GetAsync("acme", default));
        Assert.Empty((await rig.Reader().ListAsync(1, 25, default)).Employers);
    }

    [Fact]
    public async Task The_generation_time_is_the_start_of_the_batch_never_the_moment_the_run_finished()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 5));
        await rig.Publisher().RunDueAsync(default);

        var snapshot = (await rig.Reader().CurrentAsync(default))!;

        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), snapshot.GeneratedAt); // 15:37:12 is not stored anywhere
        Assert.Equal(24, snapshot.IntervalHours);
        await using var db = Data.NewDb(rig.Name);
        Assert.All(await db.Snapshots.ToListAsync(), s => Assert.Equal(TimeSpan.Zero, s.PeriodStart.TimeOfDay));
    }

    [Theory]
    [InlineData(1, "2026-10-05T15:00:00Z")]
    [InlineData(6, "2026-10-05T12:00:00Z")]
    [InlineData(24, "2026-10-05T00:00:00Z")]
    public void Period_start_is_aligned_to_the_interval(int hours, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected), SnapshotPublisher.PeriodStart(Noon, TimeSpan.FromHours(hours)));

    [Fact]
    public async Task A_new_period_replaces_the_snapshot_and_removes_the_old_rows()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 5));
        await rig.Publisher().RunDueAsync(default);
        rig.Clock.Advance(TimeSpan.FromDays(1));
        await rig.Publisher().RunDueAsync(default);

        await using var db = Data.NewDb(rig.Name);
        Assert.Equal(1, await db.Snapshots.CountAsync());
        Assert.Equal(1, await db.EmployerSnapshots.CountAsync());
    }

    [Fact]
    public async Task A_change_of_k_republishes_at_once_and_a_stricter_k_hides_what_it_no_longer_allows()
    {
        using var rig = new Rig(k: 5);
        rig.Source.Items.AddRange(Employer("acme", 6));
        await rig.Publisher().RunDueAsync(default);
        Assert.NotNull(await rig.Reader().GetAsync("acme", default));

        rig.Options.MinimumGroupSize = 8;
        Assert.Equal(PublishOutcome.Published, await rig.Publisher().RunDueAsync(default));

        Assert.Null(await rig.Reader().GetAsync("acme", default));
        Assert.Equal(8, (await rig.Reader().CurrentAsync(default))!.MinimumGroupSize);
    }

    [Fact]
    public async Task A_failed_run_keeps_the_previous_snapshot_reports_the_type_only_and_degrades_health()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 5));
        await rig.Publisher().RunDueAsync(default);

        rig.Clock.Advance(TimeSpan.FromDays(1));
        rig.Source.FailWith = new InvalidOperationException("secret employer name: acme");

        Assert.Equal(PublishOutcome.Failed, await rig.Publisher().RunDueAsync(default));

        Assert.NotNull(await rig.Reader().GetAsync("acme", default));
        Assert.True(rig.State.LastRunFailed);
        Assert.Contains(rig.Logs.Lines, l => l.Contains("InvalidOperationException"));
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains("secret") || l.Contains("acme"));

        rig.Source.FailWith = null;
        Assert.Equal(PublishOutcome.Published, await rig.Publisher().RunDueAsync(default)); // recovers on the next tick
        Assert.False(rig.State.LastRunFailed);
    }

    [Fact]
    public async Task A_run_that_dies_half_way_publishes_nothing_and_leaves_no_rows_behind()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("acme", 5));
        await rig.Publisher().RunDueAsync(default);
        rig.Clock.Advance(TimeSpan.FromDays(1));

        // The second employer's records are not grouped: the build fails after the first employer was already written.
        var half = new HalfThenFail([.. Employer("bravo", 5), .. Employer("charlie", 5)]);
        var publisher = new SnapshotPublisher(Data.NewDb(rig.Name), half, Microsoft.Extensions.Options.Options.Create(rig.Options), rig.Clock, rig.Metrics, rig.State, rig.Factory.CreateLogger<SnapshotPublisher>());

        Assert.Equal(PublishOutcome.Failed, await publisher.RunDueAsync(default));

        await using var db = Data.NewDb(rig.Name);
        Assert.Equal(["acme"], await db.EmployerSnapshots.Select(r => r.EmployerRef).ToListAsync());
        Assert.Equal(1, await db.Snapshots.CountAsync());
    }

    private sealed class HalfThenFail(List<Observation> items) : IObservationSource
    {
        public async IAsyncEnumerable<Observation> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                await Task.Yield();
                yield return item;
            }
            throw new IOException("connection lost");
        }
    }

    [Fact]
    public async Task Rows_left_by_a_crashed_run_are_swept_by_the_next_publication()
    {
        using var rig = new Rig();
        await using (var db = Data.NewDb(rig.Name))
        {
            db.EmployerSnapshots.Add(new EmployerSnapshotRow { SnapshotId = Guid.NewGuid(), EmployerRef = "orphan", View = "{}" });
            await db.SaveChangesAsync();
        }
        rig.Source.Items.AddRange(Employer("acme", 5));

        await rig.Publisher().RunDueAsync(default);

        await using var check = Data.NewDb(rig.Name);
        Assert.Equal(["acme"], await check.EmployerSnapshots.Select(r => r.EmployerRef).ToListAsync());
    }

    [Fact]
    public async Task An_empty_store_publishes_an_empty_snapshot()
    {
        using var rig = new Rig();

        Assert.Equal(PublishOutcome.Published, await rig.Publisher().RunDueAsync(default));
        var page = await rig.Reader().ListAsync(1, 25, default);

        Assert.NotNull(page.Snapshot);
        Assert.Empty(page.Employers);
    }

    [Fact]
    public async Task The_employer_list_is_alphabetical_paged_and_the_same_every_time()
    {
        using var rig = new Rig();
        foreach (var name in new[] { "zeta", "alpha", "mike", "bravo" })
        {
            rig.Source.Items.AddRange(Employer(name, 5, rating: name == "zeta" ? 5 : 1)); // the best-rated employer is last: order is not by score
        }
        await rig.Publisher().RunDueAsync(default);

        var first = await rig.Reader().ListAsync(1, 3, default);
        var second = await rig.Reader().ListAsync(2, 3, default);

        Assert.Equal(["alpha", "bravo", "mike"], first.Employers);
        Assert.Equal(["zeta"], second.Employers);
        Assert.Equal(4, first.Total);
        Assert.Equal(first.Employers, (await rig.Reader().ListAsync(1, 3, default)).Employers);
    }

    [Fact]
    public async Task More_employers_than_one_write_batch_are_all_published()
    {
        using var rig = new Rig();
        for (var i = 0; i < 230; i++)
        {
            rig.Source.Items.AddRange(Employer($"emp-{i:D3}", 5));
        }

        await rig.Publisher().RunDueAsync(default);

        Assert.Equal(230, (await rig.Reader().ListAsync(1, 100, default)).Total);
        await using var db = Data.NewDb(rig.Name);
        Assert.Equal(230, await db.EmployerSnapshots.CountAsync());
    }

    [Fact]
    public async Task An_unknown_employer_and_one_with_nothing_displayable_are_the_same_answer()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("tiny", 3));
        await rig.Publisher().RunDueAsync(default);

        Assert.Null(await rig.Reader().GetAsync("tiny", default));
        Assert.Null(await rig.Reader().GetAsync("never-heard-of", default));
    }

    // ---- what operations may emit ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Nothing_per_record_or_per_employer_is_logged()
    {
        using var rig = new Rig();
        rig.Source.Items.AddRange(Employer("canary-employer-zzz", 12));
        rig.Source.Items.AddRange(Employer("small-canary-employer-yyy", 2));

        await rig.Publisher().RunDueAsync(default);

        Assert.NotEmpty(rig.Logs.Lines);
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains("canary", StringComparison.OrdinalIgnoreCase));
        Assert.True(rig.Logs.Lines.Count <= 3, "one publication logs a line or two, not a line per record");
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains("14") || l.Contains("suppress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Metrics_carry_the_outcome_label_only()
    {
        using var rig = new Rig();
        var seen = new List<(string Name, string[] TagKeys, string?[] TagValues)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SignalsMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((i, _, tags, _) => seen.Add((i.Name, [.. tags.ToArray().Select(t => t.Key)], [.. tags.ToArray().Select(t => t.Value?.ToString())])));
        listener.SetMeasurementEventCallback<double>((i, _, tags, _) => seen.Add((i.Name, [.. tags.ToArray().Select(t => t.Key)], [.. tags.ToArray().Select(t => t.Value?.ToString())])));
        listener.Start();
        rig.Source.Items.AddRange(Employer("canary-employer-zzz", 12));

        await rig.Publisher().RunDueAsync(default);
        await rig.Publisher().RunDueAsync(default);
        rig.Source.FailWith = new IOException();
        rig.Clock.Advance(TimeSpan.FromDays(1));
        await rig.Publisher().RunDueAsync(default);
        listener.RecordObservableInstruments();

        Assert.NotEmpty(seen);
        Assert.All(seen, m => Assert.True(m.TagKeys.Length == 0 || m.TagKeys.SequenceEqual(["outcome"]), $"{m.Name} has labels {string.Join(',', m.TagKeys)}"));
        var outcomes = seen.SelectMany(m => m.TagValues).OfType<string>().Distinct().Order().ToArray();
        Assert.Equal(["failed", "published", "skipped"], outcomes);
        Assert.DoesNotContain(seen, m => m.Name.Contains("suppress", StringComparison.OrdinalIgnoreCase));
    }

    // ---- health -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Health_is_unhealthy_until_the_schema_exists_then_follows_the_publisher()
    {
        using var rig = new Rig();
        var schema = new SignalsSchemaSignal();
        var check = new SignalsHealthCheck(schema, rig.Reader(), rig.State, Microsoft.Extensions.Options.Options.Create(rig.Options), rig.Clock);
        var context = new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext();

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, (await check.CheckHealthAsync(context)).Status);
        schema.Complete();
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, (await check.CheckHealthAsync(context)).Status); // awaiting the first publication

        rig.Source.Items.AddRange(Employer("acme", 5));
        await rig.Publisher().RunDueAsync(default);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, (await check.CheckHealthAsync(context)).Status);

        rig.Clock.Advance(TimeSpan.FromDays(3)); // no publication for more than two batches
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, (await check.CheckHealthAsync(context)).Status);

        rig.Source.FailWith = new IOException();
        await rig.Publisher().RunDueAsync(default);
        Assert.True(rig.State.LastRunFailed);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, (await check.CheckHealthAsync(context)).Status);
    }

    // ---- configuration ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    public void K_below_the_floor_is_a_configuration_error(int k, bool invalid)
    {
        var options = new SignalsOptions { MinimumGroupSize = k };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        Assert.Equal(!invalid, System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, true));
    }

    [Theory]
    [InlineData("00:30:00", true)]
    [InlineData("01:00:00", false)]
    [InlineData("1.00:00:00", false)]
    [InlineData("01:30:00", true)]
    [InlineData("8.00:00:00", true)]
    public void The_publish_interval_is_whole_hours_from_one_hour_to_seven_days(string interval, bool invalid)
    {
        var options = new SignalsOptions { PublishInterval = TimeSpan.Parse(interval), CheckInterval = TimeSpan.FromMinutes(1) };

        Assert.Equal(invalid, options.Problems().Any());
    }

    [Fact]
    public void Defaults_are_the_briefs_k_and_a_daily_batch()
    {
        var options = new SignalsOptions();

        Assert.Equal(5, options.MinimumGroupSize);
        Assert.Equal(TimeSpan.FromDays(1), options.PublishInterval);
        Assert.Empty(options.Problems());
    }
}
