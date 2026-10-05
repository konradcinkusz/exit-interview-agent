namespace ExitInterviewAgent.Agent.Protocol;

/// <summary>What an interviewer turn is. The interviewee side (human, simulator) may use it as a cue; the text is what a person would read.</summary>
public enum TurnKind { Opening, ConsentReask, Topic, Probe, Clarification, Redirect, Close, Stop }

public enum Speaker { Interviewer, Interviewee }

/// <summary>Why the dialogue ended with the interviewee still consenting (a record is prepared).</summary>
public enum CloseReason { AllTopicsCovered, Unresponsive, Hostile, BudgetExhausted }

/// <summary>Why the dialogue ended without a record.</summary>
public enum StopReason { ConsentWithdrawn, ConsentDeclined, ConsentUnclear, Disconnected }
