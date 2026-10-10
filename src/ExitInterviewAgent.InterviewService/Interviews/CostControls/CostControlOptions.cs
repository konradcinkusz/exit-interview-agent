namespace ExitInterviewAgent.InterviewService.Interviews.CostControls;

/// <summary>
/// The cost-control keys of the <c>Interviews</c> section (web-app-plan §2, ADR-0078). They sit beside the keys of
/// <see cref="InterviewServiceOptions"/>, which owns the emergency switch and the provider. The numbers are placeholders
/// until W9 measures a real interview; the owner tunes them in configuration, not in code.
/// </summary>
public sealed class CostControlOptions
{
    /// <summary>
    /// A start needs an account whose email is verified (the <c>email_verified</c> claim is <c>true</c>). Production is true.
    /// Development and tests set it false; a true value with an authservice that does not send the claim refuses every start.
    /// </summary>
    public bool RequireVerifiedEmail { get; set; } = true;

    /// <summary>
    /// Starts admitted per UTC day, across all accounts, counted in memory. Reaching it answers 503 <c>interviews_disabled</c>
    /// until midnight UTC. Zero refuses every start (fail closed). A restart resets the count: this is a brake, not a ledger.
    /// </summary>
    public int MaxStartsPerDay { get; set; } = 20;

    public RateLimitOptions RateLimits { get; set; } = new();

    public sealed class RateLimitOptions
    {
        public int StartsPerAccountPerHour { get; set; } = 5;

        public int StartsPerIpPerHour { get; set; } = 20;

        public int RepliesPerAccountPerMinute { get; set; } = 30;

        public int RepliesPerIpPerMinute { get; set; } = 60;
    }
}
