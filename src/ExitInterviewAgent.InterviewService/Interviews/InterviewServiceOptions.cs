namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>
/// <c>Interviews</c> section. <see cref="Enabled"/> is the emergency switch (false refuses new sessions with 503). Without a
/// <see cref="Provider"/> the service also answers 503: no interview runs on a guessed model. The key is never in this file: it
/// is read from the environment variable named by <see cref="ApiKeyEnv"/> (or the provider's default one), as the CLI does.
/// </summary>
public sealed class InterviewServiceOptions
{
    public const string SectionName = "Interviews";

    public bool Enabled { get; set; } = true;

    /// <summary>A provider id from the provider catalog (for example <c>anthropic</c>), or <c>mock</c> for the scripted model.</summary>
    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>The NAME of the environment variable that holds the key. Never the key itself.</summary>
    public string? ApiKeyEnv { get; set; }

    /// <summary>When true, a start needs a credit from the ledger (<see cref="ICreditGate"/>, ADR-0077). False (development and tests) makes starts free.</summary>
    public bool RequireCredit { get; set; }

    /// <summary>A session that receives no request for this long is wiped.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>A finished session stays readable this long, then it is wiped.</summary>
    public TimeSpan ResultTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a request waits for the next interviewer turn before the session fails (the reply is not lost, the session is).</summary>
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
