using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Providers;

/// <summary>
/// The user's config file: the lowest-precedence source (flags, then environment, then this file). It never holds a key:
/// a literal key in a file ends up in backups, dotfile repositories and screen shares, so the file names the environment
/// variable that holds it (<c>apiKeyEnv</c>) and a file with a key property is refused.
/// </summary>
public sealed record UserConfig(
    string? Provider,
    string? Model,
    string? BaseUrl,
    string? ApiKeyEnv,
    double? TimeoutSeconds,
    int? MaxRetries,
    long? MaxTokens,
    decimal? MaxCost,
    int? NumCtx,
    decimal? PriceInputPerMillionTokens,
    decimal? PriceOutputPerMillionTokens,
    string? Currency)
{
    private const int MaxBytes = 64 * 1024;

    private static readonly string[] KeyLikeProperties = ["apikey", "api_key", "key", "token", "secret", "password", "authorization", "authtoken", "oauthtoken"];

    public static readonly UserConfig Empty = new(null, null, null, null, null, null, null, null, null, null, null, null);

    /// <summary>Reads <paramref name="path"/>; a missing file is <see cref="Empty"/> (the file is optional). Errors name the problem, never the content.</summary>
    public static UserConfig Load(string path)
    {
        if (!File.Exists(path)) return Empty;
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new ProviderConfigurationException("The config file is larger than 64 KiB.");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 8 });
            return Parse(doc.RootElement);
        }
        catch (JsonException)
        {
            throw new ProviderConfigurationException("The config file is not valid JSON.");
        }
    }

    public static UserConfig Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ProviderConfigurationException("The config file must contain a JSON object.");
        string? provider = null, model = null, baseUrl = null, apiKeyEnv = null, currency = null;
        double? timeout = null;
        int? retries = null, numCtx = null;
        long? maxTokens = null;
        decimal? maxCost = null, priceIn = null, priceOut = null;

        foreach (var p in root.EnumerateObject())
        {
            var name = p.Name;
            if (KeyLikeProperties.Contains(name.ToLowerInvariant()))
                throw new ProviderConfigurationException("The config file must not contain a key or token. Put the key in an environment variable and name it with \"apiKeyEnv\".");
            switch (name)
            {
                case "provider": provider = Str(p); break;
                case "model": model = Str(p); break;
                case "baseUrl": baseUrl = Str(p); break;
                case "apiKeyEnv": apiKeyEnv = Str(p); break;
                case "timeoutSeconds": timeout = Num<double>(p); break;
                case "maxRetries": retries = (int?)Num<long>(p); break;
                case "maxTokens": maxTokens = Num<long>(p); break;
                case "maxCost": maxCost = Num<decimal>(p); break;
                case "numCtx": numCtx = (int?)Num<long>(p); break;
                case "prices":
                    if (p.Value.ValueKind != JsonValueKind.Object) throw Bad("prices");
                    foreach (var q in p.Value.EnumerateObject())
                        switch (q.Name)
                        {
                            case "inputPerMillionTokens": priceIn = Num<decimal>(q); break;
                            case "outputPerMillionTokens": priceOut = Num<decimal>(q); break;
                            case "currency": currency = Str(q); break;
                            default: throw Unknown("prices." + SafeName(q.Name));
                        }
                    break;
                default: throw Unknown(SafeName(name));
            }
        }

        return new UserConfig(provider, model, baseUrl, apiKeyEnv, timeout, retries, maxTokens, maxCost, numCtx, priceIn, priceOut, currency);
    }

    private static string? Str(JsonProperty p) => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : throw Bad(p.Name);

    private static T? Num<T>(JsonProperty p) where T : struct, IParsable<T>
    {
        if (p.Value.ValueKind != JsonValueKind.Number) throw Bad(p.Name);
        return T.TryParse(p.Value.GetRawText(), CultureInfo.InvariantCulture, out var v) ? v : throw Bad(p.Name);
    }

    private static ProviderConfigurationException Bad(string name) => new($"The config file property '{SafeName(name)}' has the wrong type or an invalid value.");

    private static ProviderConfigurationException Unknown(string name) => new($"The config file has an unknown property '{name}'. Known: provider, model, baseUrl, apiKeyEnv, timeoutSeconds, maxRetries, maxTokens, maxCost, numCtx, prices.");

    /// <summary>A property name is shown back only if it looks like a name, so a pasted secret is never echoed.</summary>
    private static string SafeName(string name) => Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_.]{0,31}$") ? name : "(unprintable)";
}
