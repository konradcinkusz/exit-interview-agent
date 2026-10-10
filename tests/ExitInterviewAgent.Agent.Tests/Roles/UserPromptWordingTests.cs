using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Tests.Roles;

/// <summary>
/// A real run showed every question word for word as the protocol's fixed text: the user message told the model "do not add content",
/// which contradicted the system rule to restate the last reply. The user messages must ask for the restating sentence.
/// </summary>
public class UserPromptWordingTests
{
    private static QuestionRequest Request(TurnKind kind) => new(kind, Topic.Management, "SEED-TEXT", new Transcript(), kind == TurnKind.DeepProbe ? DeepFocus.WhatHappened : null);

    [Theory]
    [InlineData(TurnKind.Topic)]
    public void The_interviewer_message_asks_for_a_restating_sentence_and_does_not_forbid_content(TurnKind kind)
    {
        var text = Prompts.InterviewerUser(Request(kind), "nonce");

        Assert.Contains("restate", text);
        Assert.Contains("SEED-TEXT", text);
        Assert.DoesNotContain("do not add content", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TurnKind.Probe)]
    [InlineData(TurnKind.DeepProbe)]
    public void The_prober_message_asks_for_a_restating_sentence(TurnKind kind)
    {
        var text = Prompts.ProberUser(Request(kind), "nonce");

        Assert.Contains("restat", text);
        Assert.Contains("SEED-TEXT", text);
    }
}
