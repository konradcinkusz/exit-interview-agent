using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Protocol;

namespace ExitInterviewAgent.Agent.Roles;

/// <summary>Outcome of checking a model-worded question: a controlled reason code, never the text.</summary>
public readonly record struct GuardVerdict(bool Ok, string Reason)
{
    public static GuardVerdict Pass { get; } = new(true, "ok");
}

/// <summary>
/// What a turn is for, when its move is known (interview-dynamics spec §3.8). The machine maps a move to a shape in WP5; the default is the
/// rule for the turn kind alone, so every existing caller keeps its verdicts.
/// </summary>
public enum QuestionShape
{
    /// <summary>The rule for the turn kind: a probe must ask for a concrete example.</summary>
    Default,
    /// <summary>A 1-to-5 rating of a light topic: a closed question is allowed, and it must name both anchors, 1 and 5.</summary>
    Scale,
    /// <summary>A deepening follow-up that is not an example request (elaborate, ladder, consequence, scale follow-up).</summary>
    Follow,
}

/// <summary>
/// The code-side check on every question a model words. A rejected question is replaced by the protocol's own
/// wording, so a compromised or careless model can make the interview blander but not different: it cannot leak the
/// prompt, echo a name or address, lead the interviewee, or ask two things at once. The English and Polish rules are
/// separate lists with the same effect (ADR-0062, ADR-0075); a Polish question is checked by both.
/// </summary>
public static partial class QuestionGuard
{
    public const int MaxChars = 500;

    /// <summary>The most words one model turn may use (interview-dynamics spec §3.8). A turn of 45 words or fewer is the target.</summary>
    public const int MaxWords = 60;

    /// <summary>
    /// Checks one model-worded turn. Protocol 1.3 (spec §3.8) judges the question sentence and the acknowledgement apart: the question sentence
    /// gets the full lint, the acknowledgement only the loaded and assumptive lints, and the interviewee's own quoted words (in „…” or "…")
    /// are exempt from those two but must be verbatim from their last turn, when <paramref name="lastInterviewee"/> is given.
    /// <paramref name="shape"/> adds the move's requirement; <see cref="QuestionShape.Default"/> is the rule for the turn kind alone.
    /// </summary>
    public static GuardVerdict Check(string? text, TurnKind kind, IPiiGuard pii, QuestionShape shape = QuestionShape.Default, string? lastInterviewee = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(false, "empty");
        var t = text.Trim();
        if (t.Length > MaxChars) return new(false, "too_long");
        if (Token().Matches(t).Count > MaxWords) return new(false, "max_words");
        if (t.Contains('\n') && t.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 2) return new(false, "multi_paragraph");
        if (PromptLeak().IsMatch(t)) return new(false, "prompt_leak");

        var asks = kind is TurnKind.Topic or TurnKind.Probe or TurnKind.Clarification or TurnKind.Redirect or TurnKind.DeepProbe;
        if (asks)
        {
            var marks = t.Count(c => c == '?');
            if (marks == 0 && kind != TurnKind.Redirect && !(kind == TurnKind.Topic && Invitation().IsMatch(t))) return new(false, "no_question");
            if (marks > 2) return new(false, "multiple_questions");
            if (marks == 2 || SecondInterrogative().IsMatch(t)) return new(false, "double_barrelled");
        }

        var (acknowledgement, question) = Split(t);
        if (LeadingReason(question, shape) is { } leading) return new(false, leading);
        if (AcknowledgementReason(acknowledgement) is { } judged) return new(false, judged);
        if (Misquoted(t, lastInterviewee)) return new(false, "misquote");
        if (kind == TurnKind.Probe && shape == QuestionShape.Default && !ExampleRequest().IsMatch(question)) return new(false, "probe_without_example");
        if (shape == QuestionShape.Scale && !(Anchor1().IsMatch(question) && Anchor5().IsMatch(question))) return new(false, "scale_without_anchors");
        if (pii.HasFindings(t)) return new(false, "pii");
        return GuardVerdict.Pass;
    }

    /// <summary>The leading-question lint on its own, for the protocol's fixed wording and for tests.</summary>
    public static string? LeadingReason(string question) => LeadingReason(question, QuestionShape.Default);

    /// <summary>
    /// The question sentence is the last sentence with a question mark (anything after it stays with it); the acknowledgement is what
    /// comes before. A text without a question mark is all question: an invitation or a request.
    /// </summary>
    internal static (string Acknowledgement, string Question) Split(string t)
    {
        var mark = t.LastIndexOf('?');
        if (mark < 0) return ("", t);
        var start = 0;
        for (var i = mark - 1; i >= 0; i--)
        {
            if (t[i] is ('.' or '!' or '?') && i + 1 < t.Length && char.IsWhiteSpace(t[i + 1]))
            {
                start = i + 1;
                break;
            }
        }
        return (t[..start].Trim(), t[start..].Trim());
    }

    private static string? LeadingReason(string question, QuestionShape shape)
    {
        if (NegativePolar().IsMatch(question) || TagQuestion().IsMatch(question) || Assumptive().IsMatch(question)) return "leading";
        if (Loaded().IsMatch(question)) return "loaded";
        if (shape != QuestionShape.Scale && (ClosedStarter().IsMatch(question) || PolishClosedStarter().IsMatch(question))) return "closed_question";
        return null;
    }

    /// <summary>The acknowledgement is not a question, but it can still lead or load ("Przecież …", "Jak okropnie …"). Quoted words are the person's own.</summary>
    private static string? AcknowledgementReason(string acknowledgement)
    {
        if (acknowledgement.Length == 0) return null;
        var unquoted = Quoted().Replace(acknowledgement, " ");
        if (Assumptive().IsMatch(unquoted)) return "leading";
        if (Loaded().IsMatch(unquoted)) return "loaded";
        return null;
    }

    /// <summary>Every quoted span must be verbatim in the last interviewee turn (case and whitespace aside). With no turn to check against, any quote fails.</summary>
    private static bool Misquoted(string text, string? lastInterviewee)
    {
        var spans = Quoted().Matches(text).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();
        if (spans.Length == 0) return false;
        if (lastInterviewee is null) return true;
        var heard = Normalise(lastInterviewee);
        return spans.Any(span => !heard.Contains(Normalise(span), StringComparison.Ordinal));
    }

    private static string Normalise(string s) => Spaces().Replace(s.Trim(), " ").ToLowerInvariant();

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>A second interrogative clause coordinated onto the first ("... and how did ...", "..., or what would ...", "... i jak ...", "... i czy też ..."): two asks in one turn.</summary>
    [GeneratedRegex(@"\b(and|or)\s+(what|how|why|who|whom|when|where|which)\b|\b(i|oraz|lub|albo|czy)\s+(co|jak|dlaczego|kiedy|gdzie|kto|kogo|który|która|które|czy\s+te[żz])\b", Opt, 200)]
    private static partial Regex SecondInterrogative();

    [GeneratedRegex(@"\b(don'?t|doesn'?t|didn'?t|isn'?t|wasn'?t|aren'?t|weren'?t|wouldn'?t|couldn'?t|shouldn'?t|haven'?t|hasn'?t)\s+(you|they|he|she|it|that|there|the|your)\b[^?]*\?|\b(wouldn'?t|don'?t)\s+you\s+(say|agree|think|feel|find)\b|\bczy\s+nie\b[^?]*\?|\bczyż\s+nie\b", Opt, 200)]
    private static partial Regex NegativePolar();

    [GeneratedRegex(@",\s*(isn'?t\s+it|wasn'?t\s+it|didn'?t\s+you|don'?t\s+you|weren'?t\s+you|wouldn'?t\s+you|aren'?t\s+you|right|correct|prawda|nieprawda|czyż\s+nie|nieprawdaż)\s*\?|\b(right|correct|true|prawda|nieprawda)\s*\?\s*$", Opt, 200)]
    private static partial Regex TagQuestion();

    [GeneratedRegex(@"\b(surely|obviously|clearly|of\s+course|naturally|you\s+must\s+(have|be)|i\s+(assume|imagine|guess|suppose)\s+(you|that|it)|oczywiście|zapewne|pewnie|na\s+pewno|bez\s+wątpienia|przecież)\b", Opt, 200)]
    private static partial Regex Assumptive();

    [GeneratedRegex(@"\bhow\s+(bad|terrible|awful|unfair|toxic|great|wonderful|horrible)\b|\bwhy\s+(was|were|is|are)\b[^?]{0,60}\b(bad|terrible|awful|unfair|toxic|horrible)\b|\b(frustrat\w+|disappoint\w+|angry|upset)\b[^?]*\bwith\b[^?]*\?|\bjak\s+(bardzo\s+)?(źle|okropnie|fatalnie|strasznie|wspaniale)\b", Opt, 200)]
    private static partial Regex Loaded();

    [GeneratedRegex(@"^\W*(did|do|does|was|were|is|are|have|has|had)\s+(you|they|your|the|it|there|that|he|she)\b", Opt, 200)]
    private static partial Regex ClosedStarter();

    [GeneratedRegex(@"^\W*czy\s+(miał|miała|był|była|było|mieli|dostał|dostała|otrzymał|otrzymała|zgłosił|zgłosiła|jest|są|został|została|mógł|mogła)\b", Opt, 200)]
    private static partial Regex PolishClosedStarter();

    /// <summary>An open invitation is as good as a question for a topic opener ("Tell me about ...", "Proszę opowiedzieć ...").</summary>
    [GeneratedRegex(@"(^|[.!?]\s+)(tell\s+me|describe|walk\s+me\s+through|share|talk\s+me\s+through|opowiedz\w*|opisz\w*|proszę\s+(mi\s+)?(opowiedzieć|opisać))\b", Opt, 200)]
    private static partial Regex Invitation();

    [GeneratedRegex(@"\b(example|specific|instance|particular\s+(situation|moment|time)|przykład\w*|konkretn\w*|sytuacj\w*|moment\w*)\b", Opt, 200)]
    private static partial Regex ExampleRequest();

    [GeneratedRegex(@"(^|\n)\s*(role|system|assistant|user)\s*:|<<<|>>>|system\s+prompt|\bdata\s+block\b|interview_data", Opt, 200)]
    private static partial Regex PromptLeak();

    /// <summary>A quoted span: the interviewee's own words in „…” or "…". The first group is the Polish form, the second the straight one.</summary>
    [GeneratedRegex(@"„([^”]+)”|""([^""]+)""", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Token();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\b1\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Anchor1();

    [GeneratedRegex(@"\b5\b", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Anchor5();
}
