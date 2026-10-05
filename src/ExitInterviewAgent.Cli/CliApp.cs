using System.Globalization;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Personas;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview</c>: <c>demo</c> runs a whole simulated interview offline with the scripted mock model and a
/// persona, <c>personas</c> lists them. No network, no credentials, no submission. Exit code: 0 all invariants hold,
/// 1 an invariant failed, 2 usage error.
/// </summary>
public static class CliApp
{
    public const string Usage = """
        exit-interview: offline demo of the exit interview agent (simulated personas, scripted mock model, no network)

        Usage:
          exit-interview demo --persona <id> [--seed <n>] [--out <dir>]
          exit-interview personas
          exit-interview --help | --version

        demo      Runs a full interview offline and prints the masked transcript, the record, the validation result
                  and an invariant report. --seed makes it reproducible (default 1). --out writes transcript.txt,
                  record.json and report.txt into the directory (no transcript or record exists when consent is withdrawn).
        personas  Lists the simulated interviewees.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { await stdout.WriteLineAsync(Usage); return args.Length == 0 ? 2 : 0; }
            return args[0] switch
            {
                "--version" => await Print(stdout, $"exit-interview, interview protocol {InterviewProtocol.Current.ProtocolVersion}"),
                "personas" => await ListPersonas(stdout),
                "demo" => await Demo(args[1..], stdout, stderr),
                _ => await Fail(stderr, $"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException e)
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
