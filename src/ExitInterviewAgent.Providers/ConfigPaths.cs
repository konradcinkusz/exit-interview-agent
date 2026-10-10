namespace ExitInterviewAgent.Providers;

/// <summary>Names of every environment variable this project reads. One list, so the README and a test can check it.</summary>
public static class EnvVars
{
    public const string Provider = "EXIT_INTERVIEW_PROVIDER";
    public const string Model = "EXIT_INTERVIEW_MODEL";
    public const string BaseUrl = "EXIT_INTERVIEW_BASE_URL";
    public const string ApiKeyEnv = "EXIT_INTERVIEW_API_KEY_ENV";
    public const string Config = "EXIT_INTERVIEW_CONFIG";
    public const string ConfigDir = "EXIT_INTERVIEW_CONFIG_DIR";
    public const string TimeoutSeconds = "EXIT_INTERVIEW_TIMEOUT_SECONDS";
    public const string MaxRetries = "EXIT_INTERVIEW_MAX_RETRIES";
    public const string MaxTokens = "EXIT_INTERVIEW_MAX_TOKENS";
    public const string PriceInput = "EXIT_INTERVIEW_PRICE_INPUT_PER_MTOK";
    public const string PriceOutput = "EXIT_INTERVIEW_PRICE_OUTPUT_PER_MTOK";
    public const string MaxCost = "EXIT_INTERVIEW_MAX_COST";
    public const string NumCtx = "EXIT_INTERVIEW_NUM_CTX";

    /// <summary>
    /// Anthropic's own variable for a key that is not scoped to a workspace: the API then needs the workspace id in the
    /// <c>anthropic-workspace-id</c> header. An id is not a secret; it is never sent to another provider.
    /// </summary>
    public const string AnthropicWorkspaceId = "ANTHROPIC_WORKSPACE_ID";

    public static IReadOnlyList<string> Own { get; } =
        [Provider, Model, BaseUrl, ApiKeyEnv, Config, ConfigDir, TimeoutSeconds, MaxRetries, MaxTokens, PriceInput, PriceOutput, MaxCost, NumCtx];
}

/// <summary>Where the user's config file and the disclosure-confirmation preference live. Both can be deleted at any time.</summary>
public static class ConfigPaths
{
    public const string ConfigFileName = "config.json";
    public const string ConfirmationsFileName = "confirmations.json";

    public static string Directory(Func<string, string?> env)
    {
        if (env(EnvVars.ConfigDir) is { Length: > 0 } explicitDir) return explicitDir;
        if (env("XDG_CONFIG_HOME") is { Length: > 0 } xdg) return Path.Combine(xdg, "exit-interview");
        if (OperatingSystem.IsWindows() && env("APPDATA") is { Length: > 0 } appData) return Path.Combine(appData, "exit-interview");
        var home = env("HOME") is { Length: > 0 } h ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "exit-interview");
    }

    public static string ConfigFile(Func<string, string?> env) =>
        env(EnvVars.Config) is { Length: > 0 } explicitFile ? explicitFile : Path.Combine(Directory(env), ConfigFileName);

    public static string ConfirmationsFile(Func<string, string?> env) => Path.Combine(Directory(env), ConfirmationsFileName);
}
