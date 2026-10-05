using System.Text;
using System.Text.Json;

namespace ExitInterviewAgent.Records;

/// <summary>
/// Canonical serialization: the same record always yields the same bytes. UTF-8, no insignificant whitespace,
/// properties in the schema's declared order, optional bands omitted when absent, the System.Text.Json default
/// string escaping (non-ASCII is written as \uXXXX, which keeps the form independent of the consumer's encoder).
/// </summary>
public static class RecordSerializer
{
    public static byte[] SerializeCanonical(InterviewRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("schemaVersion", InterviewRecord.SchemaVersion);
            w.WriteString("interviewId", record.InterviewId.Value);
            w.WriteString("employerRef", record.EmployerRef);
            w.WriteStartObject("context");
            w.WriteString("tenureBand", Wire.Name(record.Context.TenureBand));
            if (record.Context.SeniorityBand is { } s) w.WriteString("seniorityBand", Wire.Name(s));
            if (record.Context.FunctionBand is { } f) w.WriteString("functionBand", Wire.Name(f));
            w.WriteEndObject();
            w.WriteStartObject("topics");
            foreach (var (topic, entry) in record.Topics.Enumerate())
            {
                w.WriteStartObject(Wire.Name(topic));
                w.WriteString("status", Wire.Name(entry.Status));
                if (entry.Rating is { } r) w.WriteNumber("rating", r); else w.WriteNull("rating");
                if (entry.Confidence is { } c) w.WriteString("confidence", Wire.Name(c)); else w.WriteNull("confidence");
                w.WriteStartArray("quotes");
                foreach (var q in entry.Quotes) w.WriteStringValue(q);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteBoolean("piiMasked", record.PiiMasked);
            w.WriteStartObject("interview");
            w.WriteString("protocolVersion", record.Interview.ProtocolVersion);
            w.WriteString("language", record.Interview.Language);
            w.WriteString("durationBand", Wire.Name(record.Interview.DurationBand));
            w.WriteString("turnBand", Wire.Name(record.Interview.TurnBand));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static string SerializeCanonicalString(InterviewRecord record) => Encoding.UTF8.GetString(SerializeCanonical(record));
}
