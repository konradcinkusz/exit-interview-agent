using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Roles;

/// <summary>
/// The optional reflective sentence (Y2): the interviewer or prober may open with ONE short sentence that reflects the interviewee's words;
/// it has no question mark, and the guard still allows exactly one question in the whole output.
/// </summary>
public class ReflectionPromptTests
{
    private static readonly PiiGuard Pii = new(["Widgetron"]);

    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public void The_interviewer_rule_allows_one_reflective_sentence_and_forbids_judgement(string language)
    {
        var system = Prompts.InterviewerSystem(InterviewProtocol.For(language));

        Assert.Contains("ONE short sentence that reflects", system);
        Assert.Contains("no judgement", system);
        Assert.Contains("no reassurance", system);
        Assert.Contains("no question mark", system);
        Assert.DoesNotContain("Do not comment on, judge or reassure", system);
    }

    [Fact]
    public void The_prober_rule_carries_the_same_reflection_and_the_gender_rule()
    {
        var system = Prompts.ProberSystem(Helpers.Proto);

        Assert.Contains("ONE short sentence that reflects", system);
        Assert.Contains("Do not guess the interviewee's gender", system);
    }

    [Fact]
    public void A_reflection_followed_by_one_question_passes_the_guard() =>
        Assert.Equal("ok", QuestionGuard.Check("You said the bullying went on for months. How often did it happen?", TurnKind.DeepProbe, Pii).Reason);

    [Fact]
    public void A_reflection_that_contains_a_question_mark_is_rejected_as_a_second_question() =>
        Assert.Equal("double_barrelled", QuestionGuard.Check("Was it bullying? How often did it happen?", TurnKind.DeepProbe, Pii).Reason);

    [Fact]
    public void A_reflection_with_no_question_at_all_is_rejected()
    {
        var verdict = QuestionGuard.Check("That sounds hard to carry for months.", TurnKind.DeepProbe, Pii);

        Assert.False(verdict.Ok);
        Assert.Equal("no_question", verdict.Reason);
    }

    [Fact]
    public void The_deep_probe_user_message_names_the_menu_element_and_the_seed()
    {
        var request = new QuestionRequest(TurnKind.DeepProbe, Topic.Management, "How did this end for you?", new Transcript(), DeepFocus.HowItEndedAndMeaning);

        var user = Prompts.ProberUser(request, "abc");

        Assert.Contains("FOCUS: how_it_ended_and_meaning", user);
        Assert.Contains("SEED: How did this end for you?", user);
        Assert.Contains("never a name", user);
    }

    [Fact]
    public void The_deepening_seeds_never_ask_for_a_name()
    {
        foreach (var language in new[] { "en", "pl" })
            foreach (var seed in InterviewProtocol.For(language).DeepeningSeeds)
            {
                Assert.DoesNotContain("name", seed, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("imię", seed, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("nazwisk", seed, StringComparison.OrdinalIgnoreCase);
            }
    }
}
