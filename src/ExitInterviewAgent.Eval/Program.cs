using ExitInterviewAgent.Eval.Cli;

// Real providers register their factories here once they exist (T6): ProviderFactories.Register("anthropic", settings => ...).
// The harness never constructs a provider itself; see docs/eval/README.md ("Plugging in model providers").
return await EvalCli.RunAsync(args, Console.Out, Console.Error);
