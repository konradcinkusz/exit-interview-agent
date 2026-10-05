using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Protocol;

namespace ExitInterviewAgent.Agent.Machine;

public enum ConsentAnswer { Yes, No, Unclear }

/// <summary>
/// What the deterministic analyser read from one (already masked) reply. Booleans and counts only, so it can be
/// traced. <see cref="Polarity"/> is a protocol signal (does this answer lean one way about the employer?) used only to
/// notice contradictions; it is never stored, never put in the record, and is not an assessment of the person.
/// </summary>
public sealed record ReplySignals(
    int Words,
    bool Withdrawal,
    ConsentAnswer Consent,
    bool Terse,
    bool Vague,
    bool Hostile,
    bool Contradiction,
    bool NamesPerson,
    bool InjectionSuspected,
    int Polarity);

/// <summary>
/// Rule-based reading of a reply. Decisions that protect the interviewee (withdrawal, consent) and the shape of the
/// interview (probe, clarify, close) come from here and the state machine, never from model output. The cue lists are
/// small and English (with a few Polish withdrawal phrases); their false negatives and positives are documented in
/// docs/architecture/interview-agent.md. A reply is analysed after PII masking, so a name cannot hide a cue.
/// </summary>
public static partial class ReplyAnalyzer
{
    public static ReplySignals Analyze(string maskedReply, ProtocolLimits limits, bool namesPerson, int previousPolarity = 0)
    {
        ArgumentNullException.ThrowIfNull(maskedReply);
        var words = CountWords(maskedReply);
        var terse = words <= limits.TerseWordLimit;
        var polarity = Polarity(maskedReply);
        return new ReplySignals(
            words,
            IsWithdrawal(maskedReply, words),
            ConsentOf(maskedReply),
            terse,
            !terse && words <= 25 && VagueCue().IsMatch(maskedReply) && !ConcreteCue().IsMatch(maskedReply),
            HostileCue().IsMatch(maskedReply),
            previousPolarity != 0 && polarity != 0 && Math.Sign(previousPolarity) != Math.Sign(polarity),
            namesPerson,
            InjectionCue().IsMatch(maskedReply),
            polarity);
    }

    public static int CountWords(string text)
    {
        var n = 0;
        var inWord = false;
        foreach (var c in text)
        {
            var letter = char.IsLetterOrDigit(c);
            if (letter && !inWord) n++;
            inWord = letter || (inWord && (c is '\'' or '-' or '’'));
        }
        return n;
    }

    public static bool IsWithdrawal(string text, int? words = null) =>
        WithdrawalCue().IsMatch(text) || (words ?? CountWords(text)) <= 2 && BareStop().IsMatch(text);

    public static ConsentAnswer ConsentOf(string text)
    {
        if (WithdrawalCue().IsMatch(text)) return ConsentAnswer.No;
        if (NoAnswer().IsMatch(text)) return ConsentAnswer.No;
        return YesAnswer().IsMatch(text) ? ConsentAnswer.Yes : ConsentAnswer.Unclear;
    }

    private static int Polarity(string text)
    {
        var (pos, neg) = CueCounts(text);
        return Math.Sign(pos - neg);
    }

    /// <summary>Counts of positive and negative practice cues, for the contradiction check and the scripted mock extractor.</summary>
    internal static (int Positive, int Negative) CueCounts(string text) => (PositiveCue().Count(text), NegativeCue().Count(text));

    internal static bool HasConcreteCue(string text) => ConcreteCue().IsMatch(text);

    internal static bool HasHostileCue(string text) => HostileCue().IsMatch(text);

    /// <summary>True when a text reads like an instruction aimed at the interviewer, the extractor or a judge. Observability and quote filtering only: it never changes the protocol.</summary>
    public static bool LooksLikeInjection(string text) => InjectionCue().IsMatch(text);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\b(i\s+(want|would\s+like|wish|need)\s+to\s+(stop|end|quit|finish)|let'?s\s+stop|stop\s+(the|this)\s+(interview|conversation)|(end|cancel)\s+(the|this)\s+(interview|conversation)|(i\s+)?(withdraw|revoke|take\s+back)\s+(my\s+)?consent|i\s+(do\s+not|don'?t|no\s+longer)\s+consent|i\s+changed\s+my\s+mind|please\s+stop|i'?m\s+stopping|delete\s+(this|everything|my\s+answers)|wycofuj\w*|przerwij|chc\w+\s+przerwa\w+|nie\s+zgadzam\s+si\w+)\b", Opt, 200)]
    private static partial Regex WithdrawalCue();

    [GeneratedRegex(@"^\W*(stop|quit|exit|cancel|enough)\W*(please)?\W*$", Opt, 200)]
    private static partial Regex BareStop();

    [GeneratedRegex(@"^\W*((no|nope|nah|nie)\b(?!\s+(problem|worries))|i\s+(do\s+not|don'?t)\s+(agree|want)|i\s+(decline|refuse)|i'?d\s+rather\s+not|not\s+(now|really|comfortable))", Opt, 200)]
    private static partial Regex NoAnswer();

    [GeneratedRegex(@"^\W*(yes|yep|yeah|yup|sure|ok(ay)?|alright|agreed|tak|zgadzam\s+si\w+|go\s+ahead|that'?s\s+fine|fine|no\s+(problem|worries)|i\s+(do\s+)?(agree|consent)|i\s+agree)\b", Opt, 200)]
    private static partial Regex YesAnswer();

    [GeneratedRegex(@"\b(fine|ok|okay|good|bad|great|nice|terrible|awful|meh|so-so|whatever|stuff|things|generally|kind\s+of|sort\s+of|you\s+know|the\s+usual|average|normal|alright|not\s+great|not\s+bad)\b", Opt, 200)]
    private static partial Regex VagueCue();

    [GeneratedRegex(@"(\d|\b(january|february|march|april|may|june|july|august|september|october|november|december|monday|tuesday|wednesday|thursday|friday)\b|\b(for\s+example|for\s+instance|such\s+as|e\.g\.|specifically|one\s+time|once|last\s+(week|month|year|quarter)|the\s+day|that\s+day|when\s+(i|we|they|my)|because|after|before)\b)", Opt, 200)]
    private static partial Regex ConcreteCue();

    [GeneratedRegex(@"\b(waste\s+of\s+(my\s+)?time|stupid|idiot\w*|pointless|shut\s+up|none\s+of\s+your\s+business|leave\s+me\s+alone|stop\s+asking|ridiculous|garbage|useless|sick\s+of|damn|crap)\b", Opt, 200)]
    private static partial Regex HostileCue();

    [GeneratedRegex(@"\b(ignore|disregard|forget)\s+(all\s+|any\s+|your\s+|the\s+)?(previous|prior|above|earlier)?\s*(instructions|rules|prompt)|system\s+prompt|you\s+are\s+now\b|new\s+instructions|as\s+an?\s+(ai\s+)?(evaluator|judge|grader)|note\s+to\s+(the\s+)?(evaluator|judge|grader|extractor)|set\s+(all\s+)?ratings?|</?\s*(system|data|interview_data)|\bdebug\s+mode\b", Opt, 200)]
    private static partial Regex InjectionCue();

    [GeneratedRegex(@"\b(supportive|helpful|great|good|excellent|fair|generous|clear|welcoming|friendly|well\s+organi[sz]ed|respectful|enjoyed|loved|happy\s+with|well\s+paid|opportunit\w+|trusted|inclusive)\b", Opt, 200)]
    private static partial Regex PositiveCue();

    [GeneratedRegex(@"\b(unsupportive|unhelpful|bad|terrible|awful|poor|unfair|stingy|unclear|chaotic|disorgani[sz]ed|disrespectful|toxic|hated|unhappy|underpaid|no\s+support|no\s+help|never|nothing|broken\s+promises?|ignored|micromanag\w+|overworked|burn\w*)\b", Opt, 200)]
    private static partial Regex NegativeCue();
}
