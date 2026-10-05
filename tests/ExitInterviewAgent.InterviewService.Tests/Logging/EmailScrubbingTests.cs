using System.Net;
using System.Net.Http.Headers;
using ExitInterviewAgent.InterviewService.Infrastructure.Logging;
using ExitInterviewAgent.InterviewService.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.InterviewService.Tests.Logging;

/// <summary>Brief §6: no PII in any log or trace. The address below is reserved-invalid and appears nowhere else.</summary>
public sealed class EmailScrubbingTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private const string Email = "jane.doe+exit@example.invalid";

    private (WebApplicationFactory, CaptureLoggerProvider) Host()
    {
        var capture = new CaptureLoggerProvider();
        var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Logging:LogLevel:Default", "Trace");
            b.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace"); // appsettings quiets it; this test needs the request logs
            b.ConfigureLogging(l => l.AddProvider(capture));
        });
        return (new WebApplicationFactory(host), capture);
    }

    // Thin alias so the tuple above stays readable.
    private sealed record WebApplicationFactory(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Inner)
    {
        public IServiceProvider Services => Inner.Services;
        public HttpClient CreateClient() => Inner.CreateClient();
    }

    [Fact]
    public void Message_arguments_and_exceptions_are_scrubbed_before_any_provider_sees_them()
    {
        var (host, capture) = Host();
        var logger = host.Services.GetRequiredService<ILogger<EmailScrubbingTests>>();

        logger.LogWarning("could not reach {Contact} about {Topic}", Email, "pay");
        logger.LogError(new InvalidOperationException($"mailbox {Email} is full"), "delivery failed for {Contact}", Email);

        var all = string.Join('\n', capture.Lines);
        Assert.DoesNotContain(Email, all);
        Assert.DoesNotContain("@", all);
        Assert.Contains(LogScrubber.Replacement, all);
        Assert.Contains("about pay", all); // the rest of the message survives
    }

    [Fact]
    public async Task A_request_carrying_an_address_in_its_url_is_not_logged_with_it()
    {
        var (host, capture) = Host();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.MintToken("account-123"));

        var response = await client.GetAsync($"/api/v1/me?email={Email}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var all = string.Join('\n', capture.Lines);
        Assert.Contains("/api/v1/me", all); // the framework did log the request, so the assertion below is not vacuous
        Assert.DoesNotContain(Email, all);
        Assert.DoesNotContain("jane.doe", all);
    }

    [Fact]
    public async Task A_token_with_an_email_claim_never_puts_the_address_in_a_log()
    {
        var (host, capture) = Host();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.NewMcpToken().With(t => t.Email = Email).Build());

        await client.SendAsync(McpWire.ListTools());

        Assert.DoesNotContain("jane.doe", string.Join('\n', capture.Lines));
    }

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("first.last+tag@sub.example.invalid", true)]
    [InlineData("no address here", false)]
    [InlineData("handle @mention and 5@3", false)]
    public void Scrub_replaces_addresses_and_leaves_other_text(string input, bool changes)
        => Assert.Equal(changes, LogScrubber.Scrub(input) != input);
}
