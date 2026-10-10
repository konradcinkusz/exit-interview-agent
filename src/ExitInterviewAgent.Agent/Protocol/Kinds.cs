namespace ExitInterviewAgent.Agent.Protocol;

/// <summary>What an interviewer turn is. The interviewee side (human, simulator) may use it as a cue; the text is what a person would read.</summary>
public enum TurnKind { Opening, ConsentReask, Topic, Probe, Clarification, Redirect, DeepProbe, Close, Stop }

public enum Speaker { Interviewer, Interviewee }

/// <summary>Why the dialogue ended with the interviewee still consenting (a record is prepared).</summary>
public enum CloseReason { AllTopicsCovered, Unresponsive, Hostile, BudgetExhausted }

/// <summary>Why the dialogue ended without a record.</summary>
public enum StopReason { ConsentWithdrawn, ConsentDeclined, ConsentUnclear, Disconnected }

/// <summary>
/// The fixed deepening menu (ADR-0075), in the order the deepening asks it. A serious account is followed up on the first element
/// that the reply has not already described. The order and the element names are code; the wording is the protocol's
/// <c>deepeningSeeds</c>, index for index.
/// </summary>
public enum DeepFocus { WhatHappened, WhenHowOften, WhoByRole, WhatTheyDidAndResponse, HowItEndedAndMeaning }
