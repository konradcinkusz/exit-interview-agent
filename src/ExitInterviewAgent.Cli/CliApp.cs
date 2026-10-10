using System.Globalization;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Personas;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview</c>: <c>demo</c> runs a whole simulated interview offline with the scripted mock model and a
/// persona, <c>personas</c> lists them (no network, no credentials); <c>interview</c> runs a real one with the user's own provider;
/// <c>providers</c> inspects configuration; <c>submit</c> and <c>delete-receipt</c> talk to the submission service, only on request. Exit code of <c>demo</c>: 0 all invariants hold, 1 an invariant failed, 2 usage error.
/// </summary>
public static class CliApp
{
    public const string Usage = """
        exit-interview: an AI exit interview agent. Bring your own model: your API key (Anthropic, OpenAI-compatible) or a local model (Ollama).

        Usage:
          exit-interview interview --provider <p> --model <m> [--base-url <url>] [--api-key-env <NAME>] [--out <dir>] [--employer <ref>]
                                   [--language pl|en|auto]
                                   [--tenure <band>] [--seniority <band>] [--function <band>] [--save-transcript] [--yes-i-understand]
                                   [--max-tokens <n>] [--timeout-seconds <n>] [--max-retries <n>] [--num-ctx <n>]
                                   [--price-in <per-million>] [--price-out <per-million>] [--max-cost <amount>] [--config <file>]
                                   [--server <url>] [--save-receipt <file>]
          exit-interview tiles --record <file|-> [--provider <p> --model <m> [--base-url <url>] [--api-key-env <NAME>] [--max-tokens <n>]
                                   [--timeout-seconds <n>] [--max-retries <n>] [--num-ctx <n>] [--price-in <per-million>] [--price-out <per-million>]
                                   [--max-cost <amount>] [--config <file>]] [--out <dir>] [--format text|html|json] [--yes-i-understand]
          exit-interview submit --record <file|-> --server <url> [--save-receipt <file>] [--yes]
          exit-interview delete-receipt --server <url>
          exit-interview providers [ping <provider flags> | forget-confirmations]
          exit-interview demo --persona <id> [--seed <n>] [--out <dir>]
          exit-interview personas
          exit-interview --help | --version

        interview   A real interview in this terminal. Providers: anthropic, openai-compatible (alias openai), ollama; 'mock' is the offline test model.
                    The API key is read from an environment variable only (ANTHROPIC_API_KEY, OPENAI_API_KEY, or the one --api-key-env names): there is
                    no --api-key flag. Settings come from flags, then EXIT_INTERVIEW_* environment variables, then the config file. Before an external
                    provider is used you are told where the transcript goes and asked to confirm (--yes-i-understand for scripts). The transcript stays
                    in memory; --save-transcript (with --out) writes it. Ctrl-C or Ctrl-D stops and discards everything. Nothing is submitted unless you
                    type the confirmation word after seeing the exact record (only offered when a server address is configured).
                    Exit codes: 0 completed, 2 usage or configuration, 3 ended without a record by choice, 4 agent failure, 5 provider failure, 6 not confirmed, 130 cancelled.
        tiles       Draft texts (ADR-0074) built from a validated record: a Facts tile by code and model-written tiles that pass a code-side guard.
                    Only the record is sent to an external provider (never the transcript), after the same typed confirmation as 'interview'
                    (--yes-i-understand for scripts); 'mock' and local models send nothing. The texts are proposals to read and change before
                    you use them: nothing is published. --out writes tiles.json and tiles.html as NEW files (never overwritten); --format picks
                    what standard output shows. Exit codes: 0 ok (dropped tiles are counted, not an error), 2 usage or configuration,
                    3 not confirmed, 4 record failed the local check, 5 provider failure, 130 cancelled.
        submit      Sends a record file written by 'interview --out' to the service, with a one-time ticket minted on the web panel's /cli page.
                    Before anything is sent the record is re-checked here (schema, AI disclosure, personal data), shown exactly as it will be sent, and you
                    type "submit" to confirm. The ticket has no flag (arguments leak into process lists and shell history): it is read from
                    EXIT_INTERVIEW_TICKET, a hidden prompt, or one line of standard input. --record - reads the record from standard input (then --yes
                    and EXIT_INTERVIEW_TICKET are required). --yes skips the confirmation: for tests and scripts you control. The server address
                    (--server or EXIT_INTERVIEW_SERVER_URL) has no default; https is required except for localhost. On success the receipt code is shown
                    once: it is the only way to delete the record. --save-receipt writes it to a new file (mode 0600, never overwrites).
                    Exit codes: 0 submitted, 2 usage, 3 not confirmed or no ticket, 4 record failed the local check, 5 server refused, 6 network failure, 130 cancelled.
        delete-receipt
                    Deletes the record behind a receipt code. The code comes from EXIT_INTERVIEW_RECEIPT_CODE, a hidden prompt, or one line of standard input.
                    The service answers the same for every well-formed code, so the result reads "if it existed, it is deleted now".
        providers   Lists providers and checks local configuration without any network call. 'providers ping' makes one minimal live request.
                    OpenTelemetry export is off unless OTEL_TRACES_EXPORTER / OTEL_METRICS_EXPORTER or an OTLP endpoint is set; prompts and replies are never exported.

        demo      Runs a full interview offline and prints the masked transcript, the record, the validation result
                  and an invariant report. --seed makes it reproducible (default 1). --out writes transcript.txt,
                  record.json and report.txt into the directory (no transcript or record exists when consent is withdrawn).
        personas  Lists the simulated interviewees.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr) =>
        await RunAsync(args, new CliHost(TextReader.Null, stdout, stderr, Environment.GetEnvironmentVariable, null, default, ExportTelemetry: false)).ConfigureAwait(false);

    public static async Task<int> RunAsync(string[] args, CliHost host)
    {
        var (stdout, stderr) = (host.Out, host.Err);
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { await stdout.WriteLineAsync(Usage); return args.Length == 0 ? 2 : 0; }
            return args[0] switch
            {
                "--version" => await Print(stdout, $"exit-interview, interview protocol {InterviewProtocol.Current.ProtocolVersion}"),
                "personas" => await ListPersonas(stdout),
                "demo" => await Demo(args[1..], stdout, stderr),
                "interview" => await InterviewCommand.RunAsync(args[1..], host),
                "tiles" => await TilesCommand.RunAsync(args[1..], host),
                "providers" => await ProvidersCommand.RunAsync(args[1..], host),
                "submit" => await SubmitCommand.RunAsync(args[1..], host),
                "delete-receipt" => await DeleteReceiptCommand.RunAsync(args[1..], host),
                _ => await Fail(stderr, $"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException e)
        {
            return await Fail(stderr, e.Message);
        }
        catch (OperationCanceledException) when (host.Cancellation.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("Cancelled.");
            return 130;
        }
        catch (ExitInterviewAgent.Providers.ProviderConfigurationException e)
        {
            return await Fail(stderr, e.Message);
        }
    }

    private static async Task<int> Demo(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? personaId = null, outDir = null;
        var seed = 1;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Option '{args[i]}' needs a value.");
            switch (args[i])
            {
                case "--persona": personaId = Value(); break;
                case "--seed": seed = int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : throw new ArgumentException("--seed must be an integer."); break;
                case "--out": outDir = Value(); break;
                default: throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        if (personaId is null) throw new ArgumentException("--persona <id> is required. Run 'exit-interview personas' to list them.");
        if (!PersonaCatalog.TryGet(personaId, out var persona)) throw new ArgumentException($"Unknown persona '{personaId}'. Run 'exit-interview personas' to list them.");

        var result = await PersonaSession.RunAsync(persona!, seed);
        var checks = InterviewInvariants.Check(result, InterviewProtocol.Current, persona!.Planted, persona.Employer.Names);
        var report = Render(persona, seed, result, checks);
        await stdout.WriteAsync(report);

        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            await File.WriteAllTextAsync(Path.Combine(outDir, "report.txt"), report);
            if (result.Transcript is not null) await File.WriteAllTextAsync(Path.Combine(outDir, "transcript.txt"), result.Transcript.Render());
            if (result.RecordJson is not null) await File.WriteAllTextAsync(Path.Combine(outDir, "record.json"), Pretty(result.RecordJson) + "\n");
            await stdout.WriteLineAsync($"Wrote output to {outDir}");
        }

        if (checks.Any(c => !c.Passed)) { await stderr.WriteLineAsync("At least one invariant failed."); return 1; }
        return 0;
    }

    private static string Render(PersonaDefinition persona, int seed, InterviewResult r, IReadOnlyList<InvariantCheck> checks)
    {
        var sb = new StringBuilder();
        sb.Append("exit-interview demo: persona ").Append(persona.Id).Append(" (seed ").Append(seed.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
        sb.AppendLine("Offline run with the scripted mock model, a test seam and not a quality baseline. Nothing is sent anywhere and nothing is submitted.");
        sb.AppendLine().AppendLine("== Masked transcript ==");
        if (r.Transcript is null) sb.AppendLine("(discarded: no transcript is kept when consent is withdrawn, refused or never given)");
        else sb.Append(r.Transcript.Render());
        sb.AppendLine().AppendLine("== Record ==");
        sb.AppendLine(r.RecordJson is null ? "(no record: none is produced without consent)" : Pretty(r.RecordJson));
        sb.AppendLine().AppendLine("== Validation ==");
        sb.AppendLine(r.Validation is null ? "not applicable (no record)" : r.Validation.IsValid ? "valid: yes, errors: 0" : $"valid: no, error codes: {string.Join(", ", r.Validation.Errors.Select(e => e.Code))}");
        sb.AppendLine($"submittable: {(r.Submittable ? "yes" : "no")}{(r.Outcome == InterviewOutcome.Completed && !r.HasContent ? " (the record carries no covered topic)" : string.Empty)}");
        sb.AppendLine().AppendLine("== Invariants ==");
        foreach (var c in checks) sb.AppendLine($"[{(c.Passed ? "PASS" : "FAIL")}] {c.Id}: {c.Description}{(c.Detail.Length > 0 ? $" ({c.Detail})" : string.Empty)}");
        var d = r.Diagnostics;
        sb.AppendLine().AppendLine("== Run ==");
        sb.AppendLine($"outcome={Snake(r.Outcome)} end_reason={r.EndReason} interviewee_turns={d.IntervieweeTurns} model_calls={d.ModelCalls} probes={d.Probes} clarifications={d.Clarifications} redirects={d.Redirects} names_masked_turns={d.NamesMasked} injection_suspected_turns={d.InjectionSuspectedTurns} quotes_dropped={d.QuotesDropped} topics_covered={d.TopicsCovered}");
        return sb.ToString();
    }

    private static async Task<int> ListPersonas(TextWriter stdout)
    {
        foreach (var p in PersonaCatalog.All) await stdout.WriteLineAsync($"{p.Id,-18} {p.Title}: {p.Description}");
        return 0;
    }

    private static async Task<int> Print(TextWriter w, string line) { await w.WriteLineAsync(line); return 0; }

    private static async Task<int> Fail(TextWriter stderr, string message)
    {
        await stderr.WriteLineAsync(message);
        await stderr.WriteLineAsync("Run 'exit-interview --help' for usage.");
        return 2;
    }

    private static string Pretty(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Snake(Enum e) => ExitInterviewAgent.Agent.Tracing.SpanTags.Snake(e.ToString());
}
