using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;

namespace ExitInterviewAgent.Agent.Tests.Roles;

/// <summary>
/// Guard v2 (interview-dynamics spec §3.8), one rule per test: the question sentence and the acknowledgement are judged apart, the
/// interviewee's own quoted words are exempt from the loaded and assumptive lints but must be verbatim from their last turn, and a
/// move's shape (scale anchors, follow-up without an example request) is checked. The retry is WP5 and is not tested here.
/// </summary>
public class QuestionGuardV2Tests
{
    private static readonly PiiGuard Pii = new(["Widgetron"]);

    private static string Reason(string? text, TurnKind kind = TurnKind.Topic, QuestionShape shape = QuestionShape.Default, string? said = null) =>
        QuestionGuard.Check(text, kind, Pii, shape, said).Reason;

    [Fact]
    public void A_stock_acknowledgement_is_not_rejected() =>
        Assert.Equal("ok", Reason("Rozumiem. Słyszę, że to było trudne. Co się wtedy działo?", TurnKind.Probe, QuestionShape.Follow));

    [Fact]
    public void A_leading_statement_in_the_acknowledgement_is_rejected_as_leading() =>
        Assert.Equal("leading", Reason("Przecież to musiało być trudne. Co się wtedy działo?", TurnKind.DeepProbe));

    [Fact]
    public void An_assumptive_word_in_the_acknowledgement_is_rejected_even_before_a_good_question() =>
        Assert.Equal("leading", Reason("Pewnie było ciężko. Jak długo to trwało?", TurnKind.DeepProbe));

    [Fact]
    public void A_loaded_phrase_in_the_acknowledgement_is_rejected_as_loaded() =>
        Assert.Equal("loaded", Reason("Jak okropnie to brzmi. Co się wtedy działo?", TurnKind.DeepProbe));

    [Fact]
    public void A_loaded_phrase_inside_the_interviewees_own_quotes_is_not_judged()
    {
        const string said = "Jak okropnie to brzmi, ale tak było.";

        Assert.Equal("ok", Reason("„Jak okropnie to brzmi”. Co się wtedy działo?", TurnKind.DeepProbe, said: said));
    }

    [Fact]
    public void A_quote_that_is_not_in_the_last_interviewee_turn_is_a_misquote() =>
        Assert.Equal("misquote", Reason("„To było upokarzające”. Co się wtedy działo?", TurnKind.DeepProbe, said: "Nie chcę o tym mówić."));

    [Fact]
    public void A_quote_is_matched_ignoring_case_and_whitespace() =>
        Assert.Equal("ok", Reason("„jak   okropnie\nto brzmi”. Co się wtedy działo?", TurnKind.DeepProbe, said: "Jak okropnie to brzmi."));

    [Fact]
    public void A_quote_without_any_interviewee_turn_to_check_it_against_is_a_misquote() =>
        Assert.Equal("misquote", Reason("„Nie chcę o tym mówić”. Co się wtedy działo?", TurnKind.DeepProbe));

    [Fact]
    public void A_quote_in_the_question_sentence_is_checked_as_well() =>
        Assert.Equal("misquote", Reason("Co znaczy „plan był zły”?", TurnKind.Probe, QuestionShape.Follow, said: "Plan był dobry."));

    [Fact]
    public void A_question_that_asks_inside_the_acknowledgement_is_a_second_question() =>
        Assert.Equal("double_barrelled", Reason("Czy to było trudne? Co się wtedy działo?", TurnKind.DeepProbe));

    [Fact]
    public void A_sentence_after_the_question_mark_is_part_of_the_question_for_the_lint() =>
        Assert.Equal("leading", Reason("Co się stało? Pewnie coś poszło źle.", TurnKind.DeepProbe));

    [Fact]
    public void Sixty_words_pass_and_sixty_one_are_too_many()
    {
        var sixty = string.Join(" ", Enumerable.Repeat("słowo", 59)) + " dalej?";
        var sixtyOne = string.Join(" ", Enumerable.Repeat("słowo", 60)) + " dalej?";

        Assert.Equal("ok", Reason(sixty, TurnKind.DeepProbe));
        Assert.Equal("max_words", Reason(sixtyOne, TurnKind.DeepProbe));
    }

    [Fact]
    public void A_scale_question_needs_both_anchors_one_and_five()
    {
        Assert.Equal("ok", Reason("W skali od 1 do 5, jak ocenić wdrożenie?", TurnKind.Topic, QuestionShape.Scale));
        Assert.Equal("scale_without_anchors", Reason("W skali od 1 do 4, jak ocenić wdrożenie?", TurnKind.Topic, QuestionShape.Scale));
        Assert.Equal("scale_without_anchors", Reason("W skali od 2 do 5, jak ocenić wdrożenie?", TurnKind.Topic, QuestionShape.Scale));
    }

    [Fact]
    public void A_scale_question_may_be_closed_and_a_default_topic_question_may_not()
    {
        const string closed = "Czy była podwyżka, w skali od 1 do 5?";

        Assert.Equal("ok", Reason(closed, TurnKind.Topic, QuestionShape.Scale));
        Assert.Equal("closed_question", Reason(closed, TurnKind.Topic, QuestionShape.Default));
    }

    [Fact]
    public void A_follow_up_that_is_not_an_example_request_is_fine_but_a_default_probe_must_ask_for_one()
    {
        const string follow = "Co najbardziej zaważyło na tej ocenie?";

        Assert.Equal("ok", Reason(follow, TurnKind.Probe, QuestionShape.Follow));
        Assert.Equal("probe_without_example", Reason(follow, TurnKind.Probe, QuestionShape.Default));
    }

    [Fact]
    public void A_probe_that_asks_for_a_concrete_situation_passes_as_the_default_example()
    {
        Assert.Equal("ok", Reason("Jaka jedna konkretna sytuacja najlepiej to pokazuje?", TurnKind.Probe));
        Assert.Equal("ok", Reason("Could you give me one specific example of that?", TurnKind.Probe));
    }

    [Fact]
    public void An_invitation_opens_a_topic_without_a_question_mark() =>
        Assert.Equal("ok", Reason("Proszę opowiedzieć, na jakim stanowisku pracowało się w tej firmie."));

    [Fact]
    public void A_statement_that_is_neither_question_nor_invitation_is_no_question() =>
        Assert.Equal("no_question", Reason("Opowiem o tym później."));

    [Fact]
    public void Personal_data_in_the_acknowledgement_is_still_caught() =>
        Assert.Equal("pii", Reason("Rozumiem: anna.kowalska@example.com. Co się wtedy działo?", TurnKind.DeepProbe));

    [Fact]
    public void The_employer_name_passes_in_the_acknowledgement_too() =>
        Assert.Equal("ok", Reason("Widgetron to trudny temat. Co się wtedy działo?", TurnKind.DeepProbe));

    [Fact]
    public void The_existing_single_question_forms_keep_their_verdicts()
    {
        Assert.Equal("ok", Reason("Co się wtedy działo?", TurnKind.DeepProbe));
        Assert.Equal("multiple_questions", Reason("Co się stało? Kto to zrobił? Kiedy?"));
        Assert.Equal("double_barrelled", Reason("Co się stało? Kiedy?"));
    }
}
