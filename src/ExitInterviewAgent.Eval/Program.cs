using ExitInterviewAgent.Eval;
using ExitInterviewAgent.Eval.Cli;

// The real providers register here (ADR-0032); the harness itself never constructs one. A profile with no environment still reports skipped:no-credential.
ProviderRegistration.RegisterAll();
return await EvalCli.RunAsync(args, Console.Out, Console.Error);
