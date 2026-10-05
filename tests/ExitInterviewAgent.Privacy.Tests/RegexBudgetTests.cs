using System.Runtime.CompilerServices;

namespace ExitInterviewAgent.Privacy.Tests;

/// <summary>
/// The adversarial-input tests measure the detector's own cost against their own wall-clock assertion. The library's per-rule budget (2 s in production)
/// is raised for this test process, because on a loaded shared CI runner it fired before the test could assert anything (the obfuscated-email rule costs
/// about 3 microseconds a character unloaded, and more than eight times that under contention). The default is pinned below so production keeps it.
/// </summary>
internal static class TestProcessSettings
{
    [ModuleInitializer]
    internal static void RaiseTheRuleBudget() => AppContext.SetData(RegexBudget.SettingName, "60000");
}

public sealed class RegexBudgetTests
{
    [Fact]
    public void The_production_default_is_two_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), RegexBudget.Default);
        Assert.Equal(RegexBudget.Default, RegexBudget.Parse(null));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("120001")]
    [InlineData("-5")]
    public void A_missing_or_out_of_range_value_is_the_default(string value)
        => Assert.Equal(RegexBudget.Default, RegexBudget.Parse(value));

    [Fact]
    public void A_value_in_range_is_used()
        => Assert.Equal(TimeSpan.FromSeconds(60), RegexBudget.Parse("60000"));

    [Fact]
    public void The_test_process_runs_with_the_raised_budget()
        => Assert.Equal(TimeSpan.FromSeconds(60), RegexBudget.Timeout); // proves the module initializer ran before the first rule was built
}
