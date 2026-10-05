using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Protocol;

namespace ExitInterviewAgent.Agent.Roles;

/// <summary>Outcome of checking a model-worded question: a controlled reason code, never the text.</summary>
public readonly record struct GuardVerdict(bool Ok, string Reason)
{
    public static GuardVerdict Pass { get; } = new(true, "ok");
}

/// <summary>
/// The code-side check on every question a model words. A rejected question is replaced by the protocol's own
/// wording, so a compromised or careless model can make the interview blander but not different: it cannot leak the
/// prompt, echo a name or address, lead the interviewee, or ask two things at once.
/// </summary>
public static partial class QuestionGuard
{
    public const int MaxChars = 500;

    public static GuardVerdict Check(string? text, TurnKind kind, IPiiGuard pii)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(false, "empty");
        var t = text.Trim();
        if (t.Length > MaxChars) return new(false, "too_long");
        if (t.Contains('\n') && t.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 2) return new(false, "multi_paragraph");
        if (PromptLeak().IsMatch(t)) return new(false, "prompt_leak");

        var asks = kind is TurnKind.Topic or TurnKind.Probe or TurnKind.Clarification or TurnKind.Redirect;
        if (asks)
        {
            var marks = t.Count(c => c == '?');
            if (marks == 0 && kind != TurnKind.Redirect) return new(false, "no_question");
            if (marks > 2) return new(false, "multiple_questions");
        }

        if (LeadingReason(t) is { } leading) return new(false, leading);
        if (kind == TurnKind.Probe && !ExampleRequest().IsMatch(t)) return new(false, "probe_without_example");
        if (pii.HasFindings(t)) return new(false, "pii");
        return GuardVerdict.Pass;
    }

    /// <summary>The leading-question lint on its own, for the protocol's fixed wording and for tests.</summary>
    public static string? LeadingReason(string question)
    {
        if (NegativePolar().IsMatch(question) || TagQuestion().IsMatch(question) || Assumptive().IsMatch(question)) return "leading";
        if (Loaded().IsMatch(question)) return "loaded";
        if (ClosedStarter().IsMatch(question)) return "closed_question";
        return null;
    }

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\b(don'?t|doesn'?t|didn'?t|isn'?t|wasn'?t|aren'?t|weren'?t|wouldn'?t|couldn'?t|shouldn'?t|haven'?t|hasn'?t)\s+(you|they|he|she|it|that|there|the|your)\b[^?]*\?|\b(wouldn'?t|don'?t)\s+you\s+(say|agree|think|feel|find)\b", Opt, 200)]
    private static partial Regex NegativePolar();

    [GeneratedRegex(@",\s*(isn'?t\s+it|wasn'?t\s+it|didn'?t\s+you|don'?t\s+you|weren'?t\s+you|wouldn'?t\s+you|aren'?t\s+you|right|correct)\s*\?|\b(right|correct|true)\s*\?\s*$", Opt, 200)]
    private static partial Regex TagQuestion();

    [GeneratedRegex(@"\b(surely|obviously|clearly|of\s+course|naturally|you\s+must\s+(have|be)|i\s+(assume|imagine|guess|suppose)\s+(you|that|it))\b", Opt, 200)]
    private static partial Regex Assumptive();

    [GeneratedRegex(@"\bhow\s+(bad|terrible|awful|unfair|toxic|great|wonderful|horrible)\b|\bwhy\s+(was|were|is|are)\b[^?]{0,60}\b(bad|terrible|awful|unfair|toxic|horrible)\b|\b(frustrat\w+|disappoint\w+|angry|upset)\b[^?]*\bwith\b[^?]*\?", Opt, 200)]
    private static partial Regex Loaded();

    [GeneratedRegex(@"^\W*(did|do|does|was|were|is|are|have|has|had)\s+(you|they|your|the|it|there|that|he|she)\b", Opt, 200)]
    private static partial Regex ClosedStarter();

    [GeneratedRegex(@"\b(example|specific|instance|particular\s+(situation|moment|time))\b", Opt, 200)]
    private static partial Regex ExampleRequest();

    [GeneratedRegex(@"(^|\n)\s*(role|system|assistant|user)\s*:|<<<|>>>|system\s+prompt|\bdata\s+block\b|interview_data", Opt, 200)]
    private static partial Regex PromptLeak();
}
