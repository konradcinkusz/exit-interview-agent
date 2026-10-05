namespace ExitInterviewAgent.Providers.Tests;

/// <summary>Tests that change process environment variables run one at a time.</summary>
[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;
