namespace ExitInterviewAgent.Providers;

/// <summary>
/// Refuses credentials this project must not send. What is checked is only what Anthropic's own documentation states
/// (https://code.claude.com/docs/en/authentication, read 2026-10-05): <c>CLAUDE_CODE_OAUTH_TOKEN</c> holds an OAuth token
/// from <c>claude setup-token</c> that "authenticates with your Claude subscription", and
/// <c>CLAUDE_CODE_OAUTH_REFRESH_TOKEN</c> is its refresh token. The documentation does not describe a recognisable prefix or
/// format for subscription tokens, so <b>no value-based heuristic exists here</b>: a subscription token pasted into
/// <c>ANTHROPIC_API_KEY</c> cannot be recognised, and the API will reject it (README documents the limit).
/// </summary>
public static class CredentialPolicy
{
    public static readonly IReadOnlySet<string> SubscriptionTokenVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_OAUTH_REFRESH_TOKEN",
    };

    /// <summary>Throws when <paramref name="variableName"/> is a documented subscription-token variable.</summary>
    public static void EnsureAllowedKeySource(string variableName)
    {
        if (SubscriptionTokenVariables.Contains(variableName.Trim()))
            throw new UnsupportedCredentialException(
                $"{variableName} holds a Claude subscription (Free, Pro or Max) credential, which this project refuses to use. Anthropic's documentation says third-party developers may not route requests through "
                + $"subscription plan credentials. Create an API key in the Anthropic Console and put it in ANTHROPIC_API_KEY. See {ProviderCatalog.ReadmeAnchor}.");
    }

    /// <summary>A hint for the missing-key message: a subscription token variable is set but is (rightly) not read.</summary>
    public static bool SubscriptionTokenPresent(Func<string, string?> env) => SubscriptionTokenVariables.Any(v => !string.IsNullOrEmpty(env(v)));
}
