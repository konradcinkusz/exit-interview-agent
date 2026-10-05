using System.Reflection;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Personas;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Tracing;

public class TraceTests
{
    private const string Canary = "zebracanary7391qx";

    public static IEnumerable<object[]> Personas => PersonaCatalog.All.Select(p => new object[] { p.Id });

    private static IReadOnlyList<string> Needles(PersonaDefinition p) => [Canary, .. p.Planted, "Brunhilda", "Fogwhistle"];

    [Theory]
    [MemberData(nameof(Personas))]
    public async Task A_canary_in_the_interviewee_text_appears_in_no_span_event_attribute_or_log_line(string id)
    {
        var persona = PersonaCatalog.Get(id);
        using var capture = new TraceCapture();
        var logger = new CapturingLogger();

        var result = await PersonaSession.RunAsync(persona, 1, decorate: r => r + " " + Canary, logger: logger);

        // The test has power: the canary really went through the interview (it is in the stored transcript) ...
        if (result.Transcript is not null) Assert.Contains(Canary, result.Transcript.Render());
        // ... and a trace was really captured.
        Assert.NotEmpty(capture.Activities);
        Assert.Contains(capture.Activities, a => a.OperationName == InterviewTelemetry.Spans.Session);
        Assert.NotEmpty(logger.Lines);

        var traced = capture.AllStrings().ToList();
        foreach (var needle in Needles(persona))
        {
            Assert.DoesNotContain(traced, s => s.Contains(needle, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logger.Lines, l => l.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task The_canary_scan_can_fail_a_span_that_leaks_is_caught()
    {
        using var capture = new TraceCapture();
        using (var span = InterviewTelemetry.Source.StartActivity("interview.turn"))
        {
            span!.SetTag("interview.leak", "the interviewee said " + Canary);
            span.AddEvent(new System.Diagnostics.ActivityEvent("said " + Canary));
        }

        await Task.CompletedTask;
        Assert.Contains(capture.AllStrings(), s => s.Contains(Canary, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failures_inside_a_run_leak_the_canary_into_no_span_log_line_or_exception_message()
    {
        using var capture = new TraceCapture();
        var logger = new CapturingLogger();
        var options = Options with { Logger = logger };
        var failing = new FakeChatClient((role, _) => throw new InvalidOperationException("provider echoed " + Canary));
        var runner = InterviewRunner.Create(failing, options);
        string[] replies = ["Yes " + Canary, .. Enumerable.Range(1, 6).Select(i => $"It took {i + 2} weeks and nobody said why {Canary}.")];

        var result = await runner.RunAsync(new ScriptedInterviewee(replies));

        Assert.Equal(InterviewOutcome.ExtractionFailed, result.Outcome);
        Assert.DoesNotContain(capture.AllStrings(), s => s.Contains(Canary, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Lines, l => l.Contains(Canary, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capture.Activities, a => a.OperationName.StartsWith("chat", StringComparison.Ordinal) && a.StatusDescription == "model_call_failed");
    }

    [Fact]
    public async Task Malformed_extractor_output_that_contains_the_canary_leaks_into_no_error_code()
    {
        using var capture = new TraceCapture();
        var runner = InterviewRunner.Create(new FakeChatClient((role, user) => role == Role.Extractor ? "{ \"topics\": \"" + Canary : user.Split('\n').First(l => l.StartsWith("SEED: ", StringComparison.Ordinal))[6..]), Options);

        await runner.RunAsync(new ScriptedInterviewee(["Yes.", .. Enumerable.Range(1, 6).Select(i => $"It took {i + 2} weeks and nobody said why {Canary}.")]));

        Assert.DoesNotContain(capture.AllStrings(), s => s.Contains(Canary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Every_string_in_a_trace_is_a_name_an_enum_value_or_a_short_controlled_code_never_free_text()
    {
        using var capture = new TraceCapture();
        foreach (var persona in PersonaCatalog.All) await PersonaSession.RunAsync(persona, 1, decorate: r => r + " " + Canary);

        var controlled = new Regex(@"^[A-Za-z0-9_.:\- ]{1,48}$");
        foreach (var a in capture.Activities)
        {
            foreach (var t in a.TagObjects)
            {
                Assert.True(t.Value is int or long or bool or string or string[], $"{t.Key} has type {t.Value?.GetType().Name}");
                foreach (var s in t.Value switch { string x => [x], string[] xs => xs, _ => [] })
                    Assert.True(controlled.IsMatch(s), $"{t.Key} carries a value that is not a controlled code");
            }
            foreach (var e in a.Events)
                foreach (var t in e.Tags) Assert.True(t.Value is int or bool or string, $"{e.Name}/{t.Key}");
        }
    }

    [Fact]
    public async Task All_documented_spans_are_emitted_and_only_documented_attributes_are_used()
    {
        using var capture = new TraceCapture();
        await PersonaSession.RunAsync(PersonaCatalog.Get("vague"), 1);
        await PersonaSession.RunAsync(PersonaCatalog.Get("names-manager"), 1);
        await PersonaSession.RunAsync(PersonaCatalog.Get("prompt-injection"), 1);
        await PersonaSession.RunAsync(PersonaCatalog.Get("withdraws-consent"), 1);

        var spanNames = capture.Activities.Select(a => a.OperationName.StartsWith("chat ", StringComparison.Ordinal) ? "chat" : a.OperationName).ToHashSet();
        var documentedSpans = Constants(typeof(InterviewTelemetry.Spans));
        Assert.Subset(documentedSpans.ToHashSet(), spanNames);
        Assert.Superset(spanNames, documentedSpans.ToHashSet());

        var documentedAttrs = Constants(typeof(InterviewTelemetry.Attr)).ToHashSet();
        var used = capture.Activities.SelectMany(a => a.TagObjects.Select(t => t.Key)).ToHashSet();
        Assert.Subset(documentedAttrs, used);

        var documentedEvents = Constants(typeof(InterviewTelemetry.Events)).ToHashSet();
        var usedEvents = capture.Activities.SelectMany(a => a.Events.Select(e => e.Name)).ToHashSet();
        Assert.Subset(documentedEvents, usedEvents);
        Assert.Contains(InterviewTelemetry.Events.DisclosureDelivered, usedEvents);
        Assert.Contains(InterviewTelemetry.Events.InjectionSuspected, usedEvents);
        Assert.Contains(InterviewTelemetry.Events.NamesMasked, usedEvents);
        Assert.Contains(InterviewTelemetry.Events.TranscriptDiscarded, usedEvents);
    }

    [Fact]
    public async Task The_trace_schema_document_lists_every_span_event_and_attribute_name()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "eval", "TRACE-SCHEMA.md"));

        foreach (var name in Constants(typeof(InterviewTelemetry.Spans)).Concat(Constants(typeof(InterviewTelemetry.Events))).Concat(Constants(typeof(InterviewTelemetry.Attr))))
            Assert.True(doc.Contains($"`{name}`", StringComparison.Ordinal), $"docs/eval/TRACE-SCHEMA.md does not document `{name}`");
        Assert.Contains(InterviewTelemetry.ActivitySourceName, doc);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_session_span_summarises_the_run_with_counts_and_decisions_only()
    {
        using var capture = new TraceCapture();

        var result = await PersonaSession.RunAsync(PersonaCatalog.Get("talkative"), 1);

        var session = capture.Activities.Single(a => a.OperationName == InterviewTelemetry.Spans.Session);
        var tags = session.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("completed", tags[InterviewTelemetry.Attr.Outcome]);
        Assert.Equal("all_topics_covered", tags[InterviewTelemetry.Attr.EndReason]);
        Assert.Equal(true, tags[InterviewTelemetry.Attr.Submittable]);
        Assert.Equal(true, tags[InterviewTelemetry.Attr.AiDisclosed]);
        Assert.Equal(result.Diagnostics.ModelCalls, tags[InterviewTelemetry.Attr.ModelCalls]);
        Assert.Equal("invoke_agent", tags[InterviewTelemetry.Attr.OperationName]);
        Assert.Equal(7, capture.Activities.Count(a => a.OperationName == InterviewTelemetry.Spans.PiiGuard));
        Assert.All(capture.Activities.Where(a => a.OperationName.StartsWith("chat ", StringComparison.Ordinal)), a =>
        {
            Assert.Equal("chat", a.GetTagItem(InterviewTelemetry.Attr.OperationName));
            Assert.NotNull(a.GetTagItem(InterviewTelemetry.Attr.UsageInputTokens));
            Assert.NotNull(a.GetTagItem(InterviewTelemetry.Attr.UsageOutputTokens));
            Assert.Equal(ScriptedChatClient.ModelId, a.GetTagItem(InterviewTelemetry.Attr.RequestModel));
        });
    }

    [Fact]
    public void Tag_helpers_have_no_string_overload_so_text_cannot_be_attached()
    {
        var overloads = typeof(SpanTags).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Set").ToList();

        Assert.NotEmpty(overloads);
        Assert.DoesNotContain(overloads, m => m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == typeof(string));
        Assert.Equal("invalid_code", SpanTags.SafeCode("has spaces and text"));
        Assert.Equal("invalid_code", SpanTags.SafeCode(new string('a', 49)));
        Assert.Equal("ok_code-1.2:3", SpanTags.SafeCode("ok_code-1.2:3"));
    }

    private static IEnumerable<string> Constants(Type t) =>
        t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExitInterviewAgent.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
