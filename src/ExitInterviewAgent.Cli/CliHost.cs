using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli;

/// <summary>Everything the CLI touches in the outside world, so tests drive it with scripted input and a fake transport.</summary>
public sealed record CliHost(
    TextReader In,
    TextWriter Out,
    TextWriter Err,
    Func<string, string?> Env,
    ProviderRuntime? Runtime = null,
    CancellationToken Cancellation = default,
    bool ExportTelemetry = true,
    HttpMessageHandler? Http = null,
    ISecretPrompt? SecretPrompt = null)
{
    public static CliHost FromConsole(CancellationToken cancellation) =>
        new(Console.In, Console.Out, Console.Error, Environment.GetEnvironmentVariable, null, cancellation, SecretPrompt: new ConsoleSecretPrompt());
}

/// <summary>Reads a secret without echoing it. Absent or not interactive (a pipe, a test): the secret is read as a plain line from standard input, where there is nothing to echo to.</summary>
public interface ISecretPrompt
{
    bool IsInteractive { get; }

    /// <returns>The typed text, or null when the user cancelled or input ended.</returns>
    string? Read(string prompt, TextWriter output, CancellationToken ct);
}

internal sealed class ConsoleSecretPrompt : ISecretPrompt
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public string? Read(string prompt, TextWriter output, CancellationToken ct)
    {
        output.Write(prompt);
        output.Flush();
        var text = new System.Text.StringBuilder();
        while (true)
        {
            while (!Console.KeyAvailable)
            {
                if (ct.IsCancellationRequested) { output.WriteLine(); return null; }
                Thread.Sleep(30);
            }
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { output.WriteLine(); return text.ToString(); }
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { output.WriteLine(); return null; }
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
    }
}

/// <summary>Minimal flag parsing: <c>--name value</c> and bare boolean switches; anything unknown is an error.</summary>
internal sealed class Flags
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _switches = new(StringComparer.Ordinal);

    public static Flags Parse(string[] args, IReadOnlySet<string> valueFlags, IReadOnlySet<string> switchFlags)
    {
        var flags = new Flags();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (switchFlags.Contains(arg)) flags._switches.Add(arg);
            else if (valueFlags.Contains(arg))
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"Option '{arg}' needs a value.");
                flags._values[arg] = args[++i];
            }
            else throw new ArgumentException($"Unknown option '{Safe(arg)}'.");
        }
        return flags;
    }

    public string? this[string name] => _values.GetValueOrDefault(name);

    public bool Has(string name) => _switches.Contains(name);

    /// <summary>An unknown option is echoed only if it looks like an option, never a pasted value.</summary>
    private static string Safe(string arg) => arg.StartsWith("--", StringComparison.Ordinal) && arg.Length <= 32 && arg.All(c => char.IsLetterOrDigit(c) || c == '-') ? arg : "(value)";
}
