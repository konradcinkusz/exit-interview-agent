using ExitInterviewAgent.Privacy;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// What the CLI re-checks on this computer before anything is sent, with the same libraries the server uses: schema validation
/// (<see cref="RecordValidator"/>), AI disclosure, at least one covered topic, and a personal-data scan of every quote and free-text field.
/// It fails closed: an exception while checking is a failed check, never a pass. It reports codes, schema paths and kinds, never the text found.
/// </summary>
internal static class LocalRecordCheck
{
    public sealed record Result(bool Ok, IReadOnlyList<string> Problems);

    public static Result Run(ReadOnlySpan<byte> json)
    {
        var problems = new List<string>();
        try
        {
            if (json.Length > RecordLimits.Default.MaxPayloadBytes) return Fail("The file is larger than a record can be (" + RecordLimits.Default.MaxPayloadBytes / 1024 + " KiB).");
            var outcome = new RecordValidator().Validate(json);
            if (!outcome.IsValid)
            {
                foreach (var e in outcome.Errors.Take(20)) problems.Add($"{SubmitMessages.Describe(e.Code)} ({e.Code}{(e.Path.Length > 0 ? " at " + e.Path : string.Empty)})");
                return new Result(false, problems);
            }

            var record = outcome.Record!;
            if (!record.Interview.AiDisclosed) problems.Add("the record says the interviewee was not told the interviewer is an AI (AI_NOT_DISCLOSED at /interview/aiDisclosed)");
            if (!record.Topics.Enumerate().Any(t => t.Entry.Status == TopicStatus.Covered)) problems.Add("the record carries no covered topic, so there is nothing to submit");

            var detector = new PiiDetector();
            foreach (var (path, text) in Fields(record))
                foreach (var kind in detector.Detect(text).Select(f => f.Kind).Distinct())
                    problems.Add($"looks like {SubmitMessages.KindWords(kind.ToString())} at {path}: replace the words with a placeholder such as [PERSON] in the file");
        }
        catch (Exception)
        {
            return Fail("the local check could not be completed, so nothing may be sent (it fails closed)");
        }
        return new Result(problems.Count == 0, problems);

        static Result Fail(string why) => new(false, [why]);
    }

    private static IEnumerable<(string Path, string Text)> Fields(InterviewRecord record)
    {
        foreach (var (topic, entry) in record.Topics.Enumerate())
            for (var i = 0; i < entry.Quotes.Count; i++)
                yield return ($"/topics/{Wire.Name(topic)}/quotes/{i}", entry.Quotes[i]);
        yield return ("/employerRef", record.EmployerRef);
        yield return ("/interview/protocolVersion", record.Interview.ProtocolVersion);
        yield return ("/interview/language", record.Interview.Language);
    }
}
