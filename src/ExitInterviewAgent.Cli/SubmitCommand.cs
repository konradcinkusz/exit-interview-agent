namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview submit</c>: sends a record file that <c>interview --out</c> wrote, with a one-time ticket minted on the web panel.
/// No OAuth, no login in the CLI (brief section 4). The ticket has no flag: it comes from EXIT_INTERVIEW_TICKET, a hidden prompt or standard input.
/// </summary>
internal static class SubmitCommand
{
    internal static readonly IReadOnlySet<string> Values = new HashSet<string> { "--record", "--server", "--save-receipt" };
    internal static readonly IReadOnlySet<string> Switches = new HashSet<string> { "--yes" };

    public static async Task<int> RunAsync(string[] args, CliHost host)
    {
        var flags = Flags.Parse(args, Values, Switches);
        var recordArg = flags["--record"] ?? throw new ArgumentException("--record <file> is required (the record.json that 'interview --out' wrote; '-' reads it from standard input).");
        var server = ServerUrl.Resolve(flags["--server"], host.Env);
        var yes = flags.Has("--yes");

        byte[] record;
        if (recordArg == "-")
        {
            // Standard input is the record, so it cannot also answer questions: the confirmation and the ticket must come from elsewhere.
            if (!yes) throw new ArgumentException("With --record - the record is read from standard input, so there is no way to ask you to confirm. Add --yes (it is for scripts you control), or give a file.");
            if (string.IsNullOrWhiteSpace(host.Env(SubmitFlow.TicketEnv))) throw new ArgumentException($"With --record - standard input is taken by the record, so the ticket must be in {SubmitFlow.TicketEnv}.");
            record = await ReadBoundedAsync(host.In.ReadToEndAsync(host.Cancellation)).ConfigureAwait(false);
        }
        else
        {
            if (!File.Exists(recordArg)) throw new ArgumentException("The file given with --record does not exist.");
            if (new FileInfo(recordArg).Length > ExitInterviewAgent.Records.RecordLimits.Default.MaxPayloadBytes) throw new ArgumentException("The file given with --record is larger than any record can be.");
            record = await File.ReadAllBytesAsync(recordArg, host.Cancellation).ConfigureAwait(false);
        }

        return await SubmitFlow.RunAsync(record, host, server, yes, flags["--save-receipt"]).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(Task<string> read)
    {
        var text = await read.ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetBytes(text);
    }
}
