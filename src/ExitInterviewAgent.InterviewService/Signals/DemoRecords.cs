using System.Text.Json.Nodes;

namespace ExitInterviewAgent.InterviewService.Signals;

/// <summary>One synthetic record and the synthetic account that submits it.</summary>
public sealed record DemoRecord(string Sub, string Employer, byte[] Json);

/// <summary>
/// Synthetic records for local demos (DEMO-DATA-AND-SEEDING): invented employers under the reserved prefix, invented sentences, and
/// invented accounts; nothing here describes a real company or person (brief section 2). Deterministic for a given seed, so a screenshot
/// can be reproduced. Each employer is a small story chosen to show a rule of the aggregation, not a realistic workforce:
/// <list type="bullet">
///   <item><c>demo-harbor-logistics</c>: 42 records, a management gap and a good culture, every band populated (all cuts published).</item>
///   <item><c>demo-birchwood-software</c>: 18 records, polarised pay and a topic many people skipped (coverage drops, the interval stays wide).</item>
///   <item><c>demo-quill-and-ink</c>: exactly five records (k is met: shown).</item>
///   <item><c>demo-tiny-bakery</c>: four records (k - 1: nothing shown, and nothing says why).</item>
///   <item><c>demo-skewed-bands</c>: 26 records of which a tenure band holds three (the whole tenure cut is withheld, the others are not).</item>
///   <item><c>demo-unanimous-works</c>: 12 records that all rate culture 5 (shown, with a wide interval rather than a point).</item>
/// </list>
/// </summary>
public static class DemoRecords
{
    /// <summary>The reserved namespace. Seeding writes only under it and reset removes only under it.</summary>
    public const string Prefix = "demo-";

    private static readonly string[] Quotes =
    [
        "Synthetic demo statement about how the first weeks felt.",
        "Synthetic demo statement about day-to-day feedback from the team lead.",
        "Synthetic demo statement about opportunities to move into a bigger role.",
        "Synthetic demo statement about the gap between the offer and what happened.",
        "Synthetic demo statement about how colleagues treated each other.",
        "Synthetic demo statement about the main reason for deciding to leave.",
    ];

    private static readonly string[] Topics = ["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"];

    private sealed record Story(string Employer, int Count, Func<int, (string Tenure, string? Seniority, string? Function)> Bands, Func<int, string, Random, int?> Rating);

    public static IReadOnlyList<DemoRecord> Generate(int seed)
    {
        var rng = new Random(seed);
        string[] tenures = ["1y_3y", "3y_5y", "5y_10y"];
        var stories = new[]
        {
            new Story("demo-harbor-logistics", 42,
                i => (tenures[i % 3], new[] { "junior", "mid", "senior" }[i % 3], i % 2 == 0 ? "engineering" : "operations_support"),
                (_, topic, r) => Draw(r, topic switch { "management" => 2.0, "culture" => 4.1, "onboarding" => 3.2, "pay_vs_promises" => 2.5, _ => 3.0 }, 0.8, 0.08)),
            new Story("demo-birchwood-software", 18,
                i => (tenures[i % 2], i % 2 == 0 ? "mid" : "senior", "engineering"),
                (i, topic, r) => topic switch
                {
                    "pay_vs_promises" => i % 2 == 0 ? 1 : 5,
                    "growth" => r.NextDouble() < 0.45 ? null : Draw(r, 3.4, 1.0, 0),
                    _ => Draw(r, 3.3, 0.9, 0.05),
                }),
            new Story("demo-quill-and-ink", 5, _ => ("1y_3y", "mid", "corporate_functions"), (_, _, r) => Draw(r, 3.8, 0.7, 0)),
            new Story("demo-tiny-bakery", 4, _ => ("1y_3y", "junior", "other"), (_, _, r) => Draw(r, 2.5, 1.0, 0)),
            new Story("demo-skewed-bands", 26,
                i => (i < 12 ? "1y_3y" : i < 23 ? "3y_5y" : "gt_10y", i % 2 == 0 ? "mid" : "senior", "product_design"),
                (_, _, r) => Draw(r, 3.6, 0.9, 0.05)),
            new Story("demo-unanimous-works", 12,
                i => (tenures[i % 3], new[] { "junior", "mid", "senior" }[i % 3], i % 2 == 0 ? "sales_marketing" : "engineering"),
                (_, topic, r) => topic == "culture" ? 5 : Draw(r, 3.5, 1.0, 0.05)),
        };

        var records = new List<DemoRecord>();
        var account = 0;
        foreach (var story in stories)
        {
            for (var i = 0; i < story.Count; i++)
            {
                var (tenure, seniority, function) = story.Bands(i);
                var context = new JsonObject { ["tenureBand"] = tenure };
                if (seniority is not null)
                {
                    context["seniorityBand"] = seniority;
                }
                if (function is not null)
                {
                    context["functionBand"] = function;
                }

                var topics = new JsonObject();
                for (var t = 0; t < Topics.Length; t++)
                {
                    var rating = story.Rating(i, Topics[t], rng);
                    var skipped = rating is null && Topics[t] == "growth" && story.Employer == "demo-birchwood-software";
                    topics[Topics[t]] = skipped
                        ? new JsonObject { ["status"] = "no_data", ["rating"] = null, ["confidence"] = null, ["quotes"] = new JsonArray() }
                        : new JsonObject
                        {
                            ["status"] = "covered",
                            ["rating"] = rating,
                            ["confidence"] = new[] { "low", "medium", "high" }[rng.Next(3)],
                            ["quotes"] = new JsonArray(Quotes[t]),
                        };
                }

                var record = new JsonObject
                {
                    ["schemaVersion"] = "1",
                    ["interviewId"] = Convert.ToHexStringLower(NextBytes(rng, 16)),
                    ["employerRef"] = story.Employer,
                    ["context"] = context,
                    ["topics"] = topics,
                    ["piiMasked"] = true,
                    ["interview"] = new JsonObject
                    {
                        ["protocolVersion"] = "1.0",
                        ["language"] = "en",
                        ["aiDisclosed"] = true,
                        ["durationBand"] = "20m_40m",
                        ["turnBand"] = "20_40",
                    },
                };
                records.Add(new DemoRecord($"demo-account-{++account:D4}", story.Employer, System.Text.Encoding.UTF8.GetBytes(record.ToJsonString())));
            }
        }
        return records;
    }

    private static int? Draw(Random rng, double mean, double sd, double noData)
    {
        if (rng.NextDouble() < noData)
        {
            return null;
        }
        var gaussian = Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
        return Math.Clamp((int)Math.Round(mean + sd * gaussian), 1, 5);
    }

    private static byte[] NextBytes(Random rng, int count)
    {
        var bytes = new byte[count];
        rng.NextBytes(bytes);
        return bytes;
    }
}
