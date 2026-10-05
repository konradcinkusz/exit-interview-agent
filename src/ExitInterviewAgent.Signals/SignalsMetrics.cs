using System.Diagnostics.Metrics;

namespace ExitInterviewAgent.Signals;

/// <summary>
/// Metrics of the publisher. Labels: <c>outcome</c> only, from a closed set of three. Nothing carries an employer, a topic, a band
/// or a count of suppressed cells: how many cells were withheld is itself a statement about small groups, so it is not emitted.
/// The published employer and cell counts are public information (they are what the API lists).
/// </summary>
public sealed class SignalsMetrics : IDisposable
{
    public const string MeterName = "ExitInterviewAgent.Signals";
    public const string Published = "published";
    public const string Skipped = "skipped";
    public const string Failed = "failed";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _publications;
    private readonly Histogram<double> _duration;
    private long _employers;

    public SignalsMetrics()
    {
        _publications = _meter.CreateCounter<long>("signals.publications", description: "Publication attempts by outcome (published, skipped, failed).");
        _duration = _meter.CreateHistogram<double>("signals.publication.duration", unit: "s", description: "Duration of a publication that ran.");
        _meter.CreateObservableGauge("signals.snapshot.employers", () => Interlocked.Read(ref _employers), description: "Employers in the current snapshot.");
    }

    public void Record(string outcome, TimeSpan? duration = null, int? employers = null)
    {
        _publications.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (duration is { } d)
        {
            _duration.Record(d.TotalSeconds);
        }
        if (employers is { } e)
        {
            Interlocked.Exchange(ref _employers, e);
        }
    }

    public void Dispose() => _meter.Dispose();
}
