using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

/// <summary>
/// Interview dynamics 1.3 reply signals (interview-dynamics-spec §3.6–3.7, WP3): register, rating, "already said", distress,
/// legal remark and exit event. Explicit-statement cues only, EN and PL, read on the masked reply and its ASCII fold.
/// </summary>
public class ReplySignalsV13Tests
{
    private static ReplySignals Analyze(string text, bool consent = false) => ReplyAnalyzer.Analyze(text, Limits, namesPerson: false, consentReply: consent);

    // ---- Register (§3.6) -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Tak, na ty.", AddressForm.Ty)]
    [InlineData("tak, na Ty", AddressForm.Ty)]
    [InlineData("Tak, per Ty", AddressForm.Ty)]
    [InlineData("Tak, per Pani", AddressForm.Pani)]
    [InlineData("tak, per pani", AddressForm.Pani)]
    [InlineData("Tak, per Pan", AddressForm.Pan)]
    [InlineData("tak, pani", AddressForm.Pani)]
    [InlineData("Tak, pan", AddressForm.Pan)]
    [InlineData("Tak", AddressForm.Neutral)]
    [InlineData("Nie", AddressForm.Neutral)]
    public void The_consent_reply_can_choose_the_form_of_address(string text, AddressForm expected) =>
        Assert.Equal(expected, ReplyAnalyzer.AddressOf(text, consentReply: true));

    [Theory]
    [InlineData("możemy na ty", AddressForm.Ty)]
    [InlineData("Mówmy sobie per Pani, proszę.", AddressForm.Pani)]
    [InlineData("mozemy na ty", AddressForm.Ty)]
    public void A_later_reply_switches_the_form_only_when_it_says_so_explicitly(string text, AddressForm expected) =>
        Assert.Equal(expected, ReplyAnalyzer.AddressOf(text));

    [Theory]
    [InlineData("Pani Ania bardzo mi pomogła w pierwszym miesiącu.")]
    [InlineData("Pan Kowalski był w porządku.")]
    [InlineData("Pani z działu HR była miła.")]
    public void A_word_like_pani_inside_an_answer_is_not_a_register_choice(string text) =>
        Assert.Equal(AddressForm.Neutral, ReplyAnalyzer.AddressOf(text));

    [Fact]
    public void A_pani_inside_a_later_answer_is_ignored_even_when_it_is_the_only_word()
    {
        Assert.Equal(AddressForm.Neutral, ReplyAnalyzer.AddressOf("Pani"));
        Assert.Equal(AddressForm.Pani, ReplyAnalyzer.AddressOf("Pani", consentReply: true));
    }

    [Fact]
    public void Analyze_carries_the_address_form_for_the_consent_reply_only_when_asked()
    {
        Assert.Equal(AddressForm.Ty, Analyze("tak, na ty", consent: true).Address);
        Assert.Equal(AddressForm.Neutral, Analyze("Pani Ania była miła.").Address);
        Assert.Equal(AddressForm.Ty, Analyze("możemy na ty").Address);
    }

    [Fact]
    public void Ordinary_consent_still_reads_as_yes() =>
        Assert.Equal(ConsentAnswer.Yes, Analyze("tak, na ty", consent: true).Consent);

    // ---- ScaleValue (§3.4 rule 7) ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("3", 3)]
    [InlineData("1", 1)]
    [InlineData("5.", 5)]
    [InlineData("4/5", 4)]
    [InlineData("dam 4", 4)]
    [InlineData("I'd say 2", 2)]
    [InlineData("one", 1)]
    [InlineData("Five", 5)]
    [InlineData("pięć", 5)]
    [InlineData("piec", 5)]
    [InlineData("piątka", 5)]
    [InlineData("piatka", 5)]
    [InlineData("dwója", 2)]
    [InlineData("dwoja", 2)]
    [InlineData("trójka", 3)]
    [InlineData("czwórka", 4)]
    [InlineData("jedynka", 1)]
    public void A_bare_rating_is_read_as_the_scale_value(string text, int expected) =>
        Assert.Equal(expected, Analyze(text).ScaleValue);

    [Theory]
    [InlineData("Bardzo dobrze, bo zespół był super i szef pomagał mi w pracy.")]
    [InlineData("W 2019 roku zaczynałem.")]
    [InlineData("Trzy lata pracowałem w tym miejscu.")]
    [InlineData("I worked there for 3 years and it was fine.")]
    [InlineData("nie wiem")]
    [InlineData("3 albo 4, zależy od dnia")]
    public void Anything_longer_or_not_a_bare_rating_has_no_scale_value(string text) =>
        Assert.Null(Analyze(text).ScaleValue);

    [Fact]
    public void A_missing_rating_is_null_not_zero() => Assert.Null(Analyze("bardzo dobrze").ScaleValue);

    // ---- AlreadySaid (§3.7) ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Już o tym pisałem.")]
    [InlineData("Juz o tym pisalem.")]
    [InlineData("Już mówiłam.")]
    [InlineData("Przecież już pisałem.")]
    [InlineData("Przeciez juz pisalem wczesniej.")]
    [InlineData("To samo pytanie.")]
    [InlineData("I already said that.")]
    [InlineData("I already answered this.")]
    [InlineData("I already told you.")]
    [InlineData("Same question.")]
    public void Saying_it_was_already_said_is_read(string text) => Assert.True(Analyze(text).AlreadySaid);

    [Theory]
    [InlineData("Pisałam w ankiecie o pensji.")]
    [InlineData("Pensja była niska, a szef nie słuchał.")]
    [InlineData("I answered honestly about my manager.")]
    public void An_ordinary_answer_is_not_already_said(string text) => Assert.False(Analyze(text).AlreadySaid);

    // ---- Distress (§3.7) -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Nie daję rady.")]
    [InlineData("nie daje rady")]
    [InlineData("Nie radzę sobie z tym od roku.")]
    [InlineData("Miałem myśli samobójcze.")]
    [InlineData("Chcę się zabić.")]
    [InlineData("Miałem załamanie nerwowe w pracy.")]
    [InlineData("Miałem atak paniki na spotkaniu.")]
    [InlineData("Miałam depresję przez ten czas.")]
    [InlineData("Byłem na terapii, bo wykończyła mnie ta praca.")]
    [InlineData("I can't cope anymore.")]
    [InlineData("I can’t cope with it.")]
    [InlineData("I'm thinking about suicide.")]
    [InlineData("I had a panic attack at my desk.")]
    [InlineData("I had a nervous breakdown after the review.")]
    [InlineData("I was in therapy for depression because of it.")]
    public void An_explicit_statement_of_distress_is_read(string text) => Assert.True(Analyze(text).Distress);

    [Theory]
    [InlineData("Nie mam myśli samobójczych.")]
    [InlineData("Nie miałem załamania nerwowego.")]
    [InlineData("I never had a panic attack.")]
    [InlineData("Rynek był w depresji.")]
    [InlineData("Wiem, że terapia pomogłaby innym pracownikom.")]
    [InlineData("Firma miała problem z kolejnymi wdrożeniami.")]
    public void Negated_or_unrelated_mentions_are_not_distress(string text) => Assert.False(Analyze(text).Distress);

    // ---- LegalThreat (§3.7) ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Mam zamiar pozwać firmę.")]
    [InlineData("Pozwę ich, jeśli nic się nie zmieni.")]
    [InlineData("Firma została pozwana przez byłych pracowników.")]
    [InlineData("Rozważam pozew.")]
    [InlineData("Rozważam sprawę w sądzie pracy.")]
    [InlineData("Muszę porozmawiać z prawnikiem.")]
    [InlineData("Muszę porozmawiać z adwokatem.")]
    [InlineData("Zgłoszę to do PIP.")]
    [InlineData("Zgłoszę to do Państwowej Inspekcji Pracy.")]
    [InlineData("Zgłoszę to do inspekcji pracy.")]
    [InlineData("I'm going to sue them.")]
    [InlineData("I will talk to a lawyer about it.")]
    [InlineData("I might go to court over this.")]
    [InlineData("This could go to the labour tribunal.")]
    public void A_remark_about_legal_steps_is_read(string text) => Assert.True(Analyze(text).LegalThreat);

    [Theory]
    [InlineData("Nie mam zamiaru nikogo pozywać.")]
    [InlineData("Nie zamierzam iść do sądu.")]
    [InlineData("I was sad when I left.")]
    [InlineData("Pani Ania była bardzo pomocna.")]
    [InlineData("Mam dobre wspomnienia.")]
    // "pozwolił" (allowed) and "pozwala" (allows) share the stem "pozw"; they are everyday words, not a legal remark.
    [InlineData("Szef pozwolił mi się rozwijać.")]
    [InlineData("Firma pozwala na elastyczne godziny.")]
    [InlineData("Szef pozwalał na dużo.")]
    [InlineData("Pozwolono mi wyjść wcześniej.")]
    public void Negated_or_unrelated_mentions_are_not_a_legal_remark(string text) => Assert.False(Analyze(text).LegalThreat);

    // ---- ExitEvent and the Serious split (§3.7) --------------------------------------------------------------------------------

    [Theory]
    [InlineData("zwolnili mnie")]
    [InlineData("Zostałem zwolniony po roku.")]
    [InlineData("wyrzucili mnie")]
    [InlineData("I was fired")]
    [InlineData("I was laid off in March.")]
    [InlineData("I was sacked.")]
    [InlineData("I was dismissed after a year.")]
    public void Being_let_go_is_an_exit_event(string text)
    {
        var s = Analyze(text);
        Assert.True(s.ExitEvent);
        Assert.False(s.Serious);
    }

    [Theory]
    [InlineData("zwolniłem się sam")]
    [InlineData("zwolnilem sie")]
    [InlineData("nikt mnie nie zwolnił")]
    [InlineData("Dobrze mi się pracowało.")]
    public void Resigning_or_a_negated_dismissal_is_not_an_exit_event(string text) => Assert.False(Analyze(text).ExitEvent);

    [Theory]
    [InlineData("zwolnili mnie po mobbingu")]
    [InlineData("I was fired after I reported the harassment.")]
    public void An_exit_event_with_a_serious_term_is_still_serious(string text)
    {
        var s = Analyze(text);
        Assert.True(s.ExitEvent);
        Assert.True(s.Serious);
    }

    [Theory]
    [InlineData("There was mobbing in my team.")]
    [InlineData("Byłem mobbingowany przez przełożonego.")]
    [InlineData("I was bullied every week.")]
    public void A_serious_term_without_an_exit_is_serious_and_not_an_exit_event(string text)
    {
        var s = Analyze(text);
        Assert.True(s.Serious);
        Assert.False(s.ExitEvent);
    }

    [Fact]
    public void Threatened_with_dismissal_stays_serious_through_the_threat_cue()
    {
        var s = Analyze("Grożono mi zwolnieniem.");
        Assert.True(s.Serious);
        Assert.True(s.ExitEvent);
    }

    // ---- Defaults keep today's behaviour for callers that ignore the new fields -----------------------------------------------

    [Fact]
    public void Ordinary_talk_sets_no_new_flag()
    {
        var s = Analyze("The onboarding was fine and the team was welcoming.");
        Assert.False(s.Distress);
        Assert.False(s.LegalThreat);
        Assert.False(s.ExitEvent);
        Assert.False(s.AlreadySaid);
        Assert.Null(s.ScaleValue);
        Assert.Equal(AddressForm.Neutral, s.Address);
    }

    [Fact]
    public void A_signals_record_built_without_the_new_fields_still_compiles_and_reads_neutral()
    {
        var s = Signals();
        Assert.False(s.Distress);
        Assert.Null(s.ScaleValue);
        Assert.Equal(AddressForm.Neutral, s.Address);
    }
}
