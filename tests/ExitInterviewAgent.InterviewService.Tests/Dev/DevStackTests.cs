using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Dev;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Tests.Dev;

/// <summary>The rules of the development stack (ADR-0079) without a host: who may mount the grant, and what <c>/health</c> says about the model.</summary>
public sealed class DevStackTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static BillingOptions Billing(string provider = "fake", int price = 1000, string? currency = "pln") => new()
    {
        Provider = provider,
        PriceMinorUnits = price,
        Currency = currency,
        SuccessUrl = "http://localhost:3000/interview",
        CancelUrl = "http://localhost:3000/interview",
    };

    [Theory]
    [InlineData("Development", "fake", 1000, "pln", true)]
    [InlineData("Production", "fake", 1000, "pln", false)] // the fake provider is refused in Production, and the grant with it
    [InlineData("Staging", "fake", 1000, "pln", false)]
    [InlineData("Development", "none", 1000, "pln", false)]
    [InlineData("Development", "stripe", 1000, "pln", false)]
    [InlineData("Development", "fake", 0, "pln", false)] // no price: checkout is not configured, so the grant is not mounted
    [InlineData("Development", "fake", 1000, null, false)]
    public void The_grant_is_mounted_only_in_development_with_the_fake_provider_and_a_complete_checkout(
        string environment, string provider, int price, string? currency, bool expected)
    {
        var billing = Billing(provider, price, currency);

        Assert.Equal(expected, DevStack.CreditGrantEnabled(new FakeEnvironment(environment), billing));
    }

    private static ServiceProvider Build(string environment, string? provider, string? model = null, string? apiKeyEnv = null, BillingOptions? billing = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Interviews:Provider"] = provider,
            ["Interviews:Model"] = model,
            ["Interviews:ApiKeyEnv"] = apiKeyEnv,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        var options = billing ?? Billing();
        services.AddSingleton(Options.Create(options));
        services.AddDevStack(config);
        return services.BuildServiceProvider();
    }

    private static IntegrationStatus Row(ServiceProvider services, string name) =>
        services.GetServices<IntegrationStatus>().Single(s => s.Name == name);

    [Fact]
    public void The_model_row_says_the_scripted_mock_is_running_and_is_degraded()
    {
        using var services = Build("Development", provider: "mock", model: "scripted");

        var row = Row(services, "interview-model");

        Assert.False(row.Configured);
        Assert.Contains("MOCK", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_row_is_degraded_while_the_key_variable_is_unset_and_names_no_value()
    {
        var variable = "EIA_TEST_KEY_UNSET_" + Guid.NewGuid().ToString("N");
        using var services = Build("Development", provider: "anthropic", model: "test-model", apiKeyEnv: variable);

        var row = Row(services, "interview-model");

        Assert.False(row.Configured);
        Assert.Contains("is not set", row.Detail, StringComparison.Ordinal);
        Assert.Contains(variable, row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_row_is_configured_with_a_key_and_never_reports_the_key()
    {
        var variable = "EIA_TEST_KEY_SET_" + Guid.NewGuid().ToString("N");
        var secret = "test-secret-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            using var services = Build("Development", provider: "anthropic", model: "test-model", apiKeyEnv: variable);

            var row = Row(services, "interview-model");

            Assert.True(row.Configured);
            Assert.DoesNotContain(secret, row.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(variable, row.Detail, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void The_model_row_is_degraded_with_no_provider_and_says_interviews_answer_503()
    {
        using var services = Build("Development", provider: null);

        var row = Row(services, "interview-model");

        Assert.False(row.Configured);
        Assert.Contains("503", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_grant_row_is_mounted_in_development_and_absent_by_design_in_production()
    {
        using var development = Build("Development", provider: "mock");
        using var production = Build("Production", provider: "mock", billing: Billing());

        var mounted = Row(development, "dev-credit-grant");
        var absent = Row(production, "dev-credit-grant");

        Assert.Contains("is mounted", mounted.Detail, StringComparison.Ordinal);
        // Off by design is not a degradation: the row stays configured so that /health does not report a healthy production as broken.
        Assert.True(absent.Configured);
        Assert.Contains("by design", absent.Detail, StringComparison.Ordinal);
    }
}
