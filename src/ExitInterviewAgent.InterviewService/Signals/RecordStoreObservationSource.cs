using System.Runtime.CompilerServices;
using System.Text.Json;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.Signals;
using Microsoft.EntityFrameworkCore;

namespace ExitInterviewAgent.InterviewService.Signals;

/// <summary>
/// The anti-corruption layer between the record store and the Signals module (ADR-0052, P11): the one place that reads
/// <c>Records.Json</c> for the module, and the place that drops everything the module must never see. It maps the stored canonical
/// record, by its schema's wire names, to the module's own <see cref="Observation"/>; quotes, the interview id, confidence, the
/// metadata and the week bucket are read past and never leave this method. It is written against the published schema
/// (<c>schemas/exit-interview-record.v1.schema.json</c>), not against the record library's types, so the module's vocabulary does not move
/// when a C# type is renamed. Extraction of the module replaces this class by a transport; nothing else changes.
/// </summary>
public sealed class RecordStoreObservationSource(InterviewDbContext db) : IObservationSource
{
    public async IAsyncEnumerable<Observation> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // Ordered by employer so the module can fold one employer at a time; read-only, no tracking, streamed.
        var rows = db.Records.AsNoTracking().OrderBy(r => r.EmployerRef)
            .Select(r => new { r.EmployerRef, r.Verification, r.Json })
            .AsAsyncEnumerable();
        await foreach (var row in rows.WithCancellation(ct))
        {
            if (Map(row.EmployerRef, row.Verification, row.Json) is { } observation)
            {
                yield return observation;
            }
        }
    }

    /// <summary>Maps one stored record; null for anything that is not a schema v1 record the module understands (it is skipped, never repaired).</summary>
    public static Observation? Map(string employerRef, VerificationLevel verification, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.GetString() != "1"
                || !root.TryGetProperty("context", out var context) || !root.TryGetProperty("topics", out var topics)
                || context.GetProperty("tenureBand").GetString() is not { } tenure)
            {
                return null;
            }

            var ratings = new List<TopicRating>(6);
            foreach (var topic in topics.EnumerateObject())
            {
                if (topic.Value.GetProperty("status").GetString() != "covered")
                {
                    continue;
                }
                var rating = topic.Value.GetProperty("rating");
                ratings.Add(new TopicRating(topic.Name, rating.ValueKind == JsonValueKind.Number ? rating.GetInt32() : null));
            }

            return new Observation(
                employerRef,
                tenure,
                context.TryGetProperty("seniorityBand", out var seniority) ? seniority.GetString() : null,
                context.TryGetProperty("functionBand", out var function) ? function.GetString() : null,
                verification switch
                {
                    VerificationLevel.Verified => VerificationState.Verified,
                    VerificationLevel.Unverified => VerificationState.Unverified,
                    _ => VerificationState.Unchecked,
                },
                ratings);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
