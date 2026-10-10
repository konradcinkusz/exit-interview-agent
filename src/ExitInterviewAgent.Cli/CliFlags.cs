namespace ExitInterviewAgent.Cli;

/// <summary>Every flag of every command, in one place, so an architecture test can assert that no secret has a flag.</summary>
internal static class CliFlags
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ByCommand { get; } = new Dictionary<string, IReadOnlySet<string>>
    {
        ["interview"] = InterviewCommand.Values.Union(InterviewCommand.Switches).ToHashSet(),
        ["submit"] = SubmitCommand.Values.Union(SubmitCommand.Switches).ToHashSet(),
        ["delete-receipt"] = DeleteReceiptCommand.Values.Union(DeleteReceiptCommand.Switches).ToHashSet(),
        ["providers"] = ProviderOptions.ValueFlags,
        ["tiles"] = TilesCommand.Values.Union(TilesCommand.Switches).ToHashSet(),
    };
}
