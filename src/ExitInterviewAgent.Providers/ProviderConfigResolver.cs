using System.Globalization;
using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Providers;

public enum ConfigSource { Default, ConfigFile, Environment, CommandLine }

/// <summary>Values from command-line flags. Numbers stay text until the resolver parses them, so every source fails the same way.</summary>
public sealed record ProviderCliOptions
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? BaseUrl { get; init; }
    public string? ApiKeyEnv { get; init; }
    public string? TimeoutSeconds { get; init; }
    public string? MaxRetries { get; init; }
    public string? MaxTokens { get; init; }
    public string? MaxCost { get; init; }
    public string? PriceInput { get; init; }
    public string? PriceOutput { get; init; }
    public string? NumCtx { get; init; }
}

/// <summary>The settings, where each came from (never the key itself), and the variable the key was read from.</summary>
public sealed record ResolvedProvider(ProviderSettings Settings, IReadOnlyDictionary<string, ConfigSource> Sources, string? KeyVariable);

/// <summary>What <c>exit-interview providers</c> shows: local facts only, no network.</summary>
public sealed record ProviderStatus(ProviderInfo Info, bool KeyPresent, string? Endpoint, string Note);

/// <summary>
/// Precedence, highest first: command-line flag, environment variable, user config file, built-in default. A key is read only
/// from an environment variable (by default the provider's own, or the one named by <c>--api-key-env</c> / <c>EXIT_INTERVIEW_API_KEY_ENV</c> / <c>apiKeyEnv</c>);
/// there is deliberately no <c>--api-key</c> flag (it would land in shell history and process listings).
/// </summary>
public static partial class ProviderConfigResolver
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex VariableName();

    public static ResolvedProvider Resolve(ProviderCliOptions cli, Func<string, string?> env, UserConfig? file = null)
    {
        file ??= UserConfig.Empty;
        var sources = new Dictionary<string, ConfigSource>();

        string? Pick(string key, string? flag, string? envValue, string? fileValue, string? fallback = null)
        {
            if (!string.IsNullOrWhiteSpace(flag)) { sources[key] = ConfigSource.CommandLine; return flag.Trim(); }
            if (!string.IsNullOrWhiteSpace(envValue)) { sources[key] = ConfigSource.Environment; return envValue.Trim(); }
            if (!string.IsNullOrWhiteSpace(fileValue)) { sources[key] = ConfigSource.ConfigFile; return fileValue.Trim(); }
            if (fallback is not null) sources[key] = ConfigSource.Default;
            return fallback;
        }

        var providerId = Pick("provider", cli.Provider, env(EnvVars.Provider), file.Provider)
            ?? throw new ProviderConfigurationException($"No provider selected. Use --provider (anthropic, openai-compatible, ollama), {EnvVars.Provider}, or the config file. Run 'exit-interview providers' to see what is configured.");
        var info = ProviderCatalog.Get(providerId);

        var model = Pick("model", cli.Model, env(EnvVars.Model), file.Model)
            ?? throw new ProviderConfigurationException($"No model selected. Use --model, {EnvVars.Model}, or the config file (this project hardcodes no model id).");

        var baseEnv = env(EnvVars.BaseUrl) is { Length: > 0 } generic ? generic : info.BaseUrlEnvVar is { } v ? env(v) : null;
        if (info.Kind == ProviderKind.Ollama && info.BaseUrlEnvVar is { } ollamaVar && env(ollamaVar) is { Length: > 0 } host && string.IsNullOrEmpty(env(EnvVars.BaseUrl))) baseEnv = NormalizeOllamaHost(host);
        var baseUrlText = info.Kind == ProviderKind.Mock ? "http://localhost/" : Pick("baseUrl", cli.BaseUrl, baseEnv, file.BaseUrl, info.DefaultBaseUrl);

        string? keyVariable = null;
        SecretString? key = null;
        var keyVarName = Pick("apiKeyEnv", cli.ApiKeyEnv, env(EnvVars.ApiKeyEnv), file.ApiKeyEnv);
        if (keyVarName is not null)
        {
            if (!VariableName().IsMatch(keyVarName)) throw new ProviderConfigurationException("The name given for the API key variable is not a valid environment variable name (give the NAME of the variable, never the key itself).");
            CredentialPolicy.EnsureAllowedKeySource(keyVarName);
            keyVariable = keyVarName;
            key = env(keyVarName) is { Length: > 0 } explicitValue ? new SecretString(explicitValue) : throw new ProviderConfigurationException($"The environment variable {keyVarName} is not set or is empty.");
        }
        else if (info.KeyEnvVar is { } defaultVar)
        {
            keyVariable = defaultVar;
            if (env(defaultVar) is { Length: > 0 } value) key = new SecretString(value);
            else sources.Remove("apiKeyEnv");
        }

        var uri = BaseUrlPolicy.Validate(baseUrlText, keyed: key is not null);

        if (info.Kind == ProviderKind.Anthropic && key is null)
            throw new ProviderConfigurationException(
                $"No API key: set {info.KeyEnvVar} (or name another variable with --api-key-env)."
                + (CredentialPolicy.SubscriptionTokenPresent(env) ? " A Claude subscription token is present in the environment; it is not read and is not supported." : string.Empty)
                + $" See {ProviderCatalog.ReadmeAnchor}.");
        if (info.Kind == ProviderKind.OpenAiCompatible && key is null && uri.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase))
            throw new ProviderConfigurationException($"No API key: set {info.KeyEnvVar}. (A key is optional only for a gateway you point to with --base-url.)");

        var timeout = ParseNumber<double>("timeoutSeconds", Pick("timeoutSeconds", cli.TimeoutSeconds, env(EnvVars.TimeoutSeconds), file.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture)));
        var retries = ParseNumber<int>("maxRetries", Pick("maxRetries", cli.MaxRetries, env(EnvVars.MaxRetries), file.MaxRetries?.ToString(CultureInfo.InvariantCulture)));
        var maxTokens = ParseNumber<long>("maxTokens", Pick("maxTokens", cli.MaxTokens, env(EnvVars.MaxTokens), file.MaxTokens?.ToString(CultureInfo.InvariantCulture)));
        var maxCost = ParseNumber<decimal>("maxCost", Pick("maxCost", cli.MaxCost, env(EnvVars.MaxCost), file.MaxCost?.ToString(CultureInfo.InvariantCulture)));
        var numCtx = ParseNumber<int>("numCtx", Pick("numCtx", cli.NumCtx, env(EnvVars.NumCtx), file.NumCtx?.ToString(CultureInfo.InvariantCulture)));
        var priceIn = ParseNumber<decimal>("priceInput", Pick("priceInput", cli.PriceInput, env(EnvVars.PriceInput), file.PriceInputPerMillionTokens?.ToString(CultureInfo.InvariantCulture)));
        var priceOut = ParseNumber<decimal>("priceOutput", Pick("priceOutput", cli.PriceOutput, env(EnvVars.PriceOutput), file.PriceOutputPerMillionTokens?.ToString(CultureInfo.InvariantCulture)));
        if ((priceIn is null) != (priceOut is null)) throw new ProviderConfigurationException("Give both the input and the output price per million tokens, or neither.");

        var resilience = new ResilienceOptions
        {
            RequestTimeout = timeout is { } t ? (t > 0 && t <= 3600 ? TimeSpan.FromSeconds(t) : throw new ProviderConfigurationException("timeoutSeconds must be between 0 and 3600.")) : new ResilienceOptions().RequestTimeout,
            MaxRetries = retries ?? new ResilienceOptions().MaxRetries,
        };

        var settings = new ProviderSettings
        {
            Kind = info.Kind,
            Model = model,
            BaseUrl = uri,
            ApiKey = key,
            Resilience = resilience,
            Prices = priceIn is { } pi && priceOut is { } po ? new PriceConfig(pi, po, file.Currency ?? "USD") : null,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            NumCtx = info.Kind == ProviderKind.Ollama ? numCtx : null,
        }.Validated();

        return new ResolvedProvider(settings, sources, keyVariable);
    }

    /// <summary>The local view for <c>exit-interview providers</c>: which providers can start, from what, without a network call.</summary>
    public static IReadOnlyList<ProviderStatus> Inspect(Func<string, string?> env)
    {
        var result = new List<ProviderStatus>();
        foreach (var info in ProviderCatalog.All)
        {
            var keyPresent = info.KeyEnvVar is { } v && !string.IsNullOrEmpty(env(v));
            string? endpoint = info.DefaultBaseUrl is null ? null : BaseUrlPolicy.Display(new Uri(info.DefaultBaseUrl));
            var note = info.Kind switch
            {
                ProviderKind.Anthropic => keyPresent ? $"{info.KeyEnvVar} is set" : $"{info.KeyEnvVar} is not set"
                    + (CredentialPolicy.SubscriptionTokenPresent(env) ? "; a Claude subscription token is present and is ignored (not supported)" : string.Empty),
                ProviderKind.OpenAiCompatible => keyPresent ? $"{info.KeyEnvVar} is set" : $"{info.KeyEnvVar} is not set (needed for api.openai.com; a gateway may need none)",
                ProviderKind.Ollama => "no key; needs a running server (not checked here)",
                _ => "always available; offline",
            };
            result.Add(new ProviderStatus(info, keyPresent, endpoint, note));
        }
        return result;
    }

    private static T? ParseNumber<T>(string name, string? text) where T : struct, IParsable<T> =>
        text is null ? null : T.TryParse(text, CultureInfo.InvariantCulture, out var v) ? v : throw new ProviderConfigurationException($"The value for {name} is not a number.");

    /// <summary><c>OLLAMA_HOST</c> may be a bare <c>host[:port]</c>; a bind-all address is not a place to connect to.</summary>
    private static string NormalizeOllamaHost(string host)
    {
        var h = host.Trim();
        if (!h.Contains("://", StringComparison.Ordinal)) h = "http://" + h;
        return h.Replace("//0.0.0.0", "//127.0.0.1", StringComparison.Ordinal);
    }
}
