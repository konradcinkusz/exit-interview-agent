using System.Diagnostics;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Agent.Tests.Support;

internal static class Helpers
{
    public static readonly InterviewProtocol Proto = InterviewProtocol.Current;
    public static readonly ProtocolLimits Limits = Proto.Limits;

    /// <summary>Signals for a plain, substantive answer; override what the case is about.</summary>
    public static ReplySignals Signals(
        int words = 30, bool withdrawal = false, ConsentAnswer consent = ConsentAnswer.Yes, bool terse = false, bool vague = false,
        bool hostile = false, bool contradiction = false, bool names = false, bool injection = false, int polarity = 0) =>
        new(words, withdrawal, consent, terse, vague, hostile, contradiction, names, injection, polarity);

    public static InterviewMachine Started(out Step first)
    {
        var m = new InterviewMachine(Proto);
        first = m.Start();
        return m;
    }

    /// <summary>A started machine that has been given consent and is waiting for the first topic's answer.</summary>
    public static InterviewMachine AtFirstTopic()
    {
        var m = Started(out _);
        m.OnReply(Signals());
        return m;
    }

    public static readonly InterviewOptions Options = new("widgetron-ltd", new RecordContext(TenureBand.OneToThreeYears, SeniorityBand.Mid, FunctionBand.Engineering))
    {
        EmployerNames = ["Widgetron"],
        IdFactory = () => InterviewId.Parse(new string('a', 32)),
    };

    public static string ValidExtraction(Func<Topic, string?>? quoteFor = null, int rating = 3)
    {
        var topics = Enum.GetValues<Topic>().ToDictionary(Wire.Name, t => quoteFor?.Invoke(t) is { } q
            ? (object)new { status = "covered", rating, confidence = "medium", quotes = new[] { q } }
            : new { status = "no_data" });
        return System.Text.Json.JsonSerializer.Serialize(new { topics });
    }
}

/// <summary>An interviewee that replays a fixed list of raw replies, then leaves.</summary>
internal sealed class ScriptedInterviewee(params string?[] replies) : IInterviewee
{
    private int _next;
    public List<IntervieweeTurn> Seen { get; } = [];

    public Task<string?> ReplyAsync(IntervieweeTurn turn, CancellationToken ct)
    {
        Seen.Add(turn);
        return Task.FromResult(_next < replies.Length ? replies[_next++] : null);
    }

    public Task DeliverAsync(IntervieweeTurn turn, CancellationToken ct)
    {
        Seen.Add(turn);
        return Task.CompletedTask;
    }
}

/// <summary>A model double that returns whatever the delegate says for each role, and records every prompt it was sent.</summary>
internal sealed class FakeChatClient(Func<Role, string, string> respond) : IChatClient
{
    public List<(Role Role, string System, string User)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var system = list.First(m => m.Role == ChatRole.System).Text;
        var user = list.Last(m => m.Role == ChatRole.User).Text;
        var role = Enum.Parse<Role>(options!.AdditionalProperties![MeteredChatClient.RoleKey]!.ToString()!);
        Calls.Add((role, system, user));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(role, user))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// Collects the activities of ONE test, in-process, with no exporter. The listener is process-wide and xUnit runs test
/// classes in parallel, so the capture opens a root activity of its own and keeps only the activities that share its
/// trace id: spans started by other tests are ignored.
/// </summary>
internal sealed class TraceCapture : IDisposable
{
    private const string RootName = "ExitInterviewAgent.Tests.TraceCapture";
    private static readonly ActivitySource RootSource = new(RootName);
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];
    private readonly Activity _root;

    public TraceCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == InterviewTelemetry.ActivitySourceName || s.Name == RootName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // The listener is registered process-wide before _root exists, so another test's activity can stop first: null-safe.
            ActivityStopped = a => { if (_root is { } root && a.TraceId == root.TraceId && a != root) lock (_activities) _activities.Add(a); },
        };
        ActivitySource.AddActivityListener(_listener);
        _root = RootSource.StartActivity("test-root", ActivityKind.Internal)!;
    }

    public IReadOnlyList<Activity> Activities { get { lock (_activities) return _activities.ToList(); } }

    /// <summary>Every string a trace could leak text through: names, tag keys and values (arrays flattened), event names and tags, status.</summary>
    public IEnumerable<string> AllStrings()
    {
        foreach (var a in Activities)
        {
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
    }

    private static IEnumerable<string> Flatten(object? value) => value switch
    {
        null => [],
        string s => [s],
        System.Collections.IEnumerable e => e.Cast<object?>().SelectMany(Flatten),
        _ => [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty],
    };

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }
}

internal sealed class CapturingLogger : ILogger
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, exception));
        if (exception is not null) Lines.Add(exception.ToString());
    }
}

/// <summary>A PII guard whose detector always fails.</summary>
internal sealed class BrokenPiiGuard : IPiiGuard
{
    public PiiGuardResult Mask(string text) => new(false, string.Empty, []);

    public bool HasFindings(string maskedText) => true;
}
