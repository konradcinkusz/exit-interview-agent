using System.Text.Json;

namespace ExitInterviewAgent.Records;

/// <summary>Maps a JSON element that already passed schema validation to the model. Not for unvalidated input.</summary>
internal static class RecordReader
{
    public static InterviewRecord Read(JsonElement root)
    {
        var context = root.GetProperty("context");
        var topics = root.GetProperty("topics");
        var interview = root.GetProperty("interview");
        return new InterviewRecord(
            InterviewId.Parse(root.GetProperty("interviewId").GetString()!),
            root.GetProperty("employerRef").GetString()!,
            new RecordContext(
                Enum<TenureBand>(context.GetProperty("tenureBand")),
                context.TryGetProperty("seniorityBand", out var s) ? Enum<SeniorityBand>(s) : null,
                context.TryGetProperty("functionBand", out var f) ? Enum<FunctionBand>(f) : null),
            new TopicSet(
                Entry(topics, "onboarding"), Entry(topics, "management"), Entry(topics, "growth"),
                Entry(topics, "pay_vs_promises"), Entry(topics, "culture"), Entry(topics, "reason_for_leaving")),
            root.GetProperty("piiMasked").GetBoolean(),
            new InterviewMetadata(
                interview.GetProperty("protocolVersion").GetString()!,
                interview.GetProperty("language").GetString()!,
                interview.GetProperty("aiDisclosed").GetBoolean(),
                Enum<DurationBand>(interview.GetProperty("durationBand")),
                Enum<TurnBand>(interview.GetProperty("turnBand"))));
    }

    private static TopicEntry Entry(JsonElement topics, string name)
    {
        var t = topics.GetProperty(name);
        if (Enum<TopicStatus>(t.GetProperty("status")) == TopicStatus.NoData) return TopicEntry.NoData;
        var rating = t.GetProperty("rating");
        return TopicEntry.Covered(
            rating.ValueKind == JsonValueKind.Null ? null : rating.GetInt32(),
            Enum<Confidence>(t.GetProperty("confidence")),
            t.GetProperty("quotes").EnumerateArray().Select(q => q.GetString()!));
    }

    private static T Enum<T>(JsonElement e) where T : struct, Enum =>
        Wire.TryParse<T>(e.GetString(), out var v) ? v : throw new InvalidOperationException("Value passed the schema but is not a known wire name.");
}
