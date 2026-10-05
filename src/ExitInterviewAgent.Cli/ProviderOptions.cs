using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli;

/// <summary>The provider flags shared by <c>interview</c> and <c>providers ping</c>, and how they become settings. A key has no flag: it is read from an environment variable.</summary>
internal static class ProviderOptions
{
    public static readonly IReadOnlySet<string> ValueFlags = new HashSet<string>
    {
        "--provider", "--model", "--base-url", "--api-key-env", "--config", "--max-tokens", "--timeout-seconds", "--max-retries", "--max-cost", "--price-in", "--price-out", "--num-ctx",
    };

    public static ProviderCliOptions From(Flags f) => new()
    {
        Provider = f["--provider"],
        Model = f["--model"],
        BaseUrl = f["--base-url"],
        ApiKeyEnv = f["--api-key-env"],
        MaxTokens = f["--max-tokens"],
        TimeoutSeconds = f["--timeout-seconds"],
        MaxRetries = f["--max-retries"],
        MaxCost = f["--max-cost"],
        PriceInput = f["--price-in"],
        PriceOutput = f["--price-out"],
        NumCtx = f["--num-ctx"],
    };

    public static ResolvedProvider Resolve(Flags f, CliHost host)
    {
        var path = f["--config"] ?? ConfigPaths.ConfigFile(host.Env);
        // An explicit --config that does not exist is a mistake worth reporting; the default location is optional.
        if (f["--config"] is { } explicitPath && !File.Exists(explicitPath)) throw new ArgumentException("The file given with --config does not exist.");
        return ProviderConfigResolver.Resolve(From(f), host.Env, UserConfig.Load(path));
    }
}
