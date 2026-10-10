using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Agent.Runner;

/// <summary>
/// Which language an interviewee writes in, and whether the interview should move to it (ADR-0075, Y4). Deterministic and offline:
/// Polish diacritics plus a short list of common words per language. A tie, or no evidence, means "no answer", and the current language stays.
/// Only the wording changes; topics, the state machine and the limits do not.
/// </summary>
public static partial class InterviewLanguage
{
    public const string Polish = "pl";
    public const string English = "en";

    /// <summary>A reply must have at least this many words before its language alone moves the interview (an explicit request always does).</summary>
    public const int MinWordsToSwitch = 5;

    private const string PolishDiacritics = "ąćęłńóśźż";

    // Words that do not occur in the other language. "a", "i", "to", "on" and "do" are left out: each is a word in both.
    private static readonly HashSet<string> PolishWords =
    [
        "się", "nie", "jest", "są", "być", "tak", "że", "ale", "bardzo", "dla", "który", "która", "które", "mnie", "mam", "mi", "bo", "jak", "już",
        "tylko", "wszystko", "było", "była", "przez", "ponieważ", "czy", "jeszcze", "też", "tego", "tym", "tej", "ten", "ta", "po", "na", "jestem",
        "nikt", "trwał", "trwało", "wiem", "proszę",
    ];

    private static readonly HashSet<string> EnglishWords =
    [
        "the", "and", "is", "was", "were", "are", "it", "that", "this", "with", "but", "not", "for", "have", "has", "they", "my", "of", "in", "at",
        "be", "very", "because", "about", "what", "when", "there", "really", "just", "would", "could", "nobody", "me", "you", "we", "their", "from",
        "or", "if", "so",
    ];

    /// <summary>The language of a text by counts (Polish diacritics and Polish words against English words); null when they tie or there is nothing to count.</summary>
    public static string? Detect(string text)
    {
        var lower = text.ToLowerInvariant();
        var words = Words().Matches(lower).Select(m => m.Value).ToList();
        var polish = lower.Count(c => PolishDiacritics.Contains(c)) + words.Count(PolishWords.Contains);
        var english = words.Count(EnglishWords.Contains);
        return polish > english ? Polish : english > polish ? English : null;
    }

    /// <summary>An explicit request for a language ("po polsku", "in English", ...), or null.</summary>
    public static string? Request(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("po polsku", StringComparison.Ordinal)) return Polish;
        if (lower.Contains("po angielsku", StringComparison.Ordinal) || lower.Contains("in english", StringComparison.Ordinal) || lower.Contains("speak english", StringComparison.Ordinal)) return English;
        return null;
    }

    /// <summary>The language one interviewee turn is written in: by its text first, then by an explicit request in it. Null when neither says.</summary>
    public static string? TurnLanguage(string text) => Detect(text) ?? Request(text);

    /// <summary>
    /// The language the interview moves to after this reply, or null to stay. A request always counts; otherwise the reply must have at least
    /// <see cref="MinWordsToSwitch"/> words and be decidably in the other language.
    /// </summary>
    public static string? SwitchTo(string reply, string current)
    {
        var target = Request(reply) ?? (Words().Matches(reply).Count >= MinWordsToSwitch ? Detect(reply) : null);
        return target is null || target == current ? null : target;
    }

    /// <summary>
    /// <c>interview.language</c>: the language most interviewee turns were written in. A tie takes the last decided turn; with no decided
    /// turn at all, <paramref name="fallback"/>.
    /// </summary>
    public static string RecordLanguage(IReadOnlyList<string> turnLanguages, string fallback)
    {
        if (turnLanguages.Count == 0) return fallback;
        var polish = turnLanguages.Count(l => l == Polish);
        var english = turnLanguages.Count - polish;
        return polish != english ? (polish > english ? Polish : English) : turnLanguages[^1];
    }

    /// <summary>The one sentence said before the next question after a switch, in the language switched to.</summary>
    public static string Confirmation(string language) => language == Polish ? "Dobrze, kontynuujmy po polsku." : "Sure, let's continue in English.";

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();
}
