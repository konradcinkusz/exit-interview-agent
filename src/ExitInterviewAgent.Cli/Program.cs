using ExitInterviewAgent.Cli;

// Ctrl-C cancels the interview instead of killing the process mid-write: the interviewee "leaves", the transcript is discarded, nothing is written.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};
return await CliApp.RunAsync(args, CliHost.FromConsole(cancel.Token));
