using System.Globalization;
using System.Text;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// The one submit path, used by <c>exit-interview submit</c> and by the offer at the end of <c>interview</c>. Order, and why:
/// check the record here (fail closed) → refuse early if the receipt file cannot be created (a receipt is shown once; losing it for want of
/// a writable path would be the worst outcome) → show exactly what will leave the computer, with the record verbatim → a typed confirmation
/// → only then ask for the ticket (so it is not typed, or read from the environment, for something the user is about to decline) → one request.
/// </summary>
internal static class SubmitFlow
{
    public static class Exit
    {
        public const int Ok = 0, Usage = 2, NotConfirmed = 3, RecordInvalid = 4, Rejected = 5, NetworkFailure = 6, Cancelled = 130;
    }

    public const string ConfirmWord = "submit";
    public const string TicketEnv = "EXIT_INTERVIEW_TICKET";
    public const string ReceiptEnv = "EXIT_INTERVIEW_RECEIPT_CODE";

    public static async Task<int> RunAsync(byte[] record, CliHost host, ServerUrl server, bool assumeYes, string? saveReceiptPath)
    {
        var o = host.Out;
        var check = LocalRecordCheck.Run(record);
        if (!check.Ok)
        {
            await host.Err.WriteLineAsync("The record cannot be submitted. Nothing was sent.").ConfigureAwait(false);
            foreach (var p in check.Problems) await host.Err.WriteLineAsync("  - " + p).ConfigureAwait(false);
            return Exit.RecordInvalid;
        }

        if (saveReceiptPath is not null && ReceiptFile.Problem(saveReceiptPath) is { } receiptProblem)
        {
            await host.Err.WriteLineAsync(receiptProblem + " Nothing was sent.").ConfigureAwait(false);
            return Exit.Usage;
        }

        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("== What will leave this computer ==").ConfigureAwait(false);
        await o.WriteLineAsync($"To:      {server.Display}  (one POST; redirects are never followed)").ConfigureAwait(false);
        await o.WriteLineAsync($"Sent:    exactly the record below ({record.Length.ToString(CultureInfo.InvariantCulture)} bytes) and your one-time ticket, in a request header. Nothing else: no transcript, no name, no file names, no model or provider details.").ConfigureAwait(false);
        await o.WriteLineAsync("Checks:  the record passed the schema check, the AI-disclosure check and the personal-data check on this computer.").ConfigureAwait(false);
        await o.WriteLineAsync("Know:    the server sees your network address and the time, as any server does, and the ticket ties this request to your web account at that instant (a known limit, see docs/architecture/cli-submission.md).").ConfigureAwait(false);
        await o.WriteLineAsync("Result:  you get a receipt code, shown once. It is the only way to delete the record later; nobody can look it up for you.").ConfigureAwait(false);
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("== The record that will be sent ==").ConfigureAwait(false);
        await o.WriteLineAsync(Encoding.UTF8.GetString(record).TrimEnd()).ConfigureAwait(false);
        await o.WriteLineAsync().ConfigureAwait(false);

        if (assumeYes) await o.WriteLineAsync("(--yes given: not asking. That flag is for tests and scripts you control; it removes the last chance to look.)").ConfigureAwait(false);
        else
        {
            await o.WriteAsync($"Type \"{ConfirmWord}\" to send this record (anything else keeps it on this computer): ").ConfigureAwait(false);
            string? answer;
            try { answer = await host.In.ReadLineAsync(host.Cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { answer = null; }
            if (!string.Equals(answer?.Trim(), ConfirmWord, StringComparison.OrdinalIgnoreCase))
            {
                await o.WriteLineAsync().ConfigureAwait(false);
                await o.WriteLineAsync("Not confirmed. Nothing was sent.").ConfigureAwait(false);
                return Exit.NotConfirmed;
            }
        }

        using var ticket = await SecretInput.ReadAsync(host, TicketEnv, "ticket", "Submission ticket (from the web /cli page; typing is hidden): ").ConfigureAwait(false);
        if (ticket is null)
        {
            await o.WriteLineAsync("No ticket given. Nothing was sent. Mint one on the web panel's /cli page.").ConfigureAwait(false);
            return Exit.NotConfirmed;
        }

        await o.WriteLineAsync("Sending...").ConfigureAwait(false);
        using var client = new SubmissionClient(server, host.Http);
        var result = await client.SubmitAsync(record, ticket, host.Cancellation).ConfigureAwait(false);
        switch (result)
        {
            case CallResult.Accepted accepted:
                using (accepted.ReceiptCode) return await ShowReceiptAsync(host, server, accepted.ReceiptCode, saveReceiptPath).ConfigureAwait(false);
            case CallResult.Rejected rejected:
                await host.Err.WriteLineAsync($"Not submitted (HTTP {rejected.Status.ToString(CultureInfo.InvariantCulture)}, {rejected.Code}).").ConfigureAwait(false);
                foreach (var line in SubmitMessages.Explain(rejected)) await host.Err.WriteLineAsync(line).ConfigureAwait(false);
                return Exit.Rejected;
            case CallResult.Failed failed:
                await host.Err.WriteLineAsync("Not submitted.").ConfigureAwait(false);
                foreach (var line in SubmitMessages.Explain(failed, server, submission: true)) await host.Err.WriteLineAsync(line).ConfigureAwait(false);
                return failed.Reason == Failure.Cancelled ? Exit.Cancelled : Exit.NetworkFailure;
            default:
                throw new InvalidOperationException("Unexpected result.");
        }
    }

    private static async Task<int> ShowReceiptAsync(CliHost host, ServerUrl server, RedactedSecret receipt, string? savePath)
    {
        var o = host.Out;
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("Submitted. Your record was accepted.").ConfigureAwait(false);
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("== Your receipt code (shown once) ==").ConfigureAwait(false);
        await o.WriteLineAsync(receipt.Reveal()).ConfigureAwait(false);
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("This is the only way to delete this record. The service stores only a fingerprint of the code, it cannot find or re-issue it for you, and it is not shown again.").ConfigureAwait(false);
        await o.WriteLineAsync("Anyone who has the code can delete the record; it does not say who you are. Keep it somewhere private (a password manager).").ConfigureAwait(false);
        await o.WriteLineAsync().ConfigureAwait(false);
        await o.WriteLineAsync("To delete the record later (the code is asked for, hidden, never given as an argument):").ConfigureAwait(false);
        await o.WriteLineAsync($"  exit-interview delete-receipt --server {server.Display}").ConfigureAwait(false);

        if (savePath is null) await o.WriteLineAsync("Not saved anywhere: use --save-receipt <file> next time if you want a file.").ConfigureAwait(false);
        else
        {
            try
            {
                var full = ReceiptFile.Write(savePath, receipt.Reveal());
                await o.WriteLineAsync($"Saved to {full} (readable only by you where the system supports it). Whoever can read that file can delete the record.").ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await host.Err.WriteLineAsync("The receipt code could not be saved to the file you named. It is shown above: copy it now, it will not be shown again.").ConfigureAwait(false);
            }
        }
        return Exit.Ok;
    }
}

/// <summary>A receipt file: created with mode 0600 from the first byte (never world-readable, even briefly), never overwrites.</summary>
internal static class ReceiptFile
{
    /// <returns>Why this path cannot be used, or null. Checked before anything is sent.</returns>
    public static string? Problem(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full)) return "--save-receipt names a directory, not a file.";
            if (File.Exists(full)) return "--save-receipt: that file already exists and will not be overwritten.";
            if (!Directory.Exists(Path.GetDirectoryName(full))) return "--save-receipt: the folder for that file does not exist.";
            return null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return "--save-receipt is not a usable path."; }
    }

    public static string Write(string path, string code)
    {
        var full = Path.GetFullPath(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(full, options);
        stream.Write(Encoding.ASCII.GetBytes(code + "\n"));
        return full;
    }
}
