using System.ComponentModel.DataAnnotations;

namespace ExitInterviewAgent.Signals;

/// <summary>Configuration section <c>Signals</c>. Every value is validated at startup: a privacy parameter that is silently clamped is not a parameter.</summary>
public sealed class SignalsOptions
{
    public const string SectionName = "Signals";

    /// <summary>The publisher and the endpoints' data. When false the module still serves the last snapshot but publishes nothing, and says so in /health.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>k: the smallest group any published number may describe, per displayed cell. Default 5 (brief section 6); at least 3.</summary>
    [Range(DisclosureRules.FloorForMinimumGroupSize, 1000)]
    public int MinimumGroupSize { get; set; } = DisclosureRules.DefaultMinimumGroupSize;

    /// <summary>Length of one publication batch. Default one day; never shorter than one hour, so a snapshot cannot track single submissions.</summary>
    public TimeSpan PublishInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How often the publisher looks at the clock to see whether a batch is due. Not the publication cadence.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Requests per minute per account on the signals endpoints (anti-scraping). The snapshot changes once per batch, so a faster poll learns nothing.</summary>
    [Range(1, 6000)]
    public int RequestsPerMinute { get; set; } = 30;

    public DemoOptions Demo { get; set; } = new();

    public static readonly TimeSpan MinimumPublishInterval = TimeSpan.FromHours(1);

    public IEnumerable<string> Problems()
    {
        if (PublishInterval < MinimumPublishInterval || PublishInterval > TimeSpan.FromDays(7) || PublishInterval.Ticks % TimeSpan.TicksPerHour != 0)
        {
            yield return "Signals:PublishInterval must be a whole number of hours, from one hour to seven days: publication is batched, never per submission.";
        }
        if (CheckInterval < TimeSpan.FromSeconds(1) || CheckInterval > PublishInterval)
        {
            yield return "Signals:CheckInterval must be between one second and the publish interval.";
        }
    }
}

/// <summary>Demo data (DEMO-DATA-AND-SEEDING). Development only; see the demo seeder in the host.</summary>
public sealed class DemoOptions
{
    /// <summary><c>Off</c> (default), <c>Seed</c> (reset the demo namespace, then seed it) or <c>Remove</c> (reset it and stop).</summary>
    public string Mode { get; set; } = "Off";

    /// <summary>Seed of the generator. Two runs with the same seed produce the same dataset.</summary>
    public int Seed { get; set; } = 42;
}
