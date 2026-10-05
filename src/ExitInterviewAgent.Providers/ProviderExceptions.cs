using ExitInterviewAgent.Agent.Roles;

namespace ExitInterviewAgent.Providers;

/// <summary>Why a provider call failed. Controlled vocabulary: it appears in metrics and spans as <c>error.type</c>.</summary>
public enum ProviderFailureKind
{
    Authentication,
    RateLimited,
    ServerError,
    BadRequest,
    Timeout,
    Network,
    InvalidResponse,
    BudgetExceeded,
    Unavailable,
}

/// <summary>
/// A provider call failed. By construction the message is built from <see cref="Kind"/>, an HTTP status and an attempt
/// count: never from a provider message, a header, a URL, a prompt or a response, all of which can echo content or secrets.
/// </summary>
public sealed class ProviderException : Exception, IModelFailure
{
    public ProviderException(ProviderFailureKind kind, int? statusCode = null, int attempts = 0, string? detail = null)
        : base(Describe(kind, statusCode, attempts, detail))
    {
        Kind = kind;
        StatusCode = statusCode;
        Attempts = attempts;
    }

    public ProviderFailureKind Kind { get; }

    public int? StatusCode { get; }

    public int Attempts { get; }

    public string FailureCode => Kind switch
    {
        ProviderFailureKind.Authentication => "provider.auth_failed",
        ProviderFailureKind.RateLimited => "provider.rate_limited",
        ProviderFailureKind.ServerError => "provider.server_error",
        ProviderFailureKind.BadRequest => "provider.bad_request",
        ProviderFailureKind.Timeout => "provider.timeout",
        ProviderFailureKind.Network => "provider.network",
        ProviderFailureKind.InvalidResponse => "provider.invalid_response",
        ProviderFailureKind.BudgetExceeded => "provider.budget_exceeded",
        _ => "provider.unavailable",
    };

    /// <summary>Retrying the interview call cannot help: bad credentials, a rejected request, a spent budget, a provider that keeps failing.</summary>
    public bool IsFatal => Kind is ProviderFailureKind.Authentication or ProviderFailureKind.BadRequest or ProviderFailureKind.BudgetExceeded or ProviderFailureKind.Unavailable;

    private static string Describe(ProviderFailureKind kind, int? status, int attempts, string? detail)
    {
        var http = status is { } s and > 0 ? $" (HTTP {s})" : string.Empty;
        var tries = attempts > 1 ? $" after {attempts} attempts" : string.Empty;
        return kind switch
        {
            ProviderFailureKind.Authentication => $"The provider rejected the credentials{http}. Check the API key for this provider.",
            ProviderFailureKind.RateLimited => $"The provider is rate limiting requests{http}{tries}.",
            ProviderFailureKind.ServerError => $"The provider reported a server error{http}{tries}.",
            ProviderFailureKind.BadRequest when status is >= 300 and < 400 => $"The provider answered with a redirect{http}. Redirects are refused so that credentials are never forwarded to another host; check the base URL.",
            ProviderFailureKind.BadRequest when status == 404 => $"The provider answered 'not found'{http}. Check the model name and the base URL.",
            ProviderFailureKind.BadRequest => $"The provider rejected the request{http}. Check the model name and the base URL.",
            ProviderFailureKind.Timeout => $"The provider did not answer in time{tries}.",
            ProviderFailureKind.Network => $"The provider could not be reached{tries}.",
            ProviderFailureKind.InvalidResponse => $"The provider returned a response this program could not use{(detail is null ? string.Empty : $" ({detail})")}.",
            ProviderFailureKind.BudgetExceeded => $"The interview budget was reached{(detail is null ? string.Empty : $" ({detail})")}; no further model calls were made.",
            _ => $"The provider failed on several consecutive calls{tries}; the interview was stopped.",
        };
    }
}

/// <summary>A configuration problem. The message names the setting, never its value.</summary>
public class ProviderConfigurationException(string message) : Exception(message);

/// <summary>The user asked for a backend this project deliberately does not offer; the message says why and points to the README.</summary>
public sealed class UnsupportedProviderException(string message) : ProviderConfigurationException(message);

/// <summary>The user supplied a credential this project refuses to send (a Claude subscription token).</summary>
public sealed class UnsupportedCredentialException(string message) : ProviderConfigurationException(message);
