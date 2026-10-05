using System.Globalization;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// <c>exit-interview delete-receipt</c>: deletes the record behind a receipt code. The code comes from EXIT_INTERVIEW_RECEIPT_CODE, a hidden
/// prompt or standard input, never from an argument. The service answers 204 for every well-formed code, found or not (ADR-0029), so the
/// wording here is "if it existed, it is deleted now" and never "deleted".
/// </summary>
internal static class DeleteReceiptCommand
{
    internal static readonly IReadOnlySet<string> Values = new HashSet<string> { "--server" };
    internal static readonly IReadOnlySet<string> Switches = new HashSet<string>();

    public static async Task<int> RunAsync(string[] args, CliHost host)
    {
        var flags = Flags.Parse(args, Values, Switches);
        var server = ServerUrl.Resolve(flags["--server"], host.Env);

        using var code = await SecretInput.ReadAsync(host, SubmitFlow.ReceiptEnv, "receipt code", "Receipt code (typing is hidden): ").ConfigureAwait(false);
        if (code is null)
        {
            await host.Out.WriteLineAsync("No receipt code given. Nothing was sent.").ConfigureAwait(false);
            return SubmitFlow.Exit.NotConfirmed;
        }

        using var client = new SubmissionClient(server, host.Http);
        var result = await client.DeleteReceiptAsync(code, host.Cancellation).ConfigureAwait(false);
        switch (result)
        {
            case CallResult.Deleted:
                await host.Out.WriteLineAsync("Done. If a record with this receipt code existed, it is deleted now.").ConfigureAwait(false);
                await host.Out.WriteLineAsync("The service gives the same answer for every well-formed code, so this does not confirm that one existed: a code with a typo that still looks valid gets the same answer. Deleting does not reopen your one submission for that employer.").ConfigureAwait(false);
                return SubmitFlow.Exit.Ok;
            case CallResult.Rejected rejected:
                await host.Err.WriteLineAsync($"Not deleted (HTTP {rejected.Status.ToString(CultureInfo.InvariantCulture)}, {rejected.Code}).").ConfigureAwait(false);
                foreach (var line in SubmitMessages.Explain(rejected)) await host.Err.WriteLineAsync(line).ConfigureAwait(false);
                return SubmitFlow.Exit.Rejected;
            case CallResult.Failed failed:
                await host.Err.WriteLineAsync("Not deleted.").ConfigureAwait(false);
                foreach (var line in SubmitMessages.Explain(failed, server, submission: false)) await host.Err.WriteLineAsync(line).ConfigureAwait(false);
                return failed.Reason == Failure.Cancelled ? SubmitFlow.Exit.Cancelled : SubmitFlow.Exit.NetworkFailure;
            default:
                throw new InvalidOperationException("Unexpected result.");
        }
    }
}
