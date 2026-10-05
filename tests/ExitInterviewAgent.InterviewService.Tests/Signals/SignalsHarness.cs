using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.InterviewService.Tests.Support;
using ExitInterviewAgent.Signals;
using Microsoft.Extensions.DependencyInjection;

namespace ExitInterviewAgent.InterviewService.Tests.Signals;

/// <summary>Seeds the real service through its real door (the submission endpoint, one account per record) and publishes by hand.</summary>
internal static class SignalsHarness
{
    public static readonly string[] Topics = ["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"];

    /// <summary>The background publisher is off in most tests so that the test, not a timer, decides when a snapshot is made.</summary>
    public static Dictionary<string, string?> ManualPublishing(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["Signals:Enabled"] = "false" };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }
        return settings;
    }

    public static JsonObject Record(string employer, int rating = 4, string tenure = "1y_3y", string? seniority = "mid", string? function = "engineering", string? quote = null)
        => TestRecords.Valid(employer, r =>
        {
            foreach (var topic in Topics)
            {
                r.Topic(topic)["rating"] = rating;
                if (quote is not null)
                {
                    r.SetQuotes(topic, quote);
                }
            }
            var context = new JsonObject { ["tenureBand"] = tenure };
            if (seniority is not null)
            {
                context["seniorityBand"] = seniority;
            }
            if (function is not null)
            {
                context["functionBand"] = function;
            }
            r["context"] = context;
        });

    /// <summary>Submits one record as a fresh account; returns the receipt code.</summary>
    public static async Task<string> SubmitAsync(TestHost host, JsonObject record)
    {
        var (response, body) = await host.Client(host.WebToken(TestRecords.NewSub())).PostAsync("/api/v1/submissions", TestRecords.Json(record)).ReadAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {body}");
        return TestRecords.ReceiptCodeOf(body);
    }

    public static async Task<List<string>> SubmitManyAsync(TestHost host, string employer, int count, Func<int, JsonObject>? make = null)
    {
        var receipts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            receipts.Add(await SubmitAsync(host, make?.Invoke(i) ?? Record(employer)));
        }
        return receipts;
    }

    /// <summary>Starts the next batch period and runs the publisher once, as the hosted service would.</summary>
    public static async Task<PublishOutcome> PublishNextBatchAsync(TestHost host)
    {
        host.Clock.Advance(TimeSpan.FromDays(1));
        return await host.InScopeAsync(sp => sp.GetRequiredService<SnapshotPublisher>().RunDueAsync(default));
    }

    public static async Task<(HttpResponseMessage Response, string Body)> GetAsync(TestHost host, string path, string? sub = null, Action<HttpRequestMessage>? configure = null)
    {
        var client = host.Client(host.WebToken(sub ?? "reader-account"));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        configure?.Invoke(request);
        var response = await client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    public static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var inner in PropertyNames(property.Value))
                    {
                        yield return inner;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var inner in PropertyNames(item))
                    {
                        yield return inner;
                    }
                }
                break;
        }
    }
}
