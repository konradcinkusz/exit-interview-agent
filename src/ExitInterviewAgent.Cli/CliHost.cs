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
    bool ExportTelemetry = true)
{
    public static CliHost FromConsole(CancellationToken cancellation) => new(Console.In, Console.Out, Console.Error, Environment.GetEnvironmentVariable, null, cancellation);
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
