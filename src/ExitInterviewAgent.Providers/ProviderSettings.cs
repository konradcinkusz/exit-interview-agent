using System.Globalization;
using System.Net;

namespace ExitInterviewAgent.Providers;

/// <summary>Timeouts and retries for one provider. Defaults are starting values, not measured optima.</summary>
public sealed record ResilienceOptions
{
    /// <summary>Longest wait for one HTTP attempt.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Overrides <see cref="OverallTimeout"/>; by default every permitted attempt and wait fits.</summary>
    public TimeSpan? OverallTimeoutOverride { get; init; }

    /// <summary>Longest a whole model call may take, retries and waits included.</summary>
    public TimeSpan OverallTimeout => OverallTimeoutOverride ?? RequestTimeout * (MaxRetries + 1) + MaxDelay * MaxRetries;

    /// <summary>Retries after the first attempt, on 408, 429, 5xx, timeouts and connection errors only.</summary>
    public int MaxRetries { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Cap on one wait. A <c>Retry-After</c> larger than this ends the retries instead of stalling the interview.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>This many model calls in a row that fail after their retries stop the interview.</summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    public void Validate()
    {
        if (RequestTimeout <= TimeSpan.Zero || OverallTimeout <= TimeSpan.Zero) throw new ProviderConfigurationException("Timeouts must be positive.");
        if (MaxRetries is < 0 or > 10) throw new ProviderConfigurationException("Max retries must be between 0 and 10.");
        if (BaseDelay < TimeSpan.Zero || MaxDelay < BaseDelay) throw new ProviderConfigurationException("Retry delays are inconsistent.");
        if (MaxConsecutiveFailures < 1) throw new ProviderConfigurationException("Max consecutive failures must be at least 1.");
    }
}

/// <summary>
/// Optional, user-supplied prices per million tokens. This project hardcodes no price (a price list cannot be verified
/// from here and goes stale): cost is tokens times what the user configures, or it is not computed.
/// </summary>
public sealed record PriceConfig(decimal InputPerMillionTokens, decimal OutputPerMillionTokens, string Currency = "USD")
{
    public decimal Cost(long inputTokens, long outputTokens) =>
        (inputTokens * InputPerMillionTokens + outputTokens * OutputPerMillionTokens) / 1_000_000m;

    public void Validate()
    {
        if (InputPerMillionTokens < 0 || OutputPerMillionTokens < 0) throw new ProviderConfigurationException("Prices must not be negative.");
        if (Currency.Length is < 1 or > 8 || !Currency.All(char.IsLetter)) throw new ProviderConfigurationException("The currency must be a short code of letters.");
    }
}

/// <summary>Everything needed to build one provider client. The API key is a <see cref="SecretString"/>; this record prints without it.</summary>
public sealed record ProviderSettings
{
    public required ProviderKind Kind { get; init; }

    public required string Model { get; init; }

    public required Uri BaseUrl { get; init; }

    public SecretString? ApiKey { get; init; }

    public ResilienceOptions Resilience { get; init; } = new();

    public PriceConfig? Prices { get; init; }

    /// <summary>Overrides the protocol's token budget (the graceful one); the hard cap is derived from it.</summary>
    public long? MaxTokens { get; init; }

    /// <summary>Hard cost ceiling; needs <see cref="Prices"/>.</summary>
    public decimal? MaxCost { get; init; }

    /// <summary>Ollama context window (<c>num_ctx</c>). Ollama's own default can silently truncate a long transcript.</summary>
    public int? NumCtx { get; init; }

    public ProviderInfo Info => ProviderCatalog.Get(Kind);

    /// <summary>Host and port only, safe to show. Never the path or anything after it.</summary>
    public string Endpoint => BaseUrlPolicy.Display(BaseUrl);

    /// <summary>True when the model runs on this machine: Ollama or a gateway at a loopback address, or the mock.</summary>
    public bool IsLocal => Kind == ProviderKind.Mock || BaseUrlPolicy.IsLoopback(BaseUrl);

    public ProviderSettings Validated()
    {
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || Model.Any(char.IsControl))
            throw new ProviderConfigurationException("A model name is required (no control characters, at most 200 characters).");
        BaseUrlPolicy.Validate(BaseUrl.OriginalString, keyed: ApiKey is not null);
        if (Kind == ProviderKind.Anthropic && ApiKey is null) throw new ProviderConfigurationException("The Anthropic provider needs an API key in the environment (ANTHROPIC_API_KEY, or the variable named by --api-key-env).");
        Resilience.Validate();
        Prices?.Validate();
        if (MaxTokens is <= 0) throw new ProviderConfigurationException("Max tokens must be positive.");
        if (MaxCost is { } c && (c <= 0 || Prices is null)) throw new ProviderConfigurationException("A cost ceiling needs prices (price-in and price-out per million tokens); this project hardcodes none.");
        if (NumCtx is { } n && n is < 512 or > 1_048_576) throw new ProviderConfigurationException("num_ctx is out of range.");
        return this;
    }
}

/// <summary>What may and may not be a base URL. A URL is a place a key and a transcript are sent to, so it is checked before anything is.</summary>
public static class BaseUrlPolicy
{
    /// <summary>
    /// Absolute http(s) URL; no embedded credentials, no query, no fragment; https unless the host is a loopback address
    /// or no key is sent. Error messages never repeat the URL (it may contain a secret).
    /// </summary>
    public static Uri Validate(string? raw, bool keyed)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ProviderConfigurationException("The base URL is not a valid absolute http(s) URL.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ProviderConfigurationException("The base URL contains embedded credentials, which are refused. Put the key in an environment variable.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ProviderConfigurationException("The base URL must not carry a query string or fragment.");
        if (keyed && uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri))
            throw new ProviderConfigurationException("A base URL that is not on this machine must use https when an API key is sent.");
        if (HostIsCopilot(uri))
            throw new UnsupportedProviderException(ProviderCatalog.CopilotReason);
        return uri;
    }

    public static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);

    public static string Display(Uri uri) => uri.IsDefaultPort ? uri.Host : string.Create(CultureInfo.InvariantCulture, $"{uri.Host}:{uri.Port}");

    /// <summary>
    /// Copilot's service host. The hostname comes from general knowledge and was not verified from GitHub's documentation
    /// (docs.github.com was not reachable); the guard exists so that "no Copilot adapter" cannot be bypassed with a base URL.
    /// </summary>
    private static bool HostIsCopilot(Uri uri) =>
        uri.Host.Equals("githubcopilot.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".githubcopilot.com", StringComparison.OrdinalIgnoreCase);
}
