using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Cli.Tests.Support;

public sealed record CliResult(int Code, string Out, string Err)
{
    public string All => Out + "\n" + Err;
}

public static class CliRun
{
    /// <summary>Canaries: strings that must never appear where a secret or submitted text must not go. All pass the secrets' shape check.</summary>
    public const string CanaryTicket = "CANARYTICKET-0123456789-abcdefghijklmnopqrstuvw";
    public const string CanaryReceipt = "CANARYRECEIPT-0123456789-abcdefghijklmnopqr";

    public static async Task<CliResult> RunAsync(string[] args, string stdin, Func<string, string?> env, HttpMessageHandler? http = null, ISecretPrompt? prompt = null, CancellationToken ct = default, TextReader? reader = null, Func<StringWriter, TextReader>? readerFactory = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = await CliApp.RunAsync(args, new CliHost(readerFactory?.Invoke(o) ?? reader ?? new StringReader(stdin), o, e, env, null, ct, ExportTelemetry: false, Http: http, SecretPrompt: prompt));
        return new CliResult(code, o.ToString(), e.ToString());
    }

    public static Func<string, string?> Env(params (string Key, string Value)[] vars)
    {
        var d = vars.ToDictionary(v => v.Key, v => v.Value);
        return k => d.GetValueOrDefault(k);
    }
}
