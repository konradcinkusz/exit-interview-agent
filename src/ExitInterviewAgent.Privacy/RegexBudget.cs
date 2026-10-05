namespace ExitInterviewAgent.Privacy;

/// <summary>
/// The wall-clock budget of one regular-expression rule. Production keeps the 2 s default (a rule that exceeds it throws, and every caller
/// treats that as "do not submit"). The budget can be raised by an AppContext value so that tests do not depend on how fast, or how busy, the
/// machine is: a timing assertion is then the test's own, not the library's.
/// </summary>
internal static class RegexBudget
{
    public const string SettingName = "ExitInterviewAgent.Privacy.RegexTimeoutMilliseconds";
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan Timeout = Parse(AppContext.GetData(SettingName) as string);

    /// <summary>A whole number of milliseconds from 100 to 120000; anything else (including nothing) is the default.</summary>
    internal static TimeSpan Parse(string? value)
        => int.TryParse(value, out var ms) && ms is >= 100 and <= 120_000 ? TimeSpan.FromMilliseconds(ms) : Default;
}
