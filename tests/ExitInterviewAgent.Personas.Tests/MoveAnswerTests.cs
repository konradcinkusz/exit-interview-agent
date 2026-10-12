using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Personas;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Personas.Tests;

/// <summary>Protocol 1.3 (WP6): a persona answers a move from its own answers, and falls back to its topic answers when it has none.</summary>
public class MoveAnswerTests
{
    /// <summary>A persona always has a reply to a question (the reply is null only when the interviewee has left), so the null is asserted away here.</summary>
    private static async Task<string> Ask(PersonaInterviewee who, TurnKind kind, Topic? topic, Move? move) =>
        (await who.ReplyAsync(new IntervieweeTurn(kind, topic, "question", move), CancellationToken.None))!;

    [Fact]
    public async Task A_warm_up_move_is_answered_from_the_persona_s_warm_up_answers()
    {
        var p = PersonaCatalog.Get("pay-first-pl");

        var reply = await Ask(new PersonaInterviewee(p, 1), TurnKind.Topic, null, Move.Warmup);

        Assert.Contains(reply, p.Responses.Warmup!);
    }

    [Fact]
    public async Task A_rating_move_is_answered_with_the_digit_for_its_topic()
    {
        var p = PersonaCatalog.Get("pay-first-pl");

        var reply = await Ask(new PersonaInterviewee(p, 1), TurnKind.Topic, Topic.Onboarding, Move.Scale);

        Assert.Equal("3", reply);
    }

    [Fact]
    public async Task A_rating_move_on_a_topic_without_a_rating_answer_falls_back_to_the_topic_answers()
    {
        var p = PersonaCatalog.Get("pay-first-pl");

        var reply = await Ask(new PersonaInterviewee(p, 1), TurnKind.Topic, Topic.ReasonForLeaving, Move.Scale);

        Assert.Contains(reply, p.Responses.Topics["reason_for_leaving"]);
    }

    [Fact]
    public async Task A_move_the_persona_has_no_answers_for_is_answered_by_its_kind_as_before()
    {
        var talkative = PersonaCatalog.Get("talkative");

        var withMove = await Ask(new PersonaInterviewee(talkative, 7), TurnKind.Topic, Topic.Culture, Move.Retention);
        var withoutMove = await Ask(new PersonaInterviewee(talkative, 7), TurnKind.Topic, Topic.Culture, null);

        Assert.Equal(withoutMove, withMove);
        Assert.Contains(withMove, talkative.Responses.Topics["culture"]);
    }

    [Fact]
    public async Task A_warm_up_move_without_warm_up_answers_gets_the_persona_s_fallback_because_it_has_no_topic()
    {
        var talkative = PersonaCatalog.Get("talkative");

        var reply = await Ask(new PersonaInterviewee(talkative, 3), TurnKind.Topic, null, Move.Warmup);

        Assert.Contains(reply, talkative.Responses.Fallback);
    }

    [Fact]
    public async Task The_same_seed_and_the_same_moves_give_the_same_interview()
    {
        var p = PersonaCatalog.Get("pay-first-pl");
        var moves = new[] { Move.Warmup, Move.Open, Move.Scale, Move.Retention, Move.AnythingElse };

        var first = new List<string>();
        var second = new List<string>();
        var a = new PersonaInterviewee(p, 11);
        var b = new PersonaInterviewee(p, 11);
        foreach (var m in moves)
        {
            first.Add(await Ask(a, TurnKind.Topic, Topic.Management, m));
            second.Add(await Ask(b, TurnKind.Topic, Topic.Management, m));
        }

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Without_moves_a_persona_with_the_new_answers_still_answers_every_topic_as_before()
    {
        var p = PersonaCatalog.Get("pay-first-pl");

        var reply = await Ask(new PersonaInterviewee(p, 5), TurnKind.Topic, Topic.PayVsPromises, null);

        Assert.Contains(reply, p.Responses.Topics["pay_vs_promises"]);
    }

    [Fact]
    public async Task A_rating_reply_is_the_digit_for_its_topic_and_never_a_topic_answer()
    {
        // The mutation this catches: a rating move answered from the topic's prose instead of its digit.
        var p = PersonaCatalog.Get("pay-first-pl");

        var reply = await Ask(new PersonaInterviewee(p, 1), TurnKind.Topic, Topic.Culture, Move.Scale);

        Assert.NotEqual(p.Responses.Topics["culture"][0], reply);
        Assert.Equal("4", reply);
    }
}
