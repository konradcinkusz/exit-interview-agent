using System.Diagnostics;
using ExitInterviewAgent.Agent.Tracing;

namespace ExitInterviewAgent.Eval.Execution;

public sealed record CapturedEvent(string Name, long AtTicks, IReadOnlyDictionary<string, object?> Tags);

/// <summary>One finished span. Tag values are ints, bools, controlled codes or arrays of codes: that is the agent's own rule (TRACE-SCHEMA).</summary>
public sealed record CapturedSpan(
    int Id, int? ParentId, string Name, long StartTicks, long DurationTicks, string Status, string? StatusDescription,
    IReadOnlyDictionary<string, object?> Tags, IReadOnlyList<CapturedEvent> Events)
{
    public double DurationMs => DurationTicks / (double)TimeSpan.TicksPerMillisecond;

    public string? Str(string key) => Tags.TryGetValue(key, out var v) ? v as string : null;

    public int? Int(string key) => Tags.TryGetValue(key, out var v) && v is int i ? i : null;

    public bool? Bool(string key) => Tags.TryGetValue(key, out var v) && v is bool b ? b : null;

    public bool HasEvent(string name) => Events.Any(e => e.Name == name);
}

/// <summary>One scenario run's trace: every span the agent emitted, in start order. Parent links are the ordinary ones.</summary>
public sealed record CapturedTrace(IReadOnlyList<CapturedSpan> Spans)
{
    public IEnumerable<CapturedSpan> Named(string name) => Spans.Where(s => s.Name == name);

    public CapturedSpan? Session => Spans.FirstOrDefault(s => s.Name == InterviewTelemetry.Spans.Session);

    public IEnumerable<CapturedSpan> Turns => Named(InterviewTelemetry.Spans.Turn);

    public IEnumerable<CapturedSpan> ChatSpans => Spans.Where(s => s.Name.StartsWith(InterviewTelemetry.Spans.Model, StringComparison.Ordinal));

    public IEnumerable<CapturedEvent> AllEvents => Spans.SelectMany(s => s.Events);

    /// <summary>Every string a trace could carry text through: names, tag keys and values (arrays flattened), event names and tags, status descriptions.</summary>
    public IEnumerable<string> AllStrings()
    {
        foreach (var s in Spans)
        {
            yield return s.Name;
            if (s.StatusDescription is { } d) yield return d;
            foreach (var (k, v) in s.Tags) { yield return k; foreach (var x in Flatten(v)) yield return x; }
            foreach (var e in s.Events)
            {
                yield return e.Name;
                foreach (var (k, v) in e.Tags) { yield return k; foreach (var x in Flatten(v)) yield return x; }
            }
        }
    }

    internal static IEnumerable<string> Flatten(object? value) => value switch
    {
        null => [],
        string s => [s],
        System.Collections.IEnumerable e => e.Cast<object?>().SelectMany(Flatten),
        _ => [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty],
    };
}

/// <summary>
/// An in-process <see cref="ActivityListener"/> for ONE run (no collector, no exporter). The listener is process-wide and tests run in
/// parallel, so the recorder opens a root activity of its own and keeps only activities that share its trace id.
/// </summary>
public sealed class TraceRecorder : IDisposable
{
    private const string RootSourceName = "ExitInterviewAgent.Eval.Recorder";
    private static readonly ActivitySource RootSource = new(RootSourceName);
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = [];
    private readonly Activity _root;

    public TraceRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == InterviewTelemetry.ActivitySourceName || s.Name == RootSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (_root is not null && a.TraceId == _root.TraceId && a != _root) lock (_stopped) _stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(_listener);
        _root = RootSource.StartActivity("eval-root", ActivityKind.Internal)!;
    }

    public CapturedTrace Snapshot()
    {
        Activity[] all;
        lock (_stopped) all = _stopped.OrderBy(a => a.StartTimeUtc).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray();
        var ids = all.Select((a, i) => (a.SpanId, Index: i + 1)).ToDictionary(x => x.SpanId, x => x.Index);
        var origin = _root.StartTimeUtc.Ticks;
        var spans = all.Select((a, i) => new CapturedSpan(
            i + 1,
            ids.TryGetValue(a.ParentSpanId, out var p) ? p : null,
            a.OperationName.StartsWith(InterviewTelemetry.Spans.Model, StringComparison.Ordinal) ? a.DisplayName : a.OperationName,
            a.StartTimeUtc.Ticks - origin,
            a.Duration.Ticks,
            a.Status.ToString(),
            a.StatusDescription,
            a.TagObjects.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
            a.Events.Select(e => new CapturedEvent(e.Name, e.Timestamp.UtcTicks - origin, e.Tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal))).ToArray())).ToList();
        return new CapturedTrace(spans);
    }

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }
}
