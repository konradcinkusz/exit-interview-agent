using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Privacy;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Roles;

public enum Role { Interviewer, Prober, Extractor }

/// <summary>
/// Implemented by an exception a model client throws when it can describe the failure without content: a controlled
/// <see cref="FailureCode"/> (letters, digits, <c>_ . : -</c>) and whether continuing is pointless (<see cref="IsFatal"/>).
/// The metered client carries the code, never the message.
/// </summary>
public interface IModelFailure
{
    string FailureCode { get; }

    bool IsFatal { get; }
}

/// <summary>A request to word one interviewer turn. <see cref="Seed"/> is the protocol's own wording, also the fallback.</summary>
public sealed record QuestionRequest(TurnKind Kind, Topic? Topic, string Seed, Transcript History);

/// <summary>Words the topic questions, redirects and clarifications. Output is checked by <see cref="QuestionGuard"/>.</summary>
public interface IInterviewer
{
    Task<string> AskAsync(QuestionRequest request, CancellationToken ct);
}

/// <summary>Words the single concrete-example probe. Whether to probe is decided by the state machine, not by this role.</summary>
public interface IProber
{
    Task<string> ProbeAsync(QuestionRequest request, CancellationToken ct);
}

/// <summary>
/// Turns the MASKED transcript into extractor-schema JSON. The return value is untrusted model output: the caller
/// parses it against <c>schemas/extractor-output.v1.schema.json</c> and builds the record with its own code.
/// <paramref name="previousErrorCodes"/> lists error codes (never content) from a failed first attempt.
/// </summary>
public interface IRecordExtractor
{
    Task<string> ExtractAsync(Transcript maskedTranscript, IReadOnlyList<string> previousErrorCodes, CancellationToken ct);
}

/// <summary>Result of masking one text. When <see cref="Ok"/> is false the detector failed and nothing may be submitted.</summary>
public sealed record PiiGuardResult(bool Ok, string MaskedText, IReadOnlyList<PiiFinding> Findings)
{
    public bool NamesPerson => Findings.Any(f => f.Kind == PiiKind.PersonName);
}

public interface IPiiGuard
{
    PiiGuardResult Mask(string text);

    /// <summary>True when the detector finds anything in an already-masked text; also true when the detector fails (fail closed).</summary>
    bool HasFindings(string maskedText);
}

/// <summary>
/// Wraps <see cref="PiiDetector"/> with <c>FailClosed = true</c> and an allow-list of employer and product names. Any
/// exception or timeout from the detector is reported as a failure, never as "no findings".
/// </summary>
public sealed class PiiGuard : IPiiGuard
{
    private readonly PiiDetector _detector;

    public PiiGuard(IEnumerable<string>? allowList = null) =>
        _detector = new PiiDetector(new PiiOptions { AllowList = (allowList ?? []).ToArray(), FailClosed = true });

    public PiiGuardResult Mask(string text)
    {
        try
        {
            var result = _detector.Mask(text);
            return new PiiGuardResult(true, result.MaskedText, result.Findings);
        }
        catch (Exception)
        {
            return new PiiGuardResult(false, string.Empty, []);
        }
    }

    public bool HasFindings(string maskedText)
    {
        try { return _detector.Detect(maskedText).Count > 0; }
        catch (Exception) { return true; }
    }
}
