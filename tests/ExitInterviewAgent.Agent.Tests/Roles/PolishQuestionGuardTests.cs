using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;

namespace ExitInterviewAgent.Agent.Tests.Roles;

/// <summary>The question guard's Polish rules (Y2), the counterparts of the English rules in <see cref="QuestionGuardTests"/> (ADR-0062).</summary>
public class PolishQuestionGuardTests
{
    private static readonly PiiGuard Pii = new(["Widgetron"]);

    private static string Reason(string? text, TurnKind kind = TurnKind.Topic) => QuestionGuard.Check(text, kind, Pii).Reason;

    [Theory]
    [InlineData("Jak wyglądało dołączenie do firmy i wejście w rolę?")]
    [InlineData("Jak na co dzień wyglądało kierowanie pracą?")]
    [InlineData("Może Pan/Pani opisać, co się wtedy wydarzyło?")]
    [InlineData("Jakie role były w to zaangażowane, na przykład przełożony albo współpracownik?")]
    [InlineData("Jak firma zareagowała, gdy to zgłoszono?")]
    public void Neutral_polish_open_questions_pass(string q) => Assert.Equal("ok", Reason(q));

    [Theory]
    [InlineData("Czy nie uważa Pan/Pani, że przełożony był niesprawiedliwy?", "leading")]
    [InlineData("Czyż nie było to toksyczne miejsce?", "leading")]
    [InlineData("To było toksyczne miejsce, prawda?", "leading")]
    [InlineData("Wynagrodzenie było niskie, nieprawda?", "leading")]
    [InlineData("Oczywiście, że przełożony był niesprawiedliwy?", "leading")]
    [InlineData("Jak bardzo źle to wyglądało?", "loaded")]
    [InlineData("Czy miał Pan szkolenie?", "closed_question")]
    [InlineData("Czy dostała Pani podwyżkę?", "closed_question")]
    public void Leading_loaded_and_closed_polish_questions_are_rejected(string q, string reason) => Assert.Equal(reason, Reason(q));

    [Theory]
    [InlineData("Jak to wyglądało i czy też wpływało na zdrowie?")]
    [InlineData("Co się stało, i jak to wpłynęło na pracę?")]
    [InlineData("Jak to wyglądało lub co się zmieniło?")]
    [InlineData("Co się stało? Kiedy?")]
    public void A_double_barrelled_polish_question_is_rejected(string q) => Assert.Equal("double_barrelled", Reason(q));

    [Fact]
    public void A_polish_example_request_satisfies_the_probe_rule() =>
        Assert.Equal("ok", Reason("Jaka jedna konkretna sytuacja albo moment najlepiej to ilustruje?", TurnKind.Probe));

    [Fact]
    public void A_deep_probe_is_guarded_as_a_question_and_passes_when_it_asks_one_thing() =>
        Assert.Equal("ok", Reason("Jak to się skończyło?", TurnKind.DeepProbe));

    [Fact]
    public void A_deep_probe_with_two_questions_is_rejected() =>
        Assert.Equal("multiple_questions", Reason("Co się stało? Kiedy? Kto?", TurnKind.DeepProbe));
}
