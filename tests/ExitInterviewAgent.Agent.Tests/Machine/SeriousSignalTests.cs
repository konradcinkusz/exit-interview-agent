using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

/// <summary>The serious-account signal (Y2): a deterministic floor, EN and PL, with a three-word negation window.</summary>
public class SeriousSignalTests
{
    private static ReplySignals Analyze(string text) => ReplyAnalyzer.Analyze(text, Limits, namesPerson: false);

    [Theory]
    [InlineData("I was bullied by my manager for months.")]
    [InlineData("It was harassment every single week.")]
    [InlineData("There was mobbing in my team.")]
    [InlineData("They discriminated against women in promotions.")]
    [InlineData("My manager threatened to fire me if I complained.")]
    [InlineData("I faced retaliation after I reported it.")]
    [InlineData("They never paid my overtime and withheld pay for two months.")]
    [InlineData("I was humiliated in front of the whole team.")]
    [InlineData("The site was unsafe and nobody fixed it.")]
    public void English_serious_accounts_are_flagged(string text) => Assert.True(Analyze(text).Serious);

    [Theory]
    [InlineData("Byłem mobbingowany przez przełożonego.")]
    [InlineData("Doświadczyłam mobbingu w zespole.")]
    [InlineData("Prześladowanie trwało cały rok.")]
    [InlineData("Szykanowano mnie za każdy błąd.")]
    [InlineData("Doszło do molestowania w biurze.")]
    [InlineData("Dyskryminowano mnie ze względu na wiek.")]
    [InlineData("Grożono mi zwolnieniem, jeśli zgłoszę to dalej.")]
    [InlineData("Po zgłoszeniu spotkał mnie odwet.")]
    [InlineData("Wyzyskiwano nas bez nadgodzin.")]
    [InlineData("Warunki były niebezpieczne.")]
    [InlineData("Nie wypłacono mi pensji przez dwa miesiące.")]
    [InlineData("Upokarzano mnie przy wszystkich.")]
    [InlineData("Wyzywał mnie od idiotów.")]
    [InlineData("Obrażał mnie publicznie.")]
    public void Polish_serious_accounts_are_flagged(string text) => Assert.True(Analyze(text).Serious);

    [Theory]
    [InlineData("There was no bullying in my team.")]
    [InlineData("I never experienced harassment.")]
    [InlineData("I wasn't bullied at all.")]
    [InlineData("Nie było mobbingu.")]
    [InlineData("Bez mobbingu, ale z presją na wyniki.")]
    public void A_simple_negation_just_before_the_term_is_not_serious(string text) => Assert.False(Analyze(text).Serious);

    [Fact]
    public void A_negated_term_does_not_hide_a_later_affirmed_one() =>
        Assert.True(Analyze("No harassment, but I was bullied every week.").Serious);

    [Theory]
    [InlineData("The onboarding was fine and the team was welcoming.")]
    [InlineData("Pay was okay and the manager was supportive.")]
    [InlineData("Kultura była dobra, a zespół pomocny.")]
    public void Ordinary_talk_is_not_serious(string text) => Assert.False(Analyze(text).Serious);

    [Fact]
    public void Serious_is_read_from_the_masked_reply_only_and_is_not_an_assessment_of_the_person()
    {
        // The signal is a boolean the machine reads; it never reaches a record, a quote or a model prompt by itself.
        var s = Analyze("I was bullied.");
        Assert.True(s.Serious);
        Assert.Equal(0, s.Polarity);
    }

    [Fact]
    public void Deepening_menu_elements_touched_by_a_reply_are_flagged_by_bit()
    {
        var bit = (int f) => 1 << f;
        var s = Analyze("On Monday my manager shouted at me in front of the team.");

        Assert.NotEqual(0, s.DeepCovered & bit((int)DeepFocus.WhatHappened));
        Assert.NotEqual(0, s.DeepCovered & bit((int)DeepFocus.WhenHowOften));
        Assert.NotEqual(0, s.DeepCovered & bit((int)DeepFocus.WhoByRole));
        Assert.Equal(0, s.DeepCovered & bit((int)DeepFocus.WhatTheyDidAndResponse));
        Assert.Equal(0, s.DeepCovered & bit((int)DeepFocus.HowItEndedAndMeaning));
    }

    [Fact]
    public void Polish_menu_cues_are_read_too()
    {
        var s = Analyze("W poniedziałek przełożony na mnie krzyczał przy zespole.");

        Assert.NotEqual(0, s.DeepCovered & (1 << (int)DeepFocus.WhenHowOften));
        Assert.NotEqual(0, s.DeepCovered & (1 << (int)DeepFocus.WhoByRole));
    }
}
