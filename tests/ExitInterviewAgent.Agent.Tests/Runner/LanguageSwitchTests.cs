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

/// <summary>Y4: the mid-interview language switch (deterministic detector, wording only), the record's language, and the wording a role receives.</summary>
public class LanguageSwitchTests
{
    // Concrete, substantive answers, so the machine does not ask for an example and the topic order is unchanged.
    private const string EnAnswer = "On this topic there was one concrete thing: the process took 3 weeks and nobody explained why.";
    private const string PlAnswer = "Na tym temacie było jedno konkretne zdarzenie: proces trwał 3 tygodnie i nikt nie wyjaśnił dlaczego.";

    // Protocol 1.3: a plain answer (nine to eleven words, no concrete cue, no vague cue, no topic cue) gets no follow-up, so the turns are fixed:
    // opening, warm-up, reason, onboarding, management, growth, pay, culture (all scaled but two), retention, anything else, close.
    private const string EnPlain = "On this topic there was a lot of work and meetings.";
    private const string PlPlain = "Na tym temacie było dużo pracy i wielu spotkań.";

    /// <summary>The interviewer turns of a plain interview, in order: 0 opening, 1 warm-up, 2 reason, 3 onboarding, 4 management, 5 growth, ...</summary>
    private const int Onboarding = 3, Management = 4;

    private static readonly InterviewProtocol Pl = InterviewProtocol.For("pl");

    private static string Cooperative(Role role, string user) => role == Role.Extractor
        ? new ScriptedChatClient().GetResponseAsync([new ChatMessage(ChatRole.System, Prompts.ExtractorSystem(Proto)), new ChatMessage(ChatRole.User, user)]).Result.Text
        : user.Split('\n').First(l => l.StartsWith("SEED: ", StringComparison.Ordinal))[6..];

    private static InterviewRunner Runner(InterviewProtocol? protocol = null) =>
        InterviewRunner.Create(new FakeChatClient(Cooperative), Options with { Protocol = protocol ?? Proto });

    private static string TopicText(ScriptedInterviewee interviewee, Topic topic) =>
        interviewee.Seen.First(t => t.Kind == TurnKind.Topic && t.Topic == topic).Text;

    // ---- the detector --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Nie wiem, to jest trudne dla mnie, bo się boję.", "pl")]
    [InlineData("The manager was supportive, but the process took weeks.", "en")]
    [InlineData("Process 2 weeks, nie the 3 people.", null)]
    [InlineData("12 3 4 5 6", null)]
    public void The_detector_counts_polish_and_english_words_and_stays_silent_on_a_tie(string text, string? expected) =>
        Assert.Equal(expected, InterviewLanguage.Detect(text));

    [Theory]
    [InlineData("Mów po polsku, proszę.", "pl")]
    [InlineData("Mów po angielsku, proszę.", "en")]
    [InlineData("Can you continue in English, please?", "en")]
    [InlineData("Let's speak English from now on.", "en")]
    [InlineData("Dobrze, nie wiem.", null)]
    public void A_request_for_a_language_is_recognised_whatever_the_length(string text, string? expected) =>
        Assert.Equal(expected, InterviewLanguage.Request(text));

    [Fact]
    public void A_reply_of_fewer_than_five_words_does_not_switch_unless_it_asks_for_a_language()
    {
        Assert.Null(InterviewLanguage.SwitchTo("Nie wiem, sorry.", "en"));
        Assert.Equal("pl", InterviewLanguage.SwitchTo("Po polsku, proszę.", "en"));
    }

    [Fact]
    public void A_substantive_reply_in_the_other_language_switches_and_one_in_the_current_language_does_not()
    {
        Assert.Equal("pl", InterviewLanguage.SwitchTo(PlAnswer, "en"));
        Assert.Null(InterviewLanguage.SwitchTo(PlAnswer, "pl"));
        Assert.Null(InterviewLanguage.SwitchTo(EnAnswer, "en"));
        Assert.Null(InterviewLanguage.SwitchTo("Process 2 weeks, nie the 3 people, one manager.", "en"));
    }

    [Theory]
    [InlineData(new[] { "pl", "pl", "en" }, "en", "pl")]
    [InlineData(new[] { "pl", "en" }, "en", "en")]
    [InlineData(new[] { "en", "pl" }, "en", "pl")]
    [InlineData(new string[0], "pl", "pl")]
    public void The_record_language_is_the_majority_of_interviewee_turns_and_a_tie_takes_the_last(string[] turns, string fallback, string expected) =>
        Assert.Equal(expected, InterviewLanguage.RecordLanguage(turns, fallback));

    [Fact]
    public void The_confirmation_sentence_is_one_sentence_in_the_language_switched_to()
    {
        Assert.Equal("Dobrze, kontynuujmy po polsku.", InterviewLanguage.Confirmation("pl"));
        Assert.Equal("Sure, let's continue in English.", InterviewLanguage.Confirmation("en"));
    }

    // ---- the runner ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_polish_answer_in_an_english_interview_moves_the_next_question_to_polish_with_one_confirmation_sentence()
    {
        var interviewee = new ScriptedInterviewee("Yes, I agree.", EnPlain, PlPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain);

        var result = await Runner().RunAsync(interviewee);

        Assert.Equal(InterviewOutcome.Completed, result.Outcome);
        Assert.Equal($"Dobrze, kontynuujmy po polsku. {Pl.Spec(Topic.Onboarding).Question}", interviewee.Seen[Onboarding].Text);
        // The next English reply moves the interview back: the switch follows each clear reply, not only the first.
        Assert.Equal($"Sure, let's continue in English. {Proto.Spec(Topic.Management).Question}", interviewee.Seen[Management].Text);
        Assert.Equal("en", result.Record!.Interview.Language);
    }

    [Theory]
    [InlineData("Mów po angielsku, proszę, na tym temacie było dużo pracy.")]
    [InlineData("Can you continue in English, please? Lots of work.")]
    public async Task An_english_request_in_a_polish_interview_moves_the_next_question_to_english(string request)
    {
        var interviewee = new ScriptedInterviewee("Tak, zgadzam się.", PlPlain, request, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain);

        var result = await Runner(Pl).RunAsync(interviewee);

        Assert.Equal(InterviewOutcome.Completed, result.Outcome);
        Assert.Equal($"Sure, let's continue in English. {Proto.Spec(Topic.Onboarding).Question}", interviewee.Seen[Onboarding].Text);
        Assert.Equal("en", result.Record!.Interview.Language);
    }

    [Fact]
    public async Task A_tie_between_the_languages_keeps_the_current_one()
    {
        // "Process 2 weeks, nie the 3 people, one manager." is read as no language, so nothing switches and the wording stays English.
        var interviewee = new ScriptedInterviewee("Yes, I agree.", EnPlain, "Process 2 weeks, nie the 3 people, one manager.", EnPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain, EnPlain);

        var result = await Runner().RunAsync(interviewee);

        // "manager" in the tie reply makes Management salient, so it is the next topic (the planner's order, not the language's).
        Assert.Equal(Proto.Spec(Topic.Management).Question, interviewee.Seen[Onboarding].Text);
        Assert.DoesNotContain(interviewee.Seen, t => t.Text.Contains("kontynuujmy", StringComparison.Ordinal));
        Assert.Equal("en", result.Record!.Interview.Language);
    }

    [Fact]
    public async Task The_record_language_is_the_one_most_interviewee_turns_were_written_in()
    {
        // The interview starts in English; six of the nine replies are Polish and three English, so the record says Polish.
        var interviewee = new ScriptedInterviewee("Yes, I agree.", PlPlain, PlPlain, PlPlain, EnPlain, PlPlain, EnPlain, PlPlain, EnPlain, PlPlain);

        var result = await Runner().RunAsync(interviewee);

        Assert.Equal("pl", result.Record!.Interview.Language);
    }

    [Fact]
    public async Task The_switch_does_not_change_the_topics_or_their_order()
    {
        var interviewee = new ScriptedInterviewee("Yes, I agree.", PlPlain, EnPlain, PlPlain, EnPlain, PlPlain, EnPlain, PlPlain, EnPlain, PlPlain);

        var result = await Runner().RunAsync(interviewee);

        Assert.Equal(InterviewOutcome.Completed, result.Outcome);
        // Protocol 1.3 order: the reason first (asked again for retention, counted once), then the planner's order with no salience.
        Assert.Equal(
            [Topic.ReasonForLeaving, Topic.Onboarding, Topic.Management, Topic.Growth, Topic.PayVsPromises, Topic.Culture],
            interviewee.Seen.Where(t => t.Kind == TurnKind.Topic && t.Topic is not null).Select(t => t.Topic!.Value).Distinct().ToArray());
    }

    // ---- the role -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_interviewer_words_the_question_in_the_wording_the_request_carries_not_the_one_it_was_built_with()
    {
        var client = new FakeChatClient((_, _) => "Co się stało?");
        var role = new ModelInterviewer(client, Proto);

        await role.AskAsync(new QuestionRequest(TurnKind.Topic, Topic.Management, Pl.Spec(Topic.Management).Question, new Transcript(), null, Pl), default);

        Assert.Contains("in Polish", client.Calls[0].System);
        Assert.DoesNotContain("in English", client.Calls[0].System);
    }
}
