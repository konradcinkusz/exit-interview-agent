using System.Globalization;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Cli.Tiles;
using ExitInterviewAgent.Providers;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview interview</c>: a real interview in the terminal, with a model of the user's choosing. The transcript stays in
/// memory (written only with <c>--save-transcript</c>); the record is written only if one was produced and is valid; nothing at all is
/// written when the user stops (Ctrl-C, Ctrl-D, or says so). A valid record can then be offered for submission (typed confirmation, never automatic); see <see cref="SubmitFlow"/>.
/// </summary>
internal static class InterviewCommand
{
    public static class Exit
    {
        public const int Ok = 0, Usage = 2, NoRecordByChoice = 3, AgentFailed = 4, ProviderFailed = 5, NotConfirmed = 6, Cancelled = 130;
    }

    internal static readonly IReadOnlySet<string> Values = new HashSet<string>(ProviderOptions.ValueFlags) { "--out", "--employer", "--tenure", "--seniority", "--function", "--server", "--save-receipt", "--language" };
    internal static readonly IReadOnlySet<string> Switches = new HashSet<string> { "--save-transcript", "--yes-i-understand", "--no-tiles" };

    public static async Task<int> RunAsync(string[] args, CliHost host)
    {
        var flags = Flags.Parse(args, Values, Switches);
        var employer = flags["--employer"] ?? "unspecified-employer";
        if (!EmployerRef.IsValid(employer)) throw new ArgumentException("--employer must be 3-64 characters of lowercase letters, digits and single hyphens.");
        var outDir = flags["--out"];
        var saveTranscript = flags.Has("--save-transcript");
        if (saveTranscript && outDir is null) throw new ArgumentException("--save-transcript needs --out <dir>: the transcript is written only where you say.");
        var language = ResolveLanguage(flags["--language"], CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var tiles = !flags.Has("--no-tiles");
        // Nothing this run would write may already exist: the refusal comes before the interview and before any model call.
        if (outDir is not null) CheckOutputFolder(outDir, tiles, saveTranscript);

        // Checked before the interview, not after: a bad address should not be discovered once the record exists.
        var server = ServerUrl.TryResolve(flags["--server"], host.Env);
        var resolved = ProviderOptions.Resolve(flags, host);
        var settings = resolved.Settings;

        if (!await ConfirmAsync(settings, flags.Has("--yes-i-understand"), host).ConfigureAwait(false)) return Exit.NotConfirmed;

        var context = await ReadContextAsync(flags, host).ConfigureAwait(false);
        if (context is null) return Exit.Cancelled;

        var wording = InterviewProtocol.For(language);
        var protocol = settings.MaxTokens is { } cap ? wording.WithLimits(wording.Limits with { MaxEstimatedTokens = (int)Math.Min(cap, int.MaxValue) }) : wording;
        ProviderChatClient? provider = null;
        IChatClient model;
        if (settings.Kind == ProviderKind.Mock) model = new ScriptedChatClient();
        else model = provider = ProviderChatClients.Create(settings, host.Runtime, protocol.Limits);

        using var telemetry = host.ExportTelemetry ? TelemetrySetup.Start(host.Env) : null;
        using (provider)
        {
            await host.Out.WriteLineAsync().ConfigureAwait(false);
            await host.Out.WriteLineAsync("Type your answer and press Enter. Ctrl-C or Ctrl-D stops the interview and discards everything.").ConfigureAwait(false);
            var options = new InterviewOptions(employer, context) { Protocol = protocol };
            // One budget for the interview and for the tiles that follow it (Y4): the tiles' calls count against the same limits.
            var meter = new ModelMeter(protocol.Limits);
            var runner = InterviewRunner.Create(model, options, meter);

            InterviewResult result;
            try
            {
                result = await runner.RunAsync(new ConsoleInterviewee(host.In, host.Out), host.Cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await host.Out.WriteLineAsync("Interview cancelled. Nothing was kept.").ConfigureAwait(false);
                return Exit.Cancelled;
            }
            catch (ModelCallFailedException e)
            {
                await host.Err.WriteLineAsync(string.Empty).ConfigureAwait(false);
                await host.Err.WriteLineAsync($"The interview was stopped because the model provider failed ({e.Code}). Nothing was kept or written.").ConfigureAwait(false);
                await host.Err.WriteLineAsync(Hint(e.Code)).ConfigureAwait(false);
                await PrintUsage(host, provider, settings).ConfigureAwait(false);
                return Exit.ProviderFailed;
            }

            return await ReportAsync(result, host, outDir, saveTranscript, provider, settings, server, flags["--save-receipt"], model, meter, tiles).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <c>--language pl|en|auto</c>. <c>auto</c> (the default) follows the system UI language: Polish if it is Polish, English otherwise.
    /// <paramref name="uiLanguage"/> is the two-letter UI language (<c>CultureInfo.CurrentUICulture.TwoLetterISOLanguageName</c>), passed in so
    /// the choice is tested without changing the process culture.
    /// </summary>
    internal static string ResolveLanguage(string? value, string uiLanguage) => value switch
    {
        null or "auto" => uiLanguage == "pl" ? "pl" : "en",
        "pl" or "en" => value,
        _ => throw new ArgumentException("--language must be one of: pl, en, auto."),
    };

    private static string Hint(string code) => code switch
    {
        "provider.auth_failed" => "Check the API key (and that it belongs to this provider). Run 'exit-interview providers ping' to test it with one minimal request.",
        "provider.bad_request" => "Check the model name and base URL. Run 'exit-interview providers' to see what is configured.",
        "provider.budget_exceeded" => "The hard budget for one interview was reached. Raise it with --max-tokens, or check the model is not looping.",
        "provider.unavailable" => "The provider failed repeatedly. Check that it is reachable (is the local server running?).",
        _ => "Run 'exit-interview providers ping' to test the provider.",
    };

    private static async Task<bool> ConfirmAsync(ProviderSettings settings, bool assumeYes, CliHost host)
    {
        var notice = Disclosure.For(settings);
        await host.Out.WriteLineAsync().ConfigureAwait(false);
        await host.Out.WriteAsync(notice.Text).ConfigureAwait(false);
        if (!notice.RequiresConfirmation) return true;

        var store = new ConfirmationStore(ConfigPaths.ConfirmationsFile(host.Env));
        if (assumeYes) { await host.Out.WriteLineAsync("(--yes-i-understand given: continuing.)").ConfigureAwait(false); return true; }
        if (store.IsConfirmed(settings)) { await host.Out.WriteLineAsync("(You confirmed this provider before on this computer: continuing. 'exit-interview providers forget-confirmations' undoes that.)").ConfigureAwait(false); return true; }

        await host.Out.WriteAsync("Type \"yes\" to continue (anything else stops): ").ConfigureAwait(false);
        var answer = await ReadAsync(host).ConfigureAwait(false);
        if (!string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            await host.Out.WriteLineAsync("Not confirmed. Nothing was sent anywhere.").ConfigureAwait(false);
            return false;
        }

        await host.Out.WriteAsync($"Remember this choice for {settings.Info.DisplayName} at {settings.Endpoint} on this computer? [y/N]: ").ConfigureAwait(false);
        var remember = await ReadAsync(host).ConfigureAwait(false);
        if (string.Equals(remember?.Trim(), "y", StringComparison.OrdinalIgnoreCase) || string.Equals(remember?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            store.Remember(settings);
            await host.Out.WriteLineAsync($"Remembered in {store.Path} (delete the file to forget).").ConfigureAwait(false);
        }
        return true;
    }

    private static async Task<string?> ReadAsync(CliHost host)
    {
        try { return await host.In.ReadLineAsync(host.Cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>Tenure is part of the record's context and has no sensible default, so it is asked for when the flag is missing.</summary>
    private static async Task<RecordContext?> ReadContextAsync(Flags flags, CliHost host)
    {
        var tenureText = flags["--tenure"];
        if (tenureText is null)
        {
            var names = string.Join(", ", Enum.GetValues<TenureBand>().Select(Wire.Name));
            await host.Out.WriteAsync($"How long did you work there? ({names}): ").ConfigureAwait(false);
            tenureText = (await ReadAsync(host).ConfigureAwait(false))?.Trim();
            if (tenureText is null) { await host.Out.WriteLineAsync("Nothing was kept.").ConfigureAwait(false); return null; }
        }
        if (!Wire.TryParse<TenureBand>(tenureText, out var tenure)) throw new ArgumentException("--tenure must be one of: " + string.Join(", ", Enum.GetValues<TenureBand>().Select(Wire.Name)) + ".");
        SeniorityBand? seniority = flags["--seniority"] is { } s ? (Wire.TryParse<SeniorityBand>(s, out var sv) ? sv : throw new ArgumentException("--seniority must be one of: " + string.Join(", ", Enum.GetValues<SeniorityBand>().Select(Wire.Name)) + ".")) : null;
        FunctionBand? function = flags["--function"] is { } fn ? (Wire.TryParse<FunctionBand>(fn, out var fv) ? fv : throw new ArgumentException("--function must be one of: " + string.Join(", ", Enum.GetValues<FunctionBand>().Select(Wire.Name)) + ".")) : null;
        return new RecordContext(tenure, seniority, function);
    }

    private static async Task<int> ReportAsync(InterviewResult r, CliHost host, string? outDir, bool saveTranscript, ProviderChatClient? provider, ProviderSettings settings, ServerUrl? server, string? saveReceipt, IChatClient model, ModelMeter meter, bool tiles)
    {
        var o = host.Out;
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("== Result ==").ConfigureAwait(false);
        await o.WriteLineAsync($"outcome: {Snake(r.Outcome)} ({r.EndReason})").ConfigureAwait(false);

        var code = r.Outcome switch
        {
            InterviewOutcome.Completed => Exit.Ok,
            InterviewOutcome.Withdrawn or InterviewOutcome.ConsentNotGiven or InterviewOutcome.Abandoned => Exit.NoRecordByChoice,
            _ => Exit.AgentFailed,
        };

        if (r.Outcome is InterviewOutcome.Withdrawn or InterviewOutcome.ConsentNotGiven or InterviewOutcome.Abandoned)
            await o.WriteLineAsync("The interview ended without a record. The transcript was discarded: nothing was kept or written.").ConfigureAwait(false);
        else if (r.Outcome == InterviewOutcome.PiiGuardFailed)
            await o.WriteLineAsync("The personal-data check failed, so nothing may be kept. Nothing was written.").ConfigureAwait(false);
        else if (r.Outcome == InterviewOutcome.ExtractionFailed)
            await o.WriteLineAsync("The model did not produce a usable record" + (saveTranscript ? "; the transcript is saved below." : ". The transcript was only in memory and is gone; run again with --save-transcript --out <dir> to keep it.")).ConfigureAwait(false);

        if (r.RecordJson is not null)
        {
            await o.WriteLineAsync().ConfigureAwait(false);
            await o.WriteLineAsync("== Record ==").ConfigureAwait(false);
            await o.WriteLineAsync(Pretty(r.RecordJson)).ConfigureAwait(false);
            await o.WriteLineAsync().ConfigureAwait(false);
            await o.WriteLineAsync("== Validation ==").ConfigureAwait(false);
            await o.WriteLineAsync(r.Validation is { IsValid: true } ? "valid: yes, errors: 0" : $"valid: no, error codes: {string.Join(", ", r.Validation?.Errors.Select(e => e.Code) ?? [])}").ConfigureAwait(false);
            await o.WriteLineAsync($"submittable: {(r.Submittable ? "yes" : "no")}{(r.Outcome == InterviewOutcome.Completed && !r.HasContent ? " (the record carries no covered topic)" : string.Empty)}.").ConfigureAwait(false);
        }

        if (outDir is not null)
        {
            var wrote = new List<string>();
            if (r.RecordJson is not null && r.Validation is { IsValid: true })
            {
                Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "record.json");
                await File.WriteAllTextAsync(path, Pretty(r.RecordJson) + "\n").ConfigureAwait(false);
                wrote.Add(path);
            }
            if (saveTranscript && r.Transcript is not null)
            {
                Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "transcript.txt");
                await File.WriteAllTextAsync(path, r.Transcript.Render()).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                wrote.Add(path);
            }
            await o.WriteLineAsync().ConfigureAwait(false);
            await o.WriteLineAsync(wrote.Count == 0 ? "Nothing was written." : "Wrote: " + string.Join(", ", wrote)).ConfigureAwait(false);
        }
        else if (r.RecordJson is not null) await o.WriteLineAsync("Nothing was written (use --out <dir> to save the record).").ConfigureAwait(false);

        if (tiles) await AutoTilesAsync(r, model, meter, host, outDir).ConfigureAwait(false);

        await PrintUsage(host, provider, settings).ConfigureAwait(false);
        if (!r.Submittable || r.RecordJson is null) return code;

        // Never automatic: the flow shows the exact record and waits for a typed word. A declined or failed submission does not make the interview a failure.
        if (server is null)
        {
            await o.WriteLineAsync().ConfigureAwait(false);
            await o.WriteLineAsync($"Submitting is optional and separate. To do it: set {ServerUrl.EnvironmentVariable} (or pass --server), mint a one-time ticket on {SubmitMessages.WebPanelHint}, then run 'exit-interview submit --record <file>' on a record written with --out.").ConfigureAwait(false);
            return code;
        }
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync($"== Submit? ==").ConfigureAwait(false);
        await o.WriteLineAsync($"You can send this record to {server.Display} now. You need a one-time ticket from {SubmitMessages.WebPanelHint}. You can also keep it and submit later with 'exit-interview submit'.").ConfigureAwait(false);
        var submitted = await SubmitFlow.RunAsync(Encoding.UTF8.GetBytes(Pretty(r.RecordJson) + "\n"), host, server, assumeYes: false, saveReceipt).ConfigureAwait(false);
        return submitted is SubmitFlow.Exit.Rejected or SubmitFlow.Exit.NetworkFailure or SubmitFlow.Exit.Cancelled or SubmitFlow.Exit.RecordInvalid ? submitted : code;
    }

    /// <summary>
    /// The tiles at the end of an interview (Y4, ADR-0075): the same model and the same budget as the interview, fed the record and the
    /// PII-masked transcript, guarded like any tiles, shown on the terminal and, with <c>--out</c>, written as new files under <c>tiles/</c>.
    /// A problem here never changes the interview's result: the reason is printed and the interview still succeeds.
    /// </summary>
    internal static async Task AutoTilesAsync(InterviewResult r, IChatClient model, ModelMeter meter, CliHost host, string? outDir)
    {
        var o = host.Out;
        if (r.Outcome != InterviewOutcome.Completed || r.Record is null || r.RecordJson is null)
        {
            await o.WriteLineAsync("No tiles: the interview ended without a record.").ConfigureAwait(false);
            return;
        }
        if (r.Validation is not { IsValid: true })
        {
            await o.WriteLineAsync("No tiles: the record did not pass validation.").ConfigureAwait(false);
            return;
        }
        if (meter.Exhausted)
        {
            await o.WriteLineAsync("No tiles: the model budget of this interview is used up. The interview result stands. 'exit-interview tiles --record <file>' can make them later from a saved record, with a new budget.").ConfigureAwait(false);
            return;
        }

        var watch = new ProviderFailureWatch(new ModelTileWriter(new MeteredChatClient(model, meter)));
        var generator = new TileGenerator(watch, new TileGuard(new PiiGuard()));
        TileSet set;
        try
        {
            set = await generator.GenerateAsync(new TileInput(r.Record, r.Transcript?.Render()), host.Cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (host.Cancellation.IsCancellationRequested)
        {
            await o.WriteLineAsync("No tiles: cancelled. The interview result stands.").ConfigureAwait(false);
            return;
        }

        // The generator never throws for a model failure, so a failure is seen here, and no tiles are shown from it.
        if (watch.Failure is { } failure)
        {
            await o.WriteLineAsync($"No tiles: the model provider failed ({failure.Code}). The interview result stands; 'exit-interview providers ping' tests the provider.").ConfigureAwait(false);
            return;
        }

        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteAsync(TileRenderer.ToText(set)).ConfigureAwait(false);
        if (outDir is not null && !await WriteTileFilesAsync(Path.Combine(outDir, "tiles"), set, host).ConfigureAwait(false)) return;
    }

    /// <summary>The two tile files, as NEW files (never overwritten), owner-only where the system supports it. False when one could not be created.</summary>
    private static async Task<bool> WriteTileFilesAsync(string dir, TileSet set, CliHost host)
    {
        Directory.CreateDirectory(dir);
        var wrote = new List<string>();
        foreach (var (name, content) in new[] { ("tiles.json", TileRenderer.ToJson(set) + "\n"), ("tiles.html", TileRenderer.ToHtml(set)) })
        {
            var path = Path.Combine(dir, name);
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
                await host.Err.WriteLineAsync($"No tiles file was written: {name} already exists and will not be overwritten.").ConfigureAwait(false);
                return false;
            }
            wrote.Add(path);
        }
        await host.Out.WriteLineAsync().ConfigureAwait(false);
        await host.Out.WriteLineAsync("Wrote: " + string.Join(", ", wrote)).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// <c>--out</c> must be a folder that holds none of the files this run would write (<c>record.json</c>, the transcript if asked for, and the
    /// two tile files unless <c>--no-tiles</c>). Nothing is overwritten, and a second run into the same folder is refused before the interview.
    /// </summary>
    private static void CheckOutputFolder(string outDir, bool tiles, bool saveTranscript)
    {
        if (File.Exists(outDir)) throw new ArgumentException("--out names a file, not a folder.");
        var names = new List<string> { "record.json" };
        if (saveTranscript) names.Add("transcript.txt");
        if (tiles) names.AddRange([Path.Combine("tiles", "tiles.json"), Path.Combine("tiles", "tiles.html")]);
        foreach (var name in names)
            if (File.Exists(Path.Combine(outDir, name))) throw new ArgumentException($"--out: {name} already exists and will not be overwritten. Choose a new folder.");
    }

    /// <summary>Records that the writer's model call failed, then lets the exception through: the generator decides what to do with it.</summary>
    private sealed class ProviderFailureWatch(ITileWriter inner) : ITileWriter
    {
        public ModelCallFailedException? Failure { get; private set; }

        public Task<string> WriteAsync(InterviewRecord record, IReadOnlyList<string> previousErrorCodes, CancellationToken ct) =>
            WriteAsync(new TileInput(record), previousErrorCodes, ct);

        /// <summary>Overridden so the masked transcript passes through: the interface default would drop it.</summary>
        public async Task<string> WriteAsync(TileInput input, IReadOnlyList<string> previousErrorCodes, CancellationToken ct)
        {
            try
            {
                return await inner.WriteAsync(input, previousErrorCodes, ct).ConfigureAwait(false);
            }
            catch (ModelCallFailedException e)
            {
                Failure ??= e;
                throw;
            }
        }
    }

    private static async Task PrintUsage(CliHost host, ProviderChatClient? provider, ProviderSettings settings)
    {
        if (provider is null) return;
        await host.Out.WriteLineAsync().ConfigureAwait(false);
        await host.Out.WriteLineAsync(UsageLine(provider)).ConfigureAwait(false);
        _ = settings;
    }

    /// <summary>The one-line usage summary (calls, tokens, latency, cost or why there is none). Shared with <c>tiles</c>.</summary>
    internal static string UsageLine(ProviderChatClient provider)
    {
        var u = provider.Budget.Snapshot();
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"usage: model_calls={u.Calls} tokens_in={u.InputTokens} tokens_out={u.OutputTokens} model_latency={u.TotalLatency.TotalSeconds:F1}s");
        if (u.AnyEstimated) sb.Append(" (some counts estimated: the provider reported none)");
        if (u.Cost is { } cost) sb.Append(CultureInfo.InvariantCulture, $" cost={cost:F4} {u.Currency} (your prices x tokens)");
        else sb.Append(" cost=not computed (no prices configured)");
        return sb.ToString();
    }

    internal static string Pretty(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Snake(Enum e) => ExitInterviewAgent.Agent.Tracing.SpanTags.Snake(e.ToString());
}
