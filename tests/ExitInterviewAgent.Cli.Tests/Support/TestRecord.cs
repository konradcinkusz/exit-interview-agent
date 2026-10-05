using System.Text.Json.Nodes;

namespace ExitInterviewAgent.Cli.Tests.Support;

/// <summary>A valid schema-v1 record, as <c>interview --out</c> would write it.</summary>
public static class TestRecord
{
    public const string CanaryQuote = "The canary quote about the quarterly review that never happened.";

    private const string Template = """
        {
          "schemaVersion": "1",
          "interviewId": "0f3c9a1e7b2d4c58a6e1903fd2b47c11",
          "employerRef": "acme-sp-zoo",
          "context": { "tenureBand": "1y_3y", "seniorityBand": "mid", "functionBand": "engineering" },
          "topics": {
            "onboarding": { "status": "covered", "rating": 2, "confidence": "medium", "quotes": ["The first two weeks nobody told me who to ask about access."] },
            "management": { "status": "covered", "rating": 1, "confidence": "high", "quotes": ["My manager [PERSON] cancelled every one-to-one.", "Feedback only came at the yearly review."] },
            "growth": { "status": "covered", "rating": null, "confidence": "low", "quotes": ["I wondered about growth, but I do not know whether it was possible."] },
            "pay_vs_promises": { "status": "covered", "rating": 2, "confidence": "high", "quotes": ["The offer said a review after six months; it never happened."] },
            "culture": { "status": "covered", "rating": 4, "confidence": "medium", "quotes": ["The team itself was great and helped each other."] },
            "reason_for_leaving": { "status": "covered", "rating": 3, "confidence": "high", "quotes": ["I left because the role I was promised never materialised."] }
          },
          "piiMasked": true,
          "interview": { "protocolVersion": "1.0", "language": "en", "aiDisclosed": true, "durationBand": "20m_40m", "turnBand": "20_40" }
        }
        """;

    public static JsonObject Valid(Action<JsonObject>? change = null)
    {
        var record = JsonNode.Parse(Template)!.AsObject();
        record["interviewId"] = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        record["topics"]!["culture"]!["quotes"] = new JsonArray(CanaryQuote);
        change?.Invoke(record);
        return record;
    }

    public static string Pretty(JsonObject record) => record.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
}
