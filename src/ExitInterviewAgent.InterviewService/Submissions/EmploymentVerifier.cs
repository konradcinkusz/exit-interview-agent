using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>What a verifier concluded when it could be consulted.</summary>
public enum EmploymentCheck
{
    Unverified,
    Verified,
}

/// <summary>The verifier cannot be consulted. The submission degrades to <c>Unchecked</c> (P8); it does not fail.</summary>
public sealed class EmploymentVerifierUnavailableException(string message) : Exception(message);

/// <summary>
/// Employer verification seam (brief section 2: an interface and a mock; real verification is an open problem, OP-1).
/// The record is NOT passed in: the verifier learns an account and an employer claim, nothing the person said. A real
/// implementation necessarily links an account to an employer inside the verifier, which is the link the rest of the
/// design avoids; that tension is part of OP-1.
/// </summary>
public interface IEmploymentVerifier
{
    Task<EmploymentCheck> VerifyAsync(string subject, string employerRef, CancellationToken ct);
}

/// <summary>Answers what configuration says. It verifies nothing; <c>/health</c> says so.</summary>
public sealed class MockEmploymentVerifier(IOptions<SubmissionOptions> options) : IEmploymentVerifier
{
    public const string Verified = "verified";
    public const string Unverified = "unverified";
    public const string Unavailable = "unavailable";

    public Task<EmploymentCheck> VerifyAsync(string subject, string employerRef, CancellationToken ct) =>
        options.Value.Verification.MockMode.ToLowerInvariant() switch
        {
            Verified => Task.FromResult(EmploymentCheck.Verified),
            Unavailable => throw new EmploymentVerifierUnavailableException("mock verifier configured as unavailable"),
            _ => Task.FromResult(EmploymentCheck.Unverified),
        };
}
