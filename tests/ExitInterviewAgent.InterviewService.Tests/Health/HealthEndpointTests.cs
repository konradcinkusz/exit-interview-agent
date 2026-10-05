using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Tests.Support;

namespace ExitInterviewAgent.InterviewService.Tests.Health;

public sealed class HealthEndpointTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Health_reports_every_optional_integration_and_marks_unconfigured_ones_degraded()
    {
        var client = factory.CreateClient();

        // Readiness stays unhealthy until the schema hosted service has run; poll rather than sleep.
        HttpResponseMessage response;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            response = await client.GetAsync("/health");
        } while (response.StatusCode != HttpStatusCode.OK && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Degraded", body.GetProperty("status").GetString());

        var integrations = body.GetProperty("integrations").EnumerateArray()
            .ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("configured").GetBoolean());
        Assert.False(integrations["database"]);          // InMemory: no connection string
        Assert.False(integrations["telemetry-export"]);  // no OTLP endpoint
        Assert.False(integrations["identity"]);          // no Jwt:Authority
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("schema").GetString());
    }

    [Fact]
    public async Task Alive_answers_200_without_touching_dependencies()
    {
        var response = await factory.CreateClient().GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
