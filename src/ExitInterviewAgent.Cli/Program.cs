using System.Text;
using ExitInterviewAgent.Cli;

// A legacy Windows console (code page 437/852) drops Polish letters on output and on input; UTF-8 keeps them.
try
{
    Console.OutputEncoding = new UTF8Encoding(false);
    Console.InputEncoding = new UTF8Encoding(false);
}
catch (Exception e) when (e is IOException or PlatformNotSupportedException or UnauthorizedAccessException) { }

// Ctrl-C cancels the interview instead of killing the process mid-write: the interviewee "leaves", the transcript is discarded, nothing is written.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};
return await CliApp.RunAsync(args, CliHost.FromConsole(cancel.Token));
