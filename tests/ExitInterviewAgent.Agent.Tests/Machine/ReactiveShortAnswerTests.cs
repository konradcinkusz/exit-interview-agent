using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

/// <summary>
/// A pre-release run showed an interviewee who answered in a few words ("słabe", "bardzo źle", "zwolnili mnie") being thanked and
/// closed as unresponsive without one follow-up. Short answers that say something are answers: they are asked "what exactly".
/// </summary>
public class ReactiveShortAnswerTests
{
    private static ReplySignals Analyze(string text) => ReplyAnalyzer.Analyze(text, Limits, false);

    [Theory]
    [InlineData("bardzo dobrze")]
    [InlineData("slabe")]
    [InlineData("słabe")]
    [InlineData("bardzo zle")]
    [InlineData("bardzo źle")]
    [InlineData("chyba kierowana praca, bylem zatrudniony jako programista")]
    public void A_short_answer_that_says_something_is_brief(string text)
    {
        var s = Analyze(text);
        Assert.True(s.Short);
    }

    [Theory]
    [InlineData("tak")]
    [InlineData("nie wiem")]
    [InlineData("Nie wiem.")]
    public void A_bare_non_answer_is_not_followed_up(string text) => Assert.False(Analyze(text).Short);

    [Theory]
    [InlineData("zwolnili mnie")]
    [InlineData("Zostałem zwolniony po roku.")]
    [InlineData("wyrzucili mnie")]
    [InlineData("I was fired")]
    public void Being_dismissed_opens_the_deepening(string text) => Assert.True(Analyze(text).Serious);

    [Theory]
    [InlineData("zwolniłem się sam")]
    [InlineData("zwolnilem sie")]
    public void Resigning_in_polish_is_not_a_dismissal(string text) => Assert.False(Analyze(text).Serious);

    [Theory]
    [InlineData("bylem mobbingowany")]
    [InlineData("szef mnie upokarzal przy wszystkich")]
    [InlineData("grozono mi zwolnieniem")]
    public void Polish_typed_without_diacritics_is_still_read(string text) => Assert.True(Analyze(text).Serious);

    [Fact]
    public void The_run_that_failed_goes_on_instead_of_closing()
    {
        var m = AtFirstTopic(); // onboarding asked
        var answers = new[] { "bardzo dobrze", "chyba kierowana praca, bylem zatrudniony jako programista", "nie bylo w ogole", "slabe", "bardzo zle", "zwolnili mnie" };
        var kinds = new List<TurnKind>();
        foreach (var a in answers)
        {
            var step = m.OnReply(Analyze(a));
            kinds.Add(step.Kind);
            Assert.False(m.IsTerminal, $"closed on: {a}");
            Assert.NotEqual(TurnKind.Close, step.Kind);
        }
        Assert.Contains(TurnKind.Probe, kinds);
        Assert.Contains(TurnKind.DeepProbe, kinds);
    }

    [Fact]
    public void Three_bare_non_answers_in_a_row_still_close_politely()
    {
        var m = AtFirstTopic();
        m.OnReply(Analyze("nie wiem"));
        m.OnReply(Analyze("nie wiem"));
        var step = m.OnReply(Analyze("nie wiem"));
        Assert.Equal(TurnKind.Close, step.Kind);
        Assert.Equal(CloseReason.Unresponsive, step.Close);
    }

    [Fact]
    public void A_short_answer_gets_one_follow_up_and_then_the_next_topic()
    {
        var m = AtFirstTopic();
        var first = m.OnReply(Analyze("słabe"));
        Assert.Equal(TurnKind.Probe, first.Kind);
        var second = m.OnReply(Analyze("słabe"));
        Assert.Equal(TurnKind.Topic, second.Kind);
    }
}

/// <summary>The second pre-release run: "możemy już skończyć?" was answered with another question, "mobingowany" (one b) was not seen.</summary>
public class SecondRunRegressionTests
{
    private static ReplySignals Analyze(string text) => ReplyAnalyzer.Analyze(text, Limits, false);

    [Theory]
    [InlineData("mozemy juz skonczyc?")]
    [InlineData("Możemy już skończyć?")]
    [InlineData("czy możemy zakończyć")]
    [InlineData("Can we wrap up?")]
    [InlineData("skończmy już")]
    public void A_request_to_finish_is_recognised_and_is_not_a_withdrawal(string text)
    {
        var s = Analyze(text);
        Assert.True(s.FinishRequest);
        Assert.False(s.Withdrawal);
    }

    [Theory]
    [InlineData("Kończymy projekty w terminie")]
    [InlineData("nie bylo w ogole")]
    public void Ordinary_talk_is_not_a_finish_request(string text) => Assert.False(Analyze(text).FinishRequest);

    [Fact]
    public void A_finish_request_closes_with_a_record_not_a_stop_and_not_another_question()
    {
        var m = AtFirstTopic();
        var step = m.OnReply(Analyze("mozemy juz skonczyc?"));
        Assert.Equal(TurnKind.Close, step.Kind);
        Assert.Equal(CloseReason.Unresponsive, step.Close);
    }

    [Theory]
    [InlineData("bylem mobingowany przez kolege")]
    [InlineData("byłem mobbingowany")]
    [InlineData("gnebili mnie")]
    [InlineData("nekali mnie codziennie")]
    [InlineData("poniżali mnie przy ludziach")]
    public void Common_spellings_of_a_serious_account_open_the_deepening(string text) => Assert.True(Analyze(text).Serious);

    [Theory]
    [InlineData("Wynagrodzenie było poniżej rynku")]
    public void Below_market_is_not_a_serious_account(string text) => Assert.False(Analyze(text).Serious);
}
