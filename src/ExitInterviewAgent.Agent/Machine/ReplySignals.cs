using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Protocol;

namespace ExitInterviewAgent.Agent.Machine;

public enum ConsentAnswer { Yes, No, Unclear }

/// <summary>
/// What the deterministic analyser read from one (already masked) reply. Booleans and counts only, so it can be
/// traced. <see cref="Polarity"/> is a protocol signal (does this answer lean one way about the employer?) used only to
/// notice contradictions; it is never stored, never put in the record, and is not an assessment of the person.
/// <see cref="Serious"/> opens the deepening phase; <see cref="DeepCovered"/> is a bit mask of the <see cref="DeepFocus"/>
/// elements the reply already describes, so the menu does not ask about them again.
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
    int Polarity,
    bool Serious = false,
    int DeepCovered = 0,
    bool Short = false,
    bool FinishRequest = false);

/// <summary>
/// Rule-based reading of a reply. Decisions that protect the interviewee (withdrawal, consent) and the shape of the
/// interview (probe, clarify, deepen, close) come from here and the state machine, never from model output. The cue lists are
/// small, English and Polish; each is a floor, not a classifier. Stems are matched with a wildcard suffix, so they cover the
/// common inflections of a listed root and nothing else. Their false negatives and positives are documented in
/// docs/architecture/interview-agent.md. A reply is analysed after PII masking, so a name cannot hide a cue.
/// </summary>
public static partial class ReplyAnalyzer
{
    public static ReplySignals Analyze(string maskedReply, ProtocolLimits limits, bool namesPerson, int previousPolarity = 0)
    {
        ArgumentNullException.ThrowIfNull(maskedReply);
        var words = CountWords(maskedReply);
        var terse = words <= limits.TerseWordLimit;
        // Polish typed without diacritics ("zle", "zwolnili mnie", "bylem") is common on a terminal: every cue is read on the text as typed
        // and on its ASCII fold, so a missing letter cannot hide it.
        var folded = Fold(maskedReply);
        bool Any(Func<string, bool> f) => f(maskedReply) || (!ReferenceEquals(folded, maskedReply) && f(folded));
        var polarity = Polarity(maskedReply, folded);
        var concrete = Any(t => ConcreteCue().IsMatch(t));
        var serious = Any(SeriousAccount);
        return new ReplySignals(
            words,
            Any(t => IsWithdrawal(t, words)),
            Any(t => ConsentOf(t) == ConsentAnswer.Yes) ? ConsentAnswer.Yes : ConsentOf(maskedReply),
            terse,
            !terse && words <= 25 && Any(t => VagueCue().IsMatch(t)) && !concrete,
            Any(t => HostileCue().IsMatch(t)),
            previousPolarity != 0 && polarity != 0 && Math.Sign(previousPolarity) != Math.Sign(polarity),
            namesPerson,
            InjectionCue().IsMatch(maskedReply),
            polarity,
            serious,
            DeepCoverage(maskedReply) | DeepCoverage(folded),
            IsBrief(words, terse, concrete, Any(Evaluates)),
            Any(t => FinishRequestCue().IsMatch(t)));
    }

    /// <summary>
    /// A short answer that says something ("bardzo dobrze", "słabe", "bywało różnie") but gives nothing concrete: worth one follow-up.
    /// A bare non-answer ("nie wiem", "tak") is not: it is let go, and only a streak of those closes the interview.
    /// </summary>
    private static bool IsBrief(int words, bool terse, bool concrete, bool evaluates) => words <= 8 && !concrete && (!terse || evaluates);

    /// <summary>A judgement word ("dobrze", "słabe", "terrible"); "nothing" and "never" are in the polarity list but judge nothing.</summary>
    private static bool Evaluates(string text)
    {
        foreach (Match m in PositiveCue().Matches(text)) return true;
        foreach (Match m in NegativeCue().Matches(text))
            if (!m.Value.Equals("nothing", StringComparison.OrdinalIgnoreCase) && !m.Value.Equals("never", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The text with Polish letters replaced by their ASCII base; the same instance when nothing changed.</summary>
    internal static string Fold(string text)
    {
        var changed = false;
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var f = chars[i] switch
            {
                'ą' => 'a',
                'ć' => 'c',
                'ę' => 'e',
                'ł' => 'l',
                'ń' => 'n',
                'ó' => 'o',
                'ś' => 's',
                'ź' or 'ż' => 'z',
                'Ą' => 'A',
                'Ć' => 'C',
                'Ę' => 'E',
                'Ł' => 'L',
                'Ń' => 'N',
                'Ó' => 'O',
                'Ś' => 'S',
                'Ź' or 'Ż' => 'Z',
                var c => c,
            };
            if (f != chars[i]) { chars[i] = f; changed = true; }
        }
        return changed ? new string(chars) : text;
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
        Withdrew(text) || (words ?? CountWords(text)) <= 2 && BareStop().IsMatch(text);

    public static ConsentAnswer ConsentOf(string text)
    {
        if (Withdrew(text)) return ConsentAnswer.No;
        if (NoAnswer().IsMatch(text)) return ConsentAnswer.No;
        return YesAnswer().IsMatch(text) ? ConsentAnswer.Yes : ConsentAnswer.Unclear;
    }

    /// <summary>
    /// The serious-account signal (ADR-0075): a cue that is not negated. A negation counts when it is one of the three words
    /// before the cue ("there was no bullying", "nie było mobbingu"). It does not look after the cue, so "mobbing did not happen"
    /// still counts. This list is a lexical floor: a paraphrase it does not name is not seen, and a term used in a negated
    /// sentence outside that three-word window is read as serious.
    /// </summary>
    public static bool SeriousAccount(string text)
    {
        foreach (Match m in SeriousCue().Matches(text))
            if (!Negated(text, m.Index)) return true;
        return false;
    }

    /// <summary>Bit <c>1 &lt;&lt; (int)focus</c> for each deepening element the reply already describes (a keyword floor, EN and PL).</summary>
    public static int DeepCoverage(string text)
    {
        var mask = 0;
        foreach (var focus in Enum.GetValues<DeepFocus>())
            if (Covers(focus, text)) mask |= 1 << (int)focus;
        return mask;
    }

    private static bool Covers(DeepFocus focus, string text) => focus switch
    {
        DeepFocus.WhatHappened => MenuWhatHappened().IsMatch(text),
        DeepFocus.WhenHowOften => MenuWhenHowOften().IsMatch(text),
        DeepFocus.WhoByRole => MenuWho().IsMatch(text),
        DeepFocus.WhatTheyDidAndResponse => MenuResponse().IsMatch(text),
        _ => MenuOutcome().IsMatch(text),
    };

    private static bool Negated(string text, int index)
    {
        foreach (var w in WordToken().Matches(text[..index]).TakeLast(3))
        {
            var token = w.Value.Replace('’', '\'').ToLowerInvariant();
            if (token is "nie" or "bez" or "nigdy" or "no" or "not" or "never" or "without" or "nor" || token.EndsWith("n't", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool Withdrew(string text) => WithdrawalCue().IsMatch(text) || PolishEnding().IsMatch(text);

    private static int Polarity(string text, string folded)
    {
        var (pos, neg) = CueCounts(ReferenceEquals(text, folded) ? text : text + " " + folded);
        return Math.Sign(pos - neg);
    }

    /// <summary>Counts of positive and negative practice cues, for the contradiction check and the scripted mock extractor.</summary>
    internal static (int Positive, int Negative) CueCounts(string text) => (PositiveCue().Count(text), NegativeCue().Count(text));

    internal static bool HasConcreteCue(string text) => ConcreteCue().IsMatch(text);

    internal static bool HasHostileCue(string text) => HostileCue().IsMatch(text);

    /// <summary>True when a text reads like an instruction aimed at the interviewer, the extractor or a judge. Observability and quote filtering only: it never changes the protocol.</summary>
    public static bool LooksLikeInjection(string text) => InjectionCue().IsMatch(text);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"\b(i\s+(want|would\s+like|wish|need)\s+to\s+(stop|end|quit|finish)|let'?s\s+stop|stop\s+(the|this)\s+(interview|conversation)|(end|cancel)\s+(the|this)\s+(interview|conversation)|(i\s+)?(withdraw|revoke|take\s+back)\s+(my\s+)?consent|i\s+(do\s+not|don'?t|no\s+longer)\s+consent|i\s+changed\s+my\s+mind|please\s+stop|i'?m\s+stopping|delete\s+(this|everything|my\s+answers)|wycofuj\w*|przerwij\w*|chc[ęe]\s+przerwa\w+|nie\s+zgadzam\s+si\w+|nie\s+chc[ęe]\s+(ju[żz]\s+)?dalej|ko[ńn]czymy\s+(ten\s+|ju[żz]\s+|tu\s+)?(wywiad\w*|rozmow\w*|tutaj|tu)|przerwa[ćc]\s+(ju[żz]\s+)?(wywiad\w*|rozmow\w*)|proszę\s+o\s+stop|stop,?\s+proszę)\b", Opt, 200)]
    private static partial Regex WithdrawalCue();

    /// <summary>"Kończymy." as the whole of a reply (or its last words): the Polish "we are finishing". Not a bare word, so "kończymy projekty" is not a withdrawal.</summary>
    [GeneratedRegex(@"\bko[ńn]czymy\W*$", Opt, 200)]
    private static partial Regex PolishEnding();

    [GeneratedRegex(@"^\W*(stop|quit|exit|cancel|enough|koniec|dość|dosyć|wystarczy|przerwij)\W*(please|proszę)?\W*$", Opt, 200)]
    private static partial Regex BareStop();

    [GeneratedRegex(@"^\W*((no|nope|nah|nie)\b(?!\s+(problem|worries))|i\s+(do\s+not|don'?t)\s+(agree|want)|i\s+(decline|refuse)|i'?d\s+rather\s+not|not\s+(now|really|comfortable))", Opt, 200)]
    private static partial Regex NoAnswer();

    [GeneratedRegex(@"^\W*(yes|yep|yeah|yup|sure|ok(ay)?|alright|agreed|tak|zgadzam\s+si\w+|go\s+ahead|that'?s\s+fine|fine|no\s+(problem|worries)|i\s+(do\s+)?(agree|consent)|i\s+agree)\b", Opt, 200)]
    private static partial Regex YesAnswer();

    [GeneratedRegex(@"\b(fine|ok|okay|good|bad|great|nice|terrible|awful|meh|so-so|whatever|stuff|things|generally|kind\s+of|sort\s+of|you\s+know|the\s+usual|average|normal|alright|not\s+great|not\s+bad|w\s+porz[ąa]dku|dobrze|r[óo]żnie|nie\s+wiem|tak\s+sobie|og[óo]lnie|jako[śs]|normalnie|[śs]rednio|przeci[ęe]tnie|zwyczajnie)\b", Opt, 200)]
    private static partial Regex VagueCue();

    [GeneratedRegex(@"(\d|\b(january|february|march|april|may|june|july|august|september|october|november|december|monday|tuesday|wednesday|thursday|friday)\b|\b(for\s+example|for\s+instance|such\s+as|e\.g\.|specifically|one\s+time|once|last\s+(week|month|year|quarter)|the\s+day|that\s+day|when\s+(i|we|they|my)|because|after|before)\b|\b(kiedy|wtedy|konkretnie|na\s+przyk[łl]ad|poniewa[żz]|tego\s+dnia|w\s+zesz\w*|razu|miesi[ąa]c\w*|tydzie[ńn]|tygodni\w*|rok\w*)\b)", Opt, 200)]
    private static partial Regex ConcreteCue();

    [GeneratedRegex(@"\b(waste\s+of\s+(my\s+)?time|stupid|idiot\w*|pointless|shut\s+up|none\s+of\s+your\s+business|leave\s+me\s+alone|stop\s+asking|ridiculous|garbage|useless|sick\s+of|damn|crap|bzdur\w*|g[łl]upi\w*|strata\s+czasu|nie\s+twoja\s+sprawa|daj\s+mi\s+spok[óo]j|zostaw\s+mnie|bez\s+sensu|debil\w*|kretyn\w*|beznadziej\w*)\b", Opt, 200)]
    private static partial Regex HostileCue();

    [GeneratedRegex(@"\b(ignore|disregard|forget)\s+(all\s+|any\s+|your\s+|the\s+)?(previous|prior|above|earlier)?\s*(instructions|rules|prompt)|system\s+prompt|you\s+are\s+now\b|new\s+instructions|as\s+an?\s+(ai\s+)?(evaluator|judge|grader)|note\s+to\s+(the\s+)?(evaluator|judge|grader|extractor)|set\s+(all\s+)?ratings?|</?\s*(system|data|interview_data)|\bdebug\s+mode\b|zignoruj\w*\s+(wszystkie\s+|poprzednie\s+|swoje\s+|wcze[śs]niejsze\s+)?(instrukcj\w*|zasad\w*|polece\w*)|poprzedni\w*\s+(instrukcj\w*|polece\w*)|nowe\s+instrukcj\w*|jeste[śs]\s+teraz\b|ocen\s+to\s+jako|ustaw\s+(wszystkie\s+)?oceny|prompt\s+systemowy", Opt, 200)]
    private static partial Regex InjectionCue();

    [GeneratedRegex(@"\b(supportive|helpful|great|good|excellent|fair|generous|clear|welcoming|friendly|well\s+organi[sz]ed|respectful|enjoyed|loved|happy\s+with|well\s+paid|opportunit\w+|trusted|inclusive|dobr\w*|[śs]wietn\w*|super|fajn\w*|wspania[łl]\w*|pozytywn\w*|rewelacyjn\w*|przyjazn\w*|uczciw\w*)\b", Opt, 200)]
    private static partial Regex PositiveCue();

    [GeneratedRegex(@"\b(unsupportive|unhelpful|bad|terrible|awful|poor|unfair|stingy|unclear|chaotic|disorgani[sz]ed|disrespectful|toxic|hated|unhappy|underpaid|no\s+support|no\s+help|never|nothing|broken\s+promises?|ignored|micromanag\w+|overworked|burn\w*|[źz]le|s[łl]ab\w*|fatal\w*|kiepsk\w*|okropn\w*|tragiczn\w*|toksyczn\w*|nieuczciw\w*|niesprawiedliw\w*|chaotyczn\w*|chaos\w*|bez\s+wsparcia|brak\s+\w+|nie\s+by[łl]o|nie\s+ma\w*|za\s+(ma[łl]o|du[żz]o))\b", Opt, 200)]
    private static partial Regex NegativeCue();

    /// <summary>
    /// The serious-account floor (ADR-0075, Y2). It is a fixed list of roots, not a classifier: a paraphrase it does not name is missed
    /// (the deepening then does not start), and a term it names can fire in a harmless sense. Stems carry a wildcard suffix so
    /// Polish and English inflections are covered; a Polish form the list does not stem to (for example a rare declension) is missed.
    /// </summary>
    /// <summary>A request to end the interview early but keep what was said ("możemy już skończyć?", "can we wrap up"): distinct from withdrawal, which discards everything.</summary>
    [GeneratedRegex(@"\b(mo[żz]emy|mog[ęe]|czy\s+mo[żz]emy|prosz[ęe])\s+(ju[żz]\s+)?(sko[ńn]czy[ćc]|ko[ńn]czy[ćc]|zako[ńn]czy[ćc])|\bsko[ńn]czmy\b|\bwystarczy\s+ju[żz]\b|\bcan\s+we\s+(please\s+)?(finish|wrap\s+up|end|stop)\b|\bcould\s+we\s+(finish|wrap\s+up|end)\b|\blet'?s\s+wrap\s+up\b", Opt, 200)]
    private static partial Regex FinishRequestCue();

    [GeneratedRegex(@"\b(mobb?ing\w*|gn[ęe]bi\w*|n[ęe]ka\w*|poni[żz]a\w*|poni[żz]y\w*|bull(y|ied|ying|ies)\w*|harass\w*|molest\w*|discriminat\w*|dyskrymin\w*|threat\w*|gro[źżz]\w*|retaliat\w*|odwet\w*|zemst\w*|wyzysk\w*|exploit\w*|unsafe|niebezpiecz\w*|wage\s+theft|withheld\s+(pay|wages|salary)|nie\s+wyp[łl]ac\w*|niewyp[łl]ac\w*|unpaid\s+(wages|overtime|salary)|upok[oa]r\w*|(?>zwolni\w*)(?!\s+si[ęe]\b)|wyrzuc\w*|fired|laid\s+off|sacked|dismissed|humiliat\w*|wyzywa\w*|obra[żz]a\w*|prze[śs]ladow\w*|persecut\w*|szykan\w*)", Opt, 200)]
    private static partial Regex SeriousCue();

    [GeneratedRegex(@"[\p{L}'’]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex WordToken();

    [GeneratedRegex(@"\b(happen\w*|incident\w*|situation\w*|event\w*|shout\w*|exclud\w*|ignor\w*|told\s+me|said\s+(that|i|to\s+me)|sent\s+me|wrote|called\s+me|wydarzy\w*|zdarzy\w*|sytuacj\w*|incydent\w*|krzycz\w*|krzyk\w*|wykluczy\w*|zignorowa\w*|powiedzia\w*|m[óo]wi\w*|wysła\w*|napisa\w*|zadzwoni\w*)\b", Opt, 200)]
    private static partial Regex MenuWhatHappened();

    [GeneratedRegex(@"\b(\d+|monday|tuesday|wednesday|thursday|friday|saturday|sunday|often|every|each|weekly|daily|monthly|sometimes|always|usually|twice|once|since|last\s+(week|month|year)|poniedzia[łl]\w*|wtorek|wtorku|środ\w*|czwart\w*|pi[ąa]t\w*|sobot\w*|niedziel\w*|cz[ęe]sto|zawsze|codziennie|co\s+tydzie[ńn]|czasem|raz|kiedy|wtedy|w\s+zesz\w*|miesi[ąa]c\w*|tydzie[ńn]|tygodni\w*|rok\w*|lat\w*|dnia|dni)\b", Opt, 200)]
    private static partial Regex MenuWhenHowOften();

    [GeneratedRegex(@"\b(manager|boss|colleague|coworker|co-worker|team\s*lead|director|supervisor|hr|head\s+of|client|peer|przełożon\w*|kierownik\w*|szef\w*|dyrekt\w*|współpracown\w*|koleg\w*|kole[żz]\w*|lider\w*|prezes\w*|klient\w*)\b", Opt, 200)]
    private static partial Regex MenuWho();

    [GeneratedRegex(@"\b(reported|report\w*|complain\w*|zg[łl]osi\w*|skarg\w*|respon\w*|reakcj\w*|zareagow\w*|reagow\w*|interwen\w*|nothing\s+was\s+done|did\s+nothing|no\s+action|nic\s+nie|hr\s+(did|said|was|ignored)|company\s+(did|said|never|refused)|firma\s+(nic|zareag\w*|odpowied\w*))\b", Opt, 200)]
    private static partial Regex MenuResponse();

    [GeneratedRegex(@"\b(end(ed|s|ing)?|left|quit|resigned|in\s+the\s+end|eventually|finally|affected|stres\w*|stress\w*|anxi\w*|sick|burn\w*|health|sleep\w*|meant|zako[ńn]czy\w*|odszed\w*|odesz\w*|w\s+ko[ńn]cu|ostatecznie|wp[łl]yn\w*|zdrow\w*|zrezygnow\w*|zwolni\w*|skończ\w*|skonczy\w*)\b", Opt, 200)]
    private static partial Regex MenuOutcome();
}
