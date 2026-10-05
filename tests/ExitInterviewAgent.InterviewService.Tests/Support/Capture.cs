using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Text;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>Collects what the process emits: logs, activities, event-source payloads, metrics.</summary>
internal sealed class Capture : IDisposable
{
    private readonly List<string> _items = [];
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters = new();
    private readonly EventListener _events;

    public Capture()
    {
        Logs = new CaptureLoggerProvider();
        _activities = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => Add(Describe(a)),
        };
        ActivitySource.AddActivityListener(_activities);

        _meters.InstrumentPublished = (instrument, listener) => listener.EnableMeasurementEvents(instrument);
        _meters.SetMeasurementEventCallback<long>((i, m, tags, _) => Add($"{i.Meter.Name}/{i.Name}={m} {Join(tags)}"));
        _meters.SetMeasurementEventCallback<int>((i, m, tags, _) => Add($"{i.Meter.Name}/{i.Name}={m} {Join(tags)}"));
        _meters.SetMeasurementEventCallback<double>((i, m, tags, _) => Add($"{i.Meter.Name}/{i.Name}={m} {Join(tags)}"));
        _meters.Start();

        _events = new PayloadListener(this);
    }

    public CaptureLoggerProvider Logs { get; }

    public string Everything
    {
        get
        {
            lock (_items)
            {
                return string.Join('\n', Logs.Lines.Concat(_items));
            }
        }
    }

    private void Add(string item)
    {
        lock (_items)
        {
            _items.Add(item);
        }
    }

    private static string Join(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var sb = new StringBuilder();
        foreach (var t in tags)
        {
            sb.Append(t.Key).Append('=').Append(t.Value).Append(';');
        }
        return sb.ToString();
    }

    private static string Describe(Activity a) => string.Join('|',
        [a.Source.Name, a.OperationName, a.DisplayName, a.StatusDescription,
         string.Join(';', a.TagObjects.Select(t => $"{t.Key}={t.Value}")),
         string.Join(';', a.Baggage.Select(t => $"{t.Key}={t.Value}")),
         string.Join(';', a.Events.Select(e => e.Name + ":" + string.Join(',', e.Tags.Select(t => $"{t.Key}={t.Value}"))))]);

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
        _events.Dispose();
    }

    /// <summary>
    /// Listens to the event sources that belong to the application and the libraries it uses. The runtime's own sources
    /// (array pool, thread pool, GC) are left out: they carry no application data and writing to them from here recurses.
    /// </summary>
    private sealed class PayloadListener(Capture owner) : EventListener
    {
        private static readonly string[] Prefixes =
            ["ModelContextProtocol", "Microsoft-AspNetCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Microsoft.EntityFrameworkCore", "System.Net", "Npgsql", "OpenTelemetry", "ExitInterviewAgent"];

        [ThreadStatic]
        private static bool _inside;

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (Prefixes.Any(p => source.Name.StartsWith(p, StringComparison.Ordinal)))
            {
                EnableEvents(source, EventLevel.Verbose);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (_inside)
            {
                return;
            }
            _inside = true;
            try
            {
                owner.Add(e.EventSource.Name + "/" + e.EventName + ":" + string.Join(',', e.Payload ?? []));
            }
            finally
            {
                _inside = false;
            }
        }
    }
}
