using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>Valid schema-v1 records and unique identities for tests. The shared InMemory store is avoided, but identities are unique anyway.</summary>
public static class TestRecords
{
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

    // Letters only: a random run of digits in an employer slug can look like a phone number to the PII re-scan, which would make tests flaky.
    public static string NewEmployer() => "emp-" + new string(RandomNumberGenerator.GetBytes(10).Select(b => (char)('a' + b % 26)).ToArray());
    public static string NewSub() => "acct-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
    public static string NewInterviewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public static JsonObject Valid(string? employer = null, Action<JsonObject>? change = null)
    {
        var record = JsonNode.Parse(Template)!.AsObject();
        record["employerRef"] = employer ?? NewEmployer();
        record["interviewId"] = NewInterviewId();
        change?.Invoke(record);
        return record;
    }

    public static byte[] Bytes(JsonObject record) => Encoding.UTF8.GetBytes(record.ToJsonString());

    public static JsonObject Topic(this JsonObject record, string name) => record["topics"]![name]!.AsObject();

    public static void SetQuotes(this JsonObject record, string topic, params string[] quotes) =>
        record.Topic(topic)["quotes"] = new JsonArray([.. quotes.Select(q => (JsonNode)JsonValue.Create(q)!)]);

    public static ByteArrayContent Json(JsonObject record) => Json(Bytes(record));

    public static ByteArrayContent Json(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public static async Task<(HttpResponseMessage Response, string Body)> ReadAsync(this Task<HttpResponseMessage> call)
    {
        var response = await call;
        return (response, await response.Content.ReadAsStringAsync());
    }

    public static string? Code(string body) => JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;

    public static string ReceiptCodeOf(string body) => JsonSerializer.Deserialize<SubmissionAccepted>(body, JsonSerializerOptions.Web)!.ReceiptCode;
    public static TicketIssued TicketOf(string body) => JsonSerializer.Deserialize<TicketIssued>(body, JsonSerializerOptions.Web)!;
}
