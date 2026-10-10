using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;
using Role = ExitInterviewAgent.Agent.Roles.Role;

namespace ExitInterviewAgent.Agent.Tests.Runner;

/// <summary>The deepening phase end to end through the runner (Y2): kinds, wording seams, the reminder, withdrawal, names and the regression path.</summary>
public class DeepeningRunnerTests
{
    private const string Serious = "I was bullied by my manager every single week for a year.";
    // Touches no element of the deepening menu, so the menu order is what the test sees.
    private const string Deep = "The meetings went on for a long time and nothing was decided.";
    private const string NameReply = "My manager, Brunhilda Fogwhistle, was the one who did it every week for a year.";
    private static readonly string[] SixAnswers = Enumerable.Range(1, 6).Select(i => $"On topic {i} there was one concrete thing: the process took {i + 2} weeks and nobody explained why.").ToArray();

    private static InterviewRunner Runner(out FakeChatClient model) =>
        InterviewRunner.Create(model = new FakeChatClient(Cooperative), Options);

    /// <summary>Extracts a valid record with the scripted mock; the interviewer and prober words are their SEED, as in the runner tests.</summary>
    private static string Cooperative(Role role, string user) => role == Role.Extractor
        ? new ScriptedChatClient().GetResponseAsync([new ChatMessage(ChatRole.System, Prompts.ExtractorSystem(Proto)), new ChatMessage(ChatRole.User, user)]).Result.Text
        : user.Split('\n').First(l => l.StartsWith("SEED: ", StringComparison.Ordinal))[6..];

    [Fact]
    public async Task A_serious_account_is_deepened_four_times_and_the_reminder_is_said_once()
    {
        var runner = Runner(out var model);
        var interviewee = new ScriptedInterviewee(["Yes, I agree.", Serious, Deep, Deep, Deep, Deep, .. SixAnswers[1..]]);

        var result = await runner.RunAsync(interviewee);

        var deep = interviewee.Seen.Where(t => t.Kind == TurnKind.DeepProbe).ToList();
        Assert.Equal(InterviewOutcome.Completed, result.Outcome);
        Assert.Equal(4, deep.Count);
        Assert.Equal($"{Proto.DeepeningReminder} {Proto.DeepeningSeeds[0]}", deep[0].Text);
        Assert.All(deep.Skip(1), t => Assert.Equal(Proto.DeepeningSeeds[deep.IndexOf(t)], t.Text));
        Assert.DoesNotContain(interviewee.Seen, t => t.Kind == TurnKind.Probe);
        Assert.Equal(4, model.Calls.Count(c => c.Role == Role.Prober));
        Assert.Contains("FOCUS: what_happened", model.Calls.First(c => c.Role == Role.Prober).User);
    }

    [Fact]
    public async Task Withdrawal_during_deepening_leaves_no_record_and_asks_nothing_more()
    {
        var runner = Runner(out var model);
        var interviewee = new ScriptedInterviewee(["Yes, I agree.", Serious, "I want to stop now, please."]);

        var result = await runner.RunAsync(interviewee);

        Assert.Equal(InterviewOutcome.Withdrawn, result.Outcome);
        Assert.Null(result.Record);
        Assert.Null(result.RecordJson);
        Assert.Null(result.Transcript);
        Assert.DoesNotContain(model.Calls, c => c.Role == Role.Extractor);
        Assert.Equal(1, interviewee.Seen.Count(t => t.Kind == TurnKind.DeepProbe));
    }

    [Fact]
    public async Task A_name_during_deepening_is_redirected_and_the_deepening_resumes_on_the_next_menu_element()
    {
        var runner = Runner(out var model);
        var interviewee = new ScriptedInterviewee(["Yes.", Serious, NameReply, "It was my manager, in our meetings, and it went on for a long time.", Deep, Deep, Deep, .. SixAnswers[1..]]);

        var result = await runner.RunAsync(interviewee);

        Assert.Contains(interviewee.Seen, t => t.Kind == TurnKind.Redirect);
        Assert.Equal(4, interviewee.Seen.Count(t => t.Kind == TurnKind.DeepProbe));
        Assert.Equal(1, result.Diagnostics.NamesMasked);
        Assert.DoesNotContain("Brunhilda", result.Transcript!.Render());
        Assert.Contains("FOCUS: when_how_often", model.Calls.Where(c => c.Role == Role.Prober).Skip(1).First().User);
        Assert.All(model.Calls, c => Assert.DoesNotContain("Brunhilda", c.User));
    }

    [Fact]
    public async Task An_interview_without_a_serious_signal_has_no_deepening_turns_and_no_reminder()
    {
        var runner = Runner(out _);
        var interviewee = new ScriptedInterviewee(["Yes, I agree.", .. SixAnswers]);

        var result = await runner.RunAsync(interviewee);

        Assert.Equal(InterviewOutcome.Completed, result.Outcome);
        Assert.DoesNotContain(interviewee.Seen, t => t.Kind == TurnKind.DeepProbe);
        Assert.DoesNotContain(interviewee.Seen, t => t.Text.Contains(Proto.DeepeningReminder, StringComparison.Ordinal));
    }
}
