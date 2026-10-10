using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Protocol;

/// <summary>The Polish protocol is the same instrument worded in Polish: same structure, same limits, same rule ids, same guarantees.</summary>
public class PolishProtocolTests
{
    private static readonly InterviewProtocol Pl = InterviewProtocol.For("pl");
    private static readonly PiiGuard Pii = new(["Widgetron"]);

    [Fact]
    public void The_polish_protocol_is_labelled_pl_and_keeps_the_protocol_version()
    {
        Assert.Equal("pl", Pl.Language);
        Assert.Equal(Proto.ProtocolVersion, Pl.ProtocolVersion);
    }

    [Fact]
    public void The_polish_protocol_has_the_same_six_topics_in_the_same_order_with_the_same_ids()
    {
        Assert.Equal(Proto.Topics.Select(t => t.Topic), Pl.Topics.Select(t => t.Topic));
        Assert.Equal(Proto.Topics.Select(t => t.Id), Pl.Topics.Select(t => t.Id));
    }

    [Fact]
    public void Every_polish_topic_has_its_own_polish_title_and_question_not_the_english_text()
    {
        for (var i = 0; i < Pl.Topics.Count; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(Pl.Topics[i].Title));
            Assert.False(string.IsNullOrWhiteSpace(Pl.Topics[i].Question));
            Assert.NotEqual(Proto.Topics[i].Question, Pl.Topics[i].Question);
            Assert.NotEqual(Proto.Topics[i].Title, Pl.Topics[i].Title);
        }
    }

    [Fact]
    public void The_polish_limits_are_exactly_the_english_limits() =>
        Assert.Equal(Proto.Limits, Pl.Limits);

    [Fact]
    public void The_polish_rules_have_the_same_ids_and_enforcement_in_the_same_order()
    {
        Assert.Equal(Proto.Rules.Select(r => (r.Id, r.Enforcement)), Pl.Rules.Select(r => (r.Id, r.Enforcement)));
        Assert.All(Pl.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Text)));
    }

    [Fact]
    public void The_polish_closings_and_acknowledgements_are_present_and_translated()
    {
        Assert.All(Enum.GetValues<CloseReason>(), c => Assert.False(string.IsNullOrWhiteSpace(Pl.Closings[c])));
        Assert.NotEqual(Proto.Closings[CloseReason.Hostile], Pl.Closings[CloseReason.Hostile]);
        Assert.False(string.IsNullOrWhiteSpace(Pl.ConsentReask));
        Assert.False(string.IsNullOrWhiteSpace(Pl.AckFrustration));
        Assert.False(string.IsNullOrWhiteSpace(Pl.AckDeclined));
        Assert.NotEqual(Proto.Opening, Pl.Opening);
    }

    [Fact]
    public void The_polish_opening_discloses_the_ai_what_is_stored_that_it_can_be_stopped_and_asks_for_consent()
    {
        var o = Pl.Opening;

        Assert.Contains("AI", o);
        Assert.Contains("sztucznej inteligencji", o);
        Assert.Contains("nie człowiekiem", o);
        Assert.Contains("przechowywany", o);
        Assert.Contains("nie zawiera imion", o);
        Assert.Contains("nie jest powiązany z Pana/Pani kontem", o);
        Assert.Contains("maskowane", o);
        Assert.Contains("w każdej chwili", o);
        Assert.Contains("przerwać", o);
        Assert.Contains("nic z tej rozmowy nie zostanie zachowane", o);
        Assert.Contains("Czy zgadza się Pan/Pani kontynuować?", o);
    }

    [Fact]
    public void The_polish_topic_clarification_and_redirect_wording_passes_the_question_guard()
    {
        foreach (var t in Pl.Topics) Assert.Equal(GuardVerdict.Pass, QuestionGuard.Check(t.Question, TurnKind.Topic, Pii));
        Assert.Equal(GuardVerdict.Pass, QuestionGuard.Check(Pl.Clarification, TurnKind.Clarification, Pii));
        Assert.Equal(GuardVerdict.Pass, QuestionGuard.Check(Pl.RedirectNames, TurnKind.Redirect, Pii));
    }

    /// <summary>
    /// Known gap, owned by the Polish guard work (ADR-0075, Y2): the example rule only recognises English words, so a natural Polish probe is
    /// refused and the fixed probe is used instead. Pinned so that this test fails, and is updated, the day the guard learns Polish.
    /// </summary>
    [Fact]
    public void The_polish_probe_is_refused_by_the_english_only_example_rule_until_the_guard_has_polish_cues() =>
        Assert.Equal("probe_without_example", QuestionGuard.Check(Pl.Probe, TurnKind.Probe, Pii).Reason);

    [Fact]
    public void For_pl_returns_the_polish_protocol_and_for_en_returns_the_current_english_one_unchanged()
    {
        Assert.Same(Pl, InterviewProtocol.For("pl"));
        Assert.Same(InterviewProtocol.Current, InterviewProtocol.For("en"));
        Assert.Equal("en", InterviewProtocol.Current.Language);
        Assert.Equal("1.1", InterviewProtocol.Current.ProtocolVersion);
        Assert.StartsWith("Hello, and thank you", InterviewProtocol.Current.Opening);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("")]
    [InlineData("PL")]
    [InlineData("de")]
    [InlineData("pl-PL")]
    public void For_rejects_every_other_language(string language) =>
        Assert.Throws<ArgumentException>(() => InterviewProtocol.For(language));

    [Fact]
    public void For_rejects_null() =>
        Assert.Throws<ArgumentException>(() => InterviewProtocol.For(null!));

    [Fact]
    public void The_polish_instruction_names_polish_so_the_model_asks_in_polish()
    {
        var interviewer = Prompts.InterviewerSystem(Pl);
        var prober = Prompts.ProberSystem(Pl);

        Assert.Contains("in Polish", interviewer);
        Assert.Contains("in Polish", prober);
        Assert.Contains("in English", Prompts.InterviewerSystem(Proto));
    }
}
