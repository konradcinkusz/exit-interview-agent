using ExitInterviewAgent.ServiceDefaults;

namespace ExitInterviewAgent.InterviewService.Tests.Kernel;

public sealed class ConnectionStringTests
{
    [Fact]
    public void Flycast_host_is_rewritten_to_internal_and_a_cold_start_timeout_is_added()
    {
        var result = DatabaseProviderExtensions.NormalizeConnectionString("Host=db.flycast;Database=interviewdb");

        Assert.Equal("Host=db.internal;Database=interviewdb;Timeout=30", result);
    }

    [Fact]
    public void Explicit_timeout_is_left_alone()
    {
        var result = DatabaseProviderExtensions.NormalizeConnectionString("Host=db.internal;Timeout=5");

        Assert.Equal("Host=db.internal;Timeout=5", result);
    }

    [Theory]
    [InlineData(null, null, 25)]
    [InlineData(-4, 0, 1)]
    [InlineData(3, 2_000_000, 100)]
    public void List_inputs_are_clamped(int? page, int? limit, int expectedLimit)
    {
        var (p, l) = ApiExtensions.ClampPage(page, limit);

        Assert.True(p >= 1);
        Assert.Equal(expectedLimit, l);
    }
}
