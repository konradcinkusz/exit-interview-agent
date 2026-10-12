using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Roles;

/// <summary>
/// A real run showed every question word for word as the protocol's fixed text: the user message told the model "do not add content",
/// which contradicted the system rule to acknowledge. The user messages must never forbid what the system prompt allows. Prompts v2 (WP4)
/// moved the "how to write the turn" rules into the system prompt, so the user message now only frames the request.
/// </summary>
public class UserPromptWordingTests
{
    private static QuestionRequest Request(TurnKind kind) => new(kind, Topic.Management, "SEED-TEXT", new Transcript(), kind == TurnKind.DeepProbe ? DeepFocus.WhatHappened : null);

    [Theory]
    [InlineData(TurnKind.Topic)]
    public void The_interviewer_message_frames_the_turn_and_does_not_forbid_content(TurnKind kind)
    {
        var text = Prompts.InterviewerUser(Request(kind), "nonce", Helpers.Proto);

        Assert.Contains("SEED: SEED-TEXT", text);
        Assert.Contains("Write the next turn in English.", text);
        Assert.DoesNotContain("do not add content", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TurnKind.Probe)]
    [InlineData(TurnKind.DeepProbe)]
    public void The_prober_message_frames_the_turn_and_does_not_forbid_content(TurnKind kind)
    {
        var text = Prompts.ProberUser(Request(kind), "nonce", Helpers.Proto);

        Assert.Contains("SEED: SEED-TEXT", text);
        Assert.Contains("Write the next turn in English.", text);
        Assert.DoesNotContain("do not add content", text, StringComparison.OrdinalIgnoreCase);
    }
}
