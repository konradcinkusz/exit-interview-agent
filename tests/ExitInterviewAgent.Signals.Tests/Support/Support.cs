using System.Runtime.CompilerServices;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Signals.Tests.Support;

/// <summary>A clock a test moves by hand.</summary>
public sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
    public void Set(DateTimeOffset at) => _now = at;
}

/// <summary>An in-memory observation feed a test mutates between publications (add a record, delete a record).</summary>
public sealed class ListSource : IObservationSource
{
    public List<Observation> Items { get; } = [];
    public Exception? FailWith { get; set; }
    public int Reads { get; private set; }

    public async IAsyncEnumerable<Observation> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Reads++;
        if (FailWith is not null)
        {
            throw FailWith;
        }
        foreach (var item in Items.OrderBy(i => i.EmployerRef, StringComparer.Ordinal).ToList())
        {
            await Task.Yield();
            yield return item;
        }
    }
}

public sealed class CaptureLogger : ILoggerProvider
{
    public List<string> Lines { get; } = [];
    public ILogger CreateLogger(string categoryName) => new Sink(Lines);
    public void Dispose()
    {
    }

    private sealed class Sink(List<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (lines)
            {
                lines.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
            }
        }
    }
}

internal static class Data
{
    public static readonly string[] Tenures = [.. Vocabulary.Bands(Dimension.Tenure)];
    public static readonly string[] Seniorities = [.. Vocabulary.Bands(Dimension.Seniority)];
    public static readonly string[] Functions = [.. Vocabulary.Bands(Dimension.Function)];

    /// <summary>One record rating every topic with the same number, in the given bands.</summary>
    public static Observation Rated(
        string employer, int rating, string tenure = "1y_3y", string? seniority = "mid", string? function = "engineering",
        VerificationState verification = VerificationState.Unchecked, params string[] topics)
        => new(employer, tenure, seniority, function, verification,
            [.. (topics.Length == 0 ? Vocabulary.Topics : topics).Select(t => new TopicRating(t, rating))]);

    public static IEnumerable<Observation> Many(int count, Func<int, Observation> make) => Enumerable.Range(0, count).Select(make);

    public static SignalsDbContext NewDb(string? name = null)
        => new(new DbContextOptionsBuilder<SignalsDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N")).Options);

    public static async Task<List<EmployerView>> BuildAsync(IEnumerable<Observation> observations, int k = 5, IDisclosurePolicy? policy = null)
    {
        var builder = new SnapshotBuilder(new DisclosureRules(k), policy ?? StandardDisclosurePolicy.Instance);
        var views = new List<EmployerView>();
        await foreach (var view in builder.BuildAsync(Feed(observations.OrderBy(o => o.EmployerRef, StringComparer.Ordinal)), new BuildReport()))
        {
            views.Add(view);
        }
        return views;
    }

    public static async IAsyncEnumerable<Observation> Feed(IEnumerable<Observation> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
