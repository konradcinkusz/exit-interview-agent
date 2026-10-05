namespace ExitInterviewAgent.Cli;

/// <summary>
/// Where a secret comes from. There is deliberately no command-line flag for any secret: arguments appear in process listings and
/// shell history. Order: the named environment variable; else a hidden prompt when a terminal is attached; else one line of standard
/// input (a pipe has nothing to hide from). The secret is never echoed and never written anywhere.
/// </summary>
internal static class SecretInput
{
    /// <returns>The secret, or null if the user cancelled or no input came. A value that does not have the shape of a secret is an error that never repeats it.</returns>
    public static async Task<RedactedSecret?> ReadAsync(CliHost host, string envName, string label, string prompt)
    {
        string? text;
        var fromEnv = host.Env(envName);
        if (!string.IsNullOrWhiteSpace(fromEnv)) text = fromEnv;
        else if (host.SecretPrompt is { IsInteractive: true } terminal) text = terminal.Read(prompt + " (typing is hidden): ", host.Out, host.Cancellation);
        else
        {
            await host.Out.WriteAsync($"{prompt} (one line from standard input): ").ConfigureAwait(false);
            try { text = await host.In.ReadLineAsync(host.Cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { text = null; }
            await host.Out.WriteLineAsync().ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!RedactedSecret.TryCreate(text, out var secret))
            throw new ArgumentException($"That does not look like a {label} (it should be 16 to 256 letters, digits, '-' or '_'). {(string.IsNullOrWhiteSpace(fromEnv) ? string.Empty : $"It came from {envName}. ")}Nothing was sent.");
        return secret;
    }
}
