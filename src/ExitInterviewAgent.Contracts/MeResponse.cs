namespace ExitInterviewAgent.Contracts;

/// <summary>
/// The caller's account, as the service sees it. The account has no link to any employer
/// (PROJECT-BRIEF §3.1): the subject is all there is.
/// </summary>
public sealed record MeResponse(string Subject);
