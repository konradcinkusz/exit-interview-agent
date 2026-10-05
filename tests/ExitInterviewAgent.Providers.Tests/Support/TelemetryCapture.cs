using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Providers.Tests.Support;

public sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string> Tags);

/// <summary>
/// Everything a collector could receive from one test: every activity from every source (the SDKs' and the HTTP stack's included),
/// every measurement from the providers' meter. Activities are tied to a per-test root so parallel tests do not mix.
/// </summary>
public sealed class TelemetryCapture : IDisposable
{
    private const string RootName = "ExitInterviewAgent.Providers.Tests.Capture";
    private static readonly ActivitySource RootSource = new(RootName);
    private readonly ActivityListener _listener;
    private readonly MeterListener _meters = new();
    private readonly List<Activity> _activities = [];
    private readonly List<Measurement> _measurements = [];
    private readonly Activity _root;

    public TelemetryCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (a.TraceId == _root!.TraceId && a != _root) lock (_activities) _activities.Add(a); },
        };
        ActivitySource.AddActivityListener(_listener);
        _root = RootSource.StartActivity("test-root", ActivityKind.Internal)!;

        _meters.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == ProviderTelemetry.MeterName) listener.EnableMeasurementEvents(instrument); };
        _meters.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
        _meters.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
        _meters.Start();
    }

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var m = new Measurement(instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value?.ToString() ?? string.Empty));
        lock (_measurements) _measurements.Add(m);
    }

    public IReadOnlyList<Activity> Activities { get { lock (_activities) return _activities.ToList(); } }

    public IReadOnlyList<Measurement> Measurements { get { lock (_measurements) return _measurements.ToList(); } }

    private static IEnumerable<string> Flatten(object? value) => value switch
    {
        null => [],
        string s => [s],
        System.Collections.IEnumerable e => e.Cast<object?>().SelectMany(Flatten),
        _ => [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty],
    };

    /// <summary>Every string a trace or metric could leak text through. <paramref name="ownSourcesOnly"/> limits it to the project's own sources (the HTTP stack's own spans carry the request path).</summary>
    public IEnumerable<string> AllStrings(bool ownSourcesOnly = false)
    {
        foreach (var a in Activities)
        {
            if (ownSourcesOnly && !a.Source.Name.StartsWith("ExitInterviewAgent.", StringComparison.Ordinal)) continue;
            yield return a.OperationName;
            yield return a.DisplayName;
            yield return a.Source.Name;
            if (a.StatusDescription is { } d) yield return d;
            foreach (var t in a.TagObjects) { yield return t.Key; foreach (var s in Flatten(t.Value)) yield return s; }
            foreach (var b in a.Baggage) { yield return b.Key; yield return b.Value ?? string.Empty; }
            foreach (var e in a.Events)
            {
                yield return e.Name;
                foreach (var t in e.Tags) { yield return t.Key; foreach (var s in Flatten(t.Value)) yield return s; }
            }
        }
        foreach (var m in Measurements)
        {
            yield return m.Instrument;
            foreach (var (k, v) in m.Tags) { yield return k; yield return v; }
        }
    }

    public void Dispose()
    {
        _meters.Dispose();
        _listener.Dispose();
        _root.Dispose();
    }
}

public sealed class ListLogger : ILogger
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToList(); } }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var line = formatter(state, exception);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs) line += " " + string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"));
        if (exception is not null) line += " " + exception;
        lock (_lines) _lines.Add(line);
    }
}
