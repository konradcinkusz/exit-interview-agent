using System.Text;

namespace ExitInterviewAgent.Privacy;

/// <summary>
/// Deterministic PII detector and masker: regular expressions, checksums and language heuristics. No model, no
/// network, no I/O. The same text and options always give the same findings. Findings carry kinds and offsets only.
/// A <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/> (2 s per rule, see RegexBudget) is possible on adversarial
/// input; callers must treat any exception as "do not submit".
/// </summary>
public sealed class PiiDetector
{
    private readonly PiiOptions _options;
    private readonly NameRules _names;

    public PiiDetector(PiiOptions? options = null)
    {
        _options = options ?? new PiiOptions();
        _names = new NameRules(_options);
    }

    public static string Placeholder(PiiKind kind) => kind switch
    {
        PiiKind.Email => "[EMAIL]",
        PiiKind.Url => "[URL]",
        PiiKind.IpAddress => "[IP_ADDRESS]",
        PiiKind.NationalId => "[NATIONAL_ID]",
        PiiKind.EmployeeId => "[EMPLOYEE_ID]",
        PiiKind.Phone => "[PHONE]",
        PiiKind.Handle => "[HANDLE]",
        PiiKind.PersonName => "[PERSON]",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Non-overlapping findings in text order. Overlapping candidates are merged into their union (so no part of
    /// either is left unmasked) and keep the kind of the most specific rule: email, URL, IP, national id, employee id,
    /// phone, handle, person name.
    /// </summary>
    public IReadOnlyList<PiiFinding> Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return [];

        var candidates = new List<PiiFinding>();
        PatternRules.Detect(text, _options.FailClosed, candidates);
        _names.Detect(text, candidates);
        return Merge(candidates);
    }

    public MaskResult Mask(string text)
    {
        var findings = Detect(text);
        if (findings.Count == 0) return new MaskResult(text, findings);

        var sb = new StringBuilder(text.Length);
        var pos = 0;
        foreach (var f in findings)
        {
            sb.Append(text, pos, f.Start - pos).Append(Placeholder(f.Kind));
            pos = f.End;
        }

        sb.Append(text, pos, text.Length - pos);
        return new MaskResult(sb.ToString(), findings);
    }

    private static List<PiiFinding> Merge(List<PiiFinding> candidates)
    {
        var sorted = candidates.Where(c => c.Length > 0).OrderBy(c => c.Start).ThenByDescending(c => c.Length).ToList();
        var result = new List<PiiFinding>();
        foreach (var c in sorted)
        {
            if (result.Count > 0 && c.Start < result[^1].End)
            {
                var last = result[^1];
                var kind = (PiiKind)Math.Min((int)last.Kind, (int)c.Kind);
                var basis = last.Basis == PiiBasis.FailClosed && c.Basis == PiiBasis.FailClosed ? PiiBasis.FailClosed
                    : last.Basis == PiiBasis.Pattern || c.Basis == PiiBasis.Pattern ? PiiBasis.Pattern : PiiBasis.Heuristic;
                result[^1] = new PiiFinding(kind, last.Start, Math.Max(last.End, c.End) - last.Start, basis);
            }
            else
            {
                result.Add(c);
            }
        }

        return result;
    }
}
