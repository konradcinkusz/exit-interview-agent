using System.Diagnostics;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Personas;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Eval.Execution;

/// <summary>Everything one run produced, kept together for the graders: the artifact triple, the logs, the raw replies, and what was planted.</summary>
public sealed record RunRecord(
    Scenario Scenario, int Seed, string Profile, PersonaDefinition Persona, InterviewResult Result, CapturedTrace Trace,
    IReadOnlyList<string> LogLines, IReadOnlyList<string> RawReplies, IReadOnlyList<string> Needles, double WallMs, RunRecord? Control = null)
{
    public string Key => $"{Scenario.Id}#{Seed}";
}

/// <summary>A run that did not produce an artifact because the harness itself failed. Never counted as a pass and never as a caught failure.</summary>
public sealed record RunError(string ScenarioId, int Seed, string Profile, string ExceptionType);

public sealed record RunOutcome(RunRecord? Run, RunError? Error);

/// <summary>
/// The in-process scenario runner. One run = persona + seed + profile + the scenario's faults and canary, a trace captured by an
/// <see cref="ActivityListener"/>, logs captured through <c>ILogger</c>. Deterministic: with the mock profile the same (scenario, seed) gives the
/// same artifact byte for byte. Uses <see cref="PersonaSession"/>; it does not re-implement the interview.
/// </summary>
public static partial class ScenarioRunner
{
    public const string Canary = "zebracanary7391qx";

    public static async Task<RunOutcome> RunAsync(Scenario scenario, int seed, ModelProfile profile, CancellationToken ct = default)
    {
        if (profile.Factory is null) throw new InvalidOperationException($"Profile '{profile.Name}' is not runnable: {profile.SkipReason}.");
        try
        {
            var run = await RunCoreAsync(scenario, seed, profile, strip: false, ct).ConfigureAwait(false);
            RunRecord? control = null;
            if (scenario.Control == "strip-injection")
                control = await RunCoreAsync(scenario, seed, profile, strip: true, ct).ConfigureAwait(false);
            return new RunOutcome(run with { Control = control }, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new RunOutcome(null, new RunError(scenario.Id, seed, profile.Name, ex.GetType().Name));
        }
    }

    /// <summary>
    /// Runs a scenario against a runner the caller built, for the deliberately broken agent variants (docs/eval/SPEC.md §9): the same persona, seed, clock,
    /// trace capture and logging as a normal run, with a different (weakened) agent. Used by the mutation tests; a variant must be caught by an assertion.
    /// </summary>
    public static async Task<RunRecord> RunVariantAsync(Scenario scenario, int seed, Func<InterviewOptions, InterviewRunner> buildRunner, CancellationToken ct = default)
    {
        var persona = PersonaCatalog.Get(scenario.Persona);
        var raw = new List<string>();
        var logger = new CapturingLogger();
        var clock = new Agent.Mock.SimulatedClock();
        var options = new InterviewOptions(persona.Employer.Ref, persona.Context.ToRecordContext())
        {
            // The persona's language selects the protocol: a Polish persona is interviewed with the Polish wording (Y2).
            Protocol = InterviewProtocol.For(persona.Language),
            EmployerNames = persona.Employer.Names.ToArray(),
            IdFactory = () => PersonaSession.DemoInterviewId(persona.Id, seed),
            Clock = clock,
            Logger = logger,
        };
        Func<string, string> decorate = reply => { raw.Add(reply); return scenario.Canary ? reply + " " + Canary : reply; };
        using var recorder = new TraceRecorder();
        var watch = Stopwatch.StartNew();
        var result = await buildRunner(options).RunAsync(new PersonaInterviewee(persona, seed, clock, decorate), ct).ConfigureAwait(false);
        watch.Stop();
        var needles = scenario.Canary ? [Canary, .. persona.Planted] : persona.Planted.ToArray();
        return new RunRecord(scenario, seed, "variant", persona, result, recorder.Snapshot(), logger.Lines.ToArray(), raw, needles, watch.Elapsed.TotalMilliseconds);
    }

    private static async Task<RunRecord> RunCoreAsync(Scenario scenario, int seed, ModelProfile profile, bool strip, CancellationToken ct)
    {
        var persona = PersonaCatalog.Get(scenario.Persona);
        var raw = new List<string>();
        var logger = new CapturingLogger();
        var probeReplies = scenario.ProbeReply is null ? [] : (persona.Responses.Probes ?? new Dictionary<string, IReadOnlyList<string>>()).Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);
        Func<string, string> decorate = reply =>
        {
            if (scenario.ProbeReply is not null && probeReplies.Contains(reply)) reply = scenario.ProbeReply;
            raw.Add(reply);
            var text = strip ? StripInjection(reply) : reply;
            return scenario.Canary ? text + " " + Canary : text;
        };

        using var client = profile.Factory!();
        IChatClient model = client;
        if (scenario.Faults is { Count: > 0 }) model = new FaultInjectingChatClient(model, scenario.Faults);

        using var recorder = new TraceRecorder();
        var watch = Stopwatch.StartNew();
        var result = await PersonaSession.RunAsync(persona, seed, model, decorate, logger, ct).ConfigureAwait(false);
        watch.Stop();
        var trace = recorder.Snapshot();

        var needles = scenario.Canary ? [Canary, .. persona.Planted] : persona.Planted.ToArray();
        return new RunRecord(scenario, seed, profile.Name, persona, result, trace, logger.Lines.ToArray(), raw, needles, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Removes the sentences a reader would call instructions to a model, for the control run of constraint C-05.</summary>
    public static string StripInjection(string reply) =>
        string.Join(' ', SentenceBreak().Split(reply).Where(s => !ReplyAnalyzer.LooksLikeInjection(s))).Trim();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex SentenceBreak();
}

/// <summary>An <c>ILogger</c> that keeps every formatted line (and exception text), so the canary scan covers what the agent logged.</summary>
public sealed class CapturingLogger : ILogger
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
