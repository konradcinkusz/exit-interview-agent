using System.Globalization;
using ExitInterviewAgent.Providers;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview providers</c> lists what can be used and checks local configuration with no network call.
/// <c>providers ping</c> makes exactly one minimal live request, only when asked. <c>providers forget-confirmations</c> deletes the remembered disclosure confirmations.
/// </summary>
internal static class ProvidersCommand
{
    public static async Task<int> RunAsync(string[] args, CliHost host)
    {
        if (args.Length > 0 && args[0] == "ping") return await Ping(args[1..], host).ConfigureAwait(false);
        if (args.Length > 0 && args[0] == "forget-confirmations") return await Forget(host).ConfigureAwait(false);
        if (args.Length > 0) throw new ArgumentException("Usage: exit-interview providers [ping <provider flags> | forget-confirmations]");
        return await List(host).ConfigureAwait(false);
    }

    private static async Task<int> List(CliHost host)
    {
        var o = host.Out;
        await o.WriteLineAsync("Model providers (no network call is made by this command):").ConfigureAwait(false);
        await o.WriteLineAsync().ConfigureAwait(false);
        foreach (var s in ProviderConfigResolver.Inspect(host.Env))
        {
            await o.WriteLineAsync($"  {s.Info.Id,-18} {s.Info.DisplayName}").ConfigureAwait(false);
            await o.WriteLineAsync($"  {string.Empty,-18} {s.Note}{(s.Endpoint is null ? string.Empty : $"; default endpoint {s.Endpoint}")}").ConfigureAwait(false);
        }

        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("Not supported, on purpose:").ConfigureAwait(false);
        await o.WriteLineAsync("  Claude subscription (Free, Pro, Max) credentials: Anthropic's documentation says third parties may not route requests through them. Use an API key.").ConfigureAwait(false);
        await o.WriteLineAsync("  GitHub Copilot: its terms for use from a separate program could not be verified, so no adapter exists (a decision, not a finding that it is prohibited).").ConfigureAwait(false);
        await o.WriteLineAsync($"  See {ProviderCatalog.ReadmeAnchor}").ConfigureAwait(false);

        var configPath = ConfigPaths.ConfigFile(host.Env);
        var store = new ConfirmationStore(ConfigPaths.ConfirmationsFile(host.Env));
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync($"Config file: {configPath} ({(File.Exists(configPath) ? "found" : "not found; optional")})").ConfigureAwait(false);
        await o.WriteLineAsync($"Remembered disclosure confirmations: {store.Count} ({store.Path})").ConfigureAwait(false);
        await o.WriteLineAsync($"Environment variables read: {string.Join(", ", EnvVars.Own)}, and per provider: {string.Join(", ", ProviderCatalog.All.SelectMany(p => new[] { p.KeyEnvVar, p.BaseUrlEnvVar }).Where(v => v is not null).Distinct())}, and {EnvVars.AnthropicWorkspaceId} (Anthropic only, for a key not scoped to a workspace)").ConfigureAwait(false);

        await o.WriteLineAsync().ConfigureAwait(false);
        try
        {
            var r = ProviderConfigResolver.Resolve(new ProviderCliOptions(), host.Env, UserConfig.Load(configPath));
            var s = r.Settings;
            await o.WriteLineAsync($"Selected without flags: provider {s.Info.Id} (from {r.Sources["provider"]}), model {s.Model} (from {r.Sources["model"]}), endpoint {s.Endpoint}, key {(s.ApiKey is null ? "none" : $"from {r.KeyVariable}")}.").ConfigureAwait(false);
        }
        catch (ProviderConfigurationException e)
        {
            await o.WriteLineAsync("Nothing selected yet: " + e.Message).ConfigureAwait(false);
        }
        await o.WriteLineAsync("Test a provider with one minimal request: exit-interview providers ping --provider <p> --model <m>").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> Forget(CliHost host)
    {
        var store = new ConfirmationStore(ConfigPaths.ConfirmationsFile(host.Env));
        await host.Out.WriteLineAsync(store.Forget() ? $"Deleted {store.Path}." : "There were no remembered confirmations.").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> Ping(string[] args, CliHost host)
    {
        var flags = Flags.Parse(args, ProviderOptions.ValueFlags, new HashSet<string>());
        var settings = ProviderOptions.Resolve(flags, host).Settings;
        if (settings.Kind == ProviderKind.Mock)
        {
            await host.Out.WriteLineAsync("The mock model is offline: nothing to ping.").ConfigureAwait(false);
            return 0;
        }

        await host.Out.WriteLineAsync($"Sending one minimal request ('reply OK', no interview content, at most 8 output tokens) to {settings.Info.DisplayName} at {settings.Endpoint}, model {settings.Model}.").ConfigureAwait(false);
        using var client = ProviderChatClients.Create(settings, host.Runtime);
        try
        {
            var started = TimeProvider.System.GetTimestamp();
            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with the single word OK.")], new ChatOptions { MaxOutputTokens = 8, Temperature = 0 }, host.Cancellation).ConfigureAwait(false);
            var u = client.Budget.Snapshot();
            await host.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"ok: answered in {TimeProvider.System.GetElapsedTime(started).TotalSeconds:F1}s, tokens_in={u.InputTokens} tokens_out={u.OutputTokens}{(response.ModelId is { } m ? $", model reported: {ExitInterviewAgent.Providers.ProviderTelemetry.Label(m)}" : string.Empty)}")).ConfigureAwait(false);
            return 0;
        }
        catch (ProviderException e)
        {
            await host.Err.WriteLineAsync("ping failed: " + e.Message).ConfigureAwait(false);
            return InterviewCommand.Exit.ProviderFailed;
        }
        catch (OperationCanceledException)
        {
            await host.Err.WriteLineAsync("ping cancelled.").ConfigureAwait(false);
            return InterviewCommand.Exit.Cancelled;
        }
    }
}
