using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Roles;

/// <summary>What the quote step did, as counts: nothing here carries text.</summary>
public sealed record AssemblyReport(int QuotesChecked, int QuotesDropped, int TopicsForcedToNoData, int TopicsCovered);

/// <summary>
/// The quote step and the record builder. The extractor's claims are checked by code before they become a record:
/// a topic needs real evidence in the interviewee's own words, every quote must be a verbatim substring of the
/// interviewee's words on THAT topic (so the extractor cannot quote the interviewer, another topic, or invent text),
/// and a quote the PII guard flags is dropped. Unverifiable quotes are dropped and counted, never submitted.
/// </summary>
public static partial class RecordAssembler
{
    public static (InterviewRecord Record, AssemblyReport Report) Assemble(
        ExtractorOutput extracted,
        Transcript transcript,
        InterviewProtocol protocol,
        IPiiGuard pii,
        string employerRef,
        RecordContext context,
        InterviewMetadata metadata,
        InterviewId id,
        IReadOnlySet<Topic> contradicted)
    {
        var checkedCount = 0;
        var dropped = 0;
        var forced = 0;

        // 1. Candidates: normalized, de-duplicated, within the schema's length, from topics with real evidence.
        var candidates = new Dictionary<Topic, (ExtractedTopic Source, List<string> Quotes)>();
        foreach (var topic in Enum.GetValues<Topic>())
        {
            var source = extracted.Topics[topic];
            if (!source.Covered) { candidates[topic] = (source, []); continue; }
            var evidence = ReplyAnalyzer.CountWords(transcript.IntervieweeText(topic));
            if (evidence < protocol.Limits.MinWordsForCoverage)
            {
                forced++;
                dropped += source.Quotes.Count;
                checkedCount += source.Quotes.Count;
                candidates[topic] = (source, []);
                continue;
            }
            var quotes = new List<string>();
            foreach (var raw in source.Quotes)
            {
                checkedCount++;
                var q = QuoteVerifier.Normalize(raw);
                if (q.Length == 0 || q.EnumerateRunes().Count() > TopicEntry.MaxQuoteLength || quotes.Contains(q, StringComparer.Ordinal)) { dropped++; continue; }
                quotes.Add(q);
            }
            candidates[topic] = (source, quotes);
        }

        // 2. Fidelity, per topic, against that topic's interviewee text, using the same verifier as everyone else.
        var probe = Build(candidates.ToDictionary(c => c.Key, c => c.Value.Quotes), extracted, contradicted, employerRef, context, metadata, id);
        var verified = new Dictionary<Topic, List<string>>();
        foreach (var topic in Enum.GetValues<Topic>())
        {
            var mismatches = QuoteVerifier.VerifyQuotes(transcript.IntervieweeText(topic), probe).Mismatches.Where(m => m.Topic == topic).Select(m => m.QuoteIndex).ToHashSet();
            var kept = new List<string>();
            for (var i = 0; i < candidates[topic].Quotes.Count; i++)
            {
                var q = candidates[topic].Quotes[i];
                if (mismatches.Contains(i) || HasNoSubstance(q) || ReplyAnalyzer.LooksLikeInjection(q) || pii.HasFindings(q)) { dropped++; continue; }
                kept.Add(q);
            }
            verified[topic] = kept;
        }

        var record = Build(verified, extracted, contradicted, employerRef, context, metadata, id);
        var covered = record.Topics.Enumerate().Count(t => t.Entry.Status == TopicStatus.Covered);
        return (record, new AssemblyReport(checkedCount, dropped, forced, covered));
    }

    private static InterviewRecord Build(
        IReadOnlyDictionary<Topic, List<string>> quotes, ExtractorOutput extracted, IReadOnlySet<Topic> contradicted,
        string employerRef, RecordContext context, InterviewMetadata metadata, InterviewId id)
    {
        TopicEntry Entry(Topic t)
        {
            var x = extracted.Topics[t];
            if (!x.Covered || quotes[t].Count == 0) return TopicEntry.NoData;
            var confidence = x.Confidence ?? Confidence.Low;
            if (contradicted.Contains(t) && confidence > Confidence.Medium) confidence = Confidence.Medium;
            return TopicEntry.Covered(x.Rating, confidence, quotes[t]);
        }

        var topics = new TopicSet(Entry(Topic.Onboarding), Entry(Topic.Management), Entry(Topic.Growth), Entry(Topic.PayVsPromises), Entry(Topic.Culture), Entry(Topic.ReasonForLeaving));
        return new InterviewRecord(id, employerRef, context, topics, PiiMasked: true, metadata);
    }

    /// <summary>A quote that is only placeholders, or too short to say anything, supports nothing.</summary>
    private static bool HasNoSubstance(string quote) => ReplyAnalyzer.CountWords(Placeholder().Replace(quote, " ")) < 3;

    [GeneratedRegex(@"\[[A-Z_]+\]", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Placeholder();
}
