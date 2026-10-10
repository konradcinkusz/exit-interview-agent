using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Interviews.CostControls;
using ExitInterviewAgent.InterviewService.Tests.Interviews;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.CostControls;

/// <summary>
/// W4 (web-app-plan §2, ADR-0078): the spend metrics are counts and sums only. Each test listens to THIS host's meter, so
/// parallel tests do not see each other. Every measurement must carry no tags at all: no account, no session, no content.
/// </summary>
public sealed class CostMetricsTests
{
    private static readonly string[] Polish =
        ["Yes, I consent.", .. Enumerable.Range(1, 16).Select(i => $"Na temat {i} proces trwał 3 tygodnie i nikt nie wyjaśnił dlaczego, bo nie było rozmowy.")];

    /// <summary>What one test's host reported: sums and counts per instrument, and how many measurements carried a tag.</summary>
    private sealed class Measured : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, long> _sums = new();
        private readonly ConcurrentDictionary<string, int> _counts = new();
        private int _tagged;

        public Measured(CostHost host)
        {
            var meter = host.Services.GetRequiredService<CostMetrics>().Meter;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (tags.Length > 0) Interlocked.Increment(ref _tagged);
                _sums.AddOrUpdate(instrument.Name, value, (_, sum) => sum + value);
                _counts.AddOrUpdate(instrument.Name, 1, (_, n) => n + 1);
            });
            _listener.Start();
        }

        public long Sum(string name) => _sums.TryGetValue(name, out var v) ? v : 0;

        public int Count(string name) => _counts.TryGetValue(name, out var v) ? v : 0;

        public int Tagged => _tagged;

        public void Dispose() => _listener.Dispose();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task A_start_counts_as_started()
    {
        using var host = new CostHost();
        using var measured = new Measured(host);
        var client = host.Verified("account-metrics-start");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" })).StatusCode);

        Assert.Equal(1, measured.Sum("interviews_started"));
    }

    [Fact]
    public async Task A_refused_start_is_not_counted_as_started()
    {
        using var host = new CostHost();
        using var measured = new Measured(host);

        await host.As("account-metrics-refused").PostAsJsonAsync("/api/v1/interviews", new { language = "xx", tenure = "1y_3y" });

        Assert.Equal(0, measured.Sum("interviews_started"));
    }

    [Fact]
    public async Task A_rate_limited_request_counts_as_rate_limited()
    {
        using var host = new CostHost().With(("Interviews:RateLimits:StartsPerAccountPerHour", "1"));
        using var measured = new Measured(host);
        var client = host.Verified("account-metrics-limited");
        await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });

        var refused = await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(1, measured.Sum("rate_limited"));
    }

    [Fact]
    public async Task An_unverified_email_counts_as_rejected_email_unverified()
    {
        using var host = new CostHost().With(("Interviews:RequireVerifiedEmail", "true"));
        using var measured = new Measured(host);

        var refused = await host.As("account-metrics-email").PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(1, measured.Sum("rejected_email_unverified"));
    }

    [Fact]
    public async Task A_completed_interview_counts_completed_and_its_usage()
    {
        using var host = new CostHost();
        using var measured = new Measured(host);
        var client = host.Verified("account-metrics-completed");
        var id = (await Json(await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" }))).GetProperty("id").GetString()!;
        foreach (var line in Polish)
        {
            await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = line });
        }
        await WaitForTerminal(client, id);
        // The status turns terminal before the counters are written at the end of the run: wait for the counters, not the status.
        await Eventually(() => measured.Sum("interviews_completed") == 1 && measured.Count("model_calls") >= 1);

        Assert.Equal(1, measured.Sum("interviews_completed"));
        Assert.True(measured.Count("tokens_estimated") >= 1, "The estimated tokens were not recorded.");
        Assert.True(measured.Sum("tokens_estimated") > 0);
        Assert.True(measured.Count("model_calls") >= 1, "The model calls were not recorded.");
        Assert.True(measured.Sum("model_calls") > 0);
    }

    [Fact]
    public async Task A_withdrawn_interview_counts_withdrawn()
    {
        using var host = new CostHost();
        using var measured = new Measured(host);
        var client = host.Verified("account-metrics-withdrawn");
        var id = (await Json(await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" }))).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/interviews/{id}")).StatusCode);

        Assert.Equal(1, measured.Sum("interviews_withdrawn"));
        Assert.Equal(0, measured.Sum("interviews_completed"));
    }

    [Fact]
    public async Task A_provider_failure_counts_failed()
    {
        using var host = new CostHost { ModelOverride = new FailingChatClient() };
        using var measured = new Measured(host);
        var client = host.Verified("account-metrics-failed");
        var id = (await Json(await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" }))).GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/api/v1/interviews/{id}/reply", new { text = "Yes, I consent." });

        Assert.Equal(1, measured.Sum("interviews_failed"));
    }

    [Fact]
    public async Task No_measurement_carries_a_tag_so_no_account_or_content_can_reach_a_metric()
    {
        using var host = new CostHost();
        using var measured = new Measured(host);
        var client = host.Verified("account-metrics-untagged");
        var id = (await Json(await client.PostAsJsonAsync("/api/v1/interviews", new { language = "pl", tenure = "1y_3y" }))).GetProperty("id").GetString()!;
        await client.DeleteAsync($"/api/v1/interviews/{id}");

        Assert.True(measured.Sum("interviews_started") >= 1, "The check ran on nothing.");
        Assert.Equal(0, measured.Tagged);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail("The counters did not reach the expected value.");
    }

    private static async Task WaitForTerminal(HttpClient client, string id)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var status = (await Json(await client.GetAsync($"/api/v1/interviews/{id}"))).GetProperty("status").GetString();
            if (status is "completed" or "stopped" or "failed") return;
            await Task.Delay(10);
        }
        Assert.Fail("The interview did not reach a terminal status.");
    }
}
