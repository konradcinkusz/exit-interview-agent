using ExitInterviewAgent.Privacy;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>Result of the server-side re-scan: kinds only (never offsets into, or the text of, what was found).</summary>
public sealed record PiiScanResult(IReadOnlyList<PiiKind> Kinds, bool Failed)
{
    public static PiiScanResult Clean { get; } = new([], false);
}

/// <summary>The server does not trust a client's claim that the record is masked: it looks again (brief section 6).</summary>
public interface IPiiScanner
{
    Task<PiiScanResult> ScanAsync(InterviewRecord record, CancellationToken ct);
}

/// <summary>
/// Scans every quote and every string field that can carry free text. Fields the schema pins to a closed alphabet
/// (the interview id is 32 hex characters) are not scanned: they cannot hold personal data, and a random hex id
/// would sometimes look like a numeric identifier to the detector. Any exception or timeout is a failure and the
/// caller rejects the submission (fail closed). Nothing is logged here.
/// </summary>
public sealed class PiiScanner(IOptions<SubmissionOptions> options) : IPiiScanner
{
    private readonly PiiSettings _settings = options.Value.Pii;
    private readonly PiiDetector _detector = new(new PiiOptions
    {
        FailClosed = options.Value.Pii.FailClosed,
        AllowList = options.Value.Pii.AllowList,
    });

    public async Task<PiiScanResult> ScanAsync(InterviewRecord record, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => Scan(record), CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(_settings.TimeoutMilliseconds), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Includes TimeoutException and RegexMatchTimeoutException. The exception text is dropped on purpose.
            return new PiiScanResult([], Failed: true);
        }
    }

    private PiiScanResult Scan(InterviewRecord record)
    {
        var kinds = new HashSet<PiiKind>();
        foreach (var text in Strings(record))
        {
            foreach (var finding in _detector.Detect(text))
            {
                kinds.Add(finding.Kind);
            }
        }
        return kinds.Count == 0 ? PiiScanResult.Clean : new PiiScanResult([.. kinds.Order()], false);
    }

    private static IEnumerable<string> Strings(InterviewRecord record)
    {
        foreach (var (_, entry) in record.Topics.Enumerate())
        {
            foreach (var quote in entry.Quotes)
            {
                yield return quote;
            }
        }
        yield return record.EmployerRef;
        yield return record.Interview.ProtocolVersion;
        yield return record.Interview.Language;
    }
}
