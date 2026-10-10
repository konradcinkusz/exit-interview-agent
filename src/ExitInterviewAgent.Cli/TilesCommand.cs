using System.Text;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Cli.Tiles;
using ExitInterviewAgent.Providers;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview tiles</c>: draft texts (ADR-0074) built from a validated record. Only the record is sent to an external provider,
/// never a transcript; the texts are proposals the person reads and changes before using them. Nothing is published, submitted or stored
/// except the files named by <c>--out</c>, which are new files only. Tiles that fail the guard are dropped and counted, never shown.
/// </summary>
internal static class TilesCommand
{
    public static class Exit
    {
        public const int Ok = 0, Usage = 2, NotConfirmed = 3, RecordInvalid = 4, ProviderFailed = 5, Cancelled = 130;
    }

    internal static readonly IReadOnlySet<string> Values = new HashSet<string>(ProviderOptions.ValueFlags) { "--record", "--out", "--format" };
    internal static readonly IReadOnlySet<string> Switches = new HashSet<string> { "--yes-i-understand" };

    private static readonly string[] Formats = ["text", "html", "json"];

    public static async Task<int> RunAsync(string[] args, CliHost host)
    {
        var flags = Flags.Parse(args, Values, Switches);
        var recordArg = flags["--record"] ?? throw new ArgumentException("--record <file> is required (the record.json that 'interview --out' wrote; '-' reads it from standard input).");
        var format = flags["--format"] ?? "text";
        if (Array.IndexOf(Formats, format) < 0) throw new ArgumentException("--format must be one of: text, html, json.");
        var outDir = flags["--out"];
        var assumeYes = flags.Has("--yes-i-understand");

        var settings = ProviderOptions.Resolve(flags, host).Settings;
        var external = Disclosure.For(settings).RequiresConfirmation;
        if (external && recordArg == "-" && !assumeYes) throw new ArgumentException("With --record - the record is read from standard input, so there is no way to ask you to confirm. Add --yes-i-understand (it is for scripts you control), or give a file.");

        // Everything that can be refused without sending anything is refused first: a bad output path, then the record.
        if (outDir is not null) CheckOutputFolder(outDir);
        var bytes = await ReadRecordAsync(recordArg, host).ConfigureAwait(false);
        var check = LocalRecordCheck.Run(bytes);
        if (!check.Ok)
        {
            await host.Err.WriteLineAsync("The record cannot be used for tiles. Nothing was sent.").ConfigureAwait(false);
            foreach (var p in check.Problems) await host.Err.WriteLineAsync("  - " + p).ConfigureAwait(false);
            return Exit.RecordInvalid;
        }
        var record = new RecordValidator().Validate(bytes).Record!;

        if (!await ConfirmAsync(settings, external, assumeYes, host).ConfigureAwait(false)) return Exit.NotConfirmed;

        var protocol = settings.MaxTokens is { } cap ? InterviewProtocol.Current.WithLimits(InterviewProtocol.Current.Limits with { MaxEstimatedTokens = (int)Math.Min(cap, int.MaxValue) }) : InterviewProtocol.Current;
        ProviderChatClient? provider = null;
        IChatClient model;
        if (settings.Kind == ProviderKind.Mock) model = new ScriptedChatClient();
        else model = provider = ProviderChatClients.Create(settings, host.Runtime, protocol.Limits);

        using var telemetry = host.ExportTelemetry ? TelemetrySetup.Start(host.Env) : null;
        using (provider)
        {
            var metered = new MeteredChatClient(model, new ModelMeter(protocol.Limits));
            var watch = new ProviderFailureWatch(new ModelTileWriter(metered));
            var generator = new TileGenerator(watch, new TileGuard(new PiiGuard()));

            TileSet set;
            try
            {
                set = await generator.GenerateAsync(record, host.Cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (host.Cancellation.IsCancellationRequested)
            {
                await host.Err.WriteLineAsync("Cancelled. Nothing was written.").ConfigureAwait(false);
                return Exit.Cancelled;
            }

            // The generator never throws for a model failure, so a failure is seen here, not in the set: no tiles are written from it.
            if (watch.Failure is { } failure)
            {
                await host.Err.WriteLineAsync("The model provider failed, so no tiles were written.").ConfigureAwait(false);
                await host.Err.WriteLineAsync(failure.Message).ConfigureAwait(false);
                await host.Err.WriteLineAsync("Run 'exit-interview providers ping' to test the provider.").ConfigureAwait(false);
                if (provider is not null) await host.Err.WriteLineAsync(InterviewCommand.UsageLine(provider)).ConfigureAwait(false);
                return Exit.ProviderFailed;
            }

            if (outDir is not null) await WriteFilesAsync(outDir, set, host).ConfigureAwait(false);

            await host.Out.WriteAsync(format switch
            {
                "html" => TileRenderer.ToHtml(set),
                "json" => TileRenderer.ToJson(set) + "\n",
                _ => TileRenderer.ToText(set),
            }).ConfigureAwait(false);

            // The usage line is counts only. Any telemetry export carries metadata only too (the chat spans never hold message text).
            if (provider is not null) await host.Err.WriteLineAsync(InterviewCommand.UsageLine(provider)).ConfigureAwait(false);
            return Exit.Ok;
        }
    }

    /// <summary>The output folder must not hold either file already: nothing is overwritten, and a refusal happens before any model call.</summary>
    private static void CheckOutputFolder(string outDir)
    {
        if (File.Exists(outDir)) throw new ArgumentException("--out names a file, not a folder.");
        foreach (var name in new[] { "tiles.json", "tiles.html" })
            if (File.Exists(Path.Combine(outDir, name))) throw new ArgumentException($"--out: {name} already exists and will not be overwritten. Choose a new folder.");
    }

    private static async Task<byte[]> ReadRecordAsync(string recordArg, CliHost host)
    {
        if (recordArg == "-")
        {
            var text = await host.In.ReadToEndAsync(host.Cancellation).ConfigureAwait(false);
            return Encoding.UTF8.GetBytes(text);
        }
        if (!File.Exists(recordArg)) throw new ArgumentException("The file given with --record does not exist.");
        if (new FileInfo(recordArg).Length > ExitInterviewAgent.Records.RecordLimits.Default.MaxPayloadBytes) throw new ArgumentException("The file given with --record is larger than any record can be.");
        return await File.ReadAllBytesAsync(recordArg, host.Cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// The disclosure is printed to standard error, so standard output stays clean for <c>--format json</c>. An external provider
    /// asks for a typed "yes" (or <c>--yes-i-understand</c>); nothing is remembered, because a remembered interview confirmation
    /// covers a transcript, not a record.
    /// </summary>
    private static async Task<bool> ConfirmAsync(ProviderSettings settings, bool external, bool assumeYes, CliHost host)
    {
        var e = host.Err;
        if (!external)
        {
            var local = settings.Kind == ProviderKind.Mock
                ? "Scripted mock model: nothing is sent anywhere."
                : $"Local model at {settings.Endpoint}: the record is processed on this computer.";
            await e.WriteLineAsync(local).ConfigureAwait(false);
            return true;
        }

        var credential = settings.ApiKey is null ? "without a key" : "using your own API key";
        await e.WriteLineAsync().ConfigureAwait(false);
        await e.WriteLineAsync("Before we start: where your record goes").ConfigureAwait(false);
        await e.WriteLineAsync().ConfigureAwait(false);
        await e.WriteLineAsync($"rekord (nie transkrypt) zostanie wysłany do {settings.Info.DisplayName}, at {settings.Endpoint}, {credential}.").ConfigureAwait(false);
        await e.WriteLineAsync("Only the validated record is sent: its ratings, confidence and the quotes it holds. The transcript is not sent and is not kept.").ConfigureAwait(false);
        await e.WriteLineAsync("The provider writes draft texts from it. Nothing is published, and you read and change every text before you use it.").ConfigureAwait(false);
        await e.WriteLineAsync().ConfigureAwait(false);

        if (assumeYes) { await e.WriteLineAsync("(--yes-i-understand given: continuing.)").ConfigureAwait(false); return true; }

        await e.WriteAsync("Type \"yes\" to continue (anything else stops): ").ConfigureAwait(false);
        var answer = await ReadAsync(host).ConfigureAwait(false);
        if (string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase)) return true;

        await e.WriteLineAsync("Not confirmed. Nothing was sent anywhere.").ConfigureAwait(false);
        return false;
    }

    private static async Task<string?> ReadAsync(CliHost host)
    {
        try { return await host.In.ReadLineAsync(host.Cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>Writes the two files as NEW files (never overwritten), owner-only where the system supports it.</summary>
    private static async Task WriteFilesAsync(string outDir, TileSet set, CliHost host)
    {
        Directory.CreateDirectory(outDir);
        var wrote = new List<string>();
        foreach (var (name, content) in new[] { ("tiles.json", TileRenderer.ToJson(set) + "\n"), ("tiles.html", TileRenderer.ToHtml(set)) })
        {
            var path = Path.Combine(outDir, name);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                await using var stream = new FileStream(path, options);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await writer.WriteAsync(content).ConfigureAwait(false);
            }
            catch (IOException) when (File.Exists(path))
            {
                throw new ArgumentException($"--out: {name} already exists and will not be overwritten. Choose a new folder.");
            }
            wrote.Add(path);
        }
        await host.Err.WriteLineAsync("Wrote: " + string.Join(", ", wrote)).ConfigureAwait(false);
    }

    /// <summary>Records that the writer's model call failed, then lets the exception through: the generator decides what to do with it.</summary>
    private sealed class ProviderFailureWatch(ITileWriter inner) : ITileWriter
    {
        public ModelCallFailedException? Failure { get; private set; }

        public async Task<string> WriteAsync(InterviewRecord record, IReadOnlyList<string> previousErrorCodes, CancellationToken ct)
        {
            try
            {
                return await inner.WriteAsync(record, previousErrorCodes, ct).ConfigureAwait(false);
            }
            catch (ModelCallFailedException e)
            {
                Failure ??= e;
                throw;
            }
        }
    }
}
