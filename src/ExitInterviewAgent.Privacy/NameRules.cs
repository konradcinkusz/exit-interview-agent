using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Privacy;

/// <summary>
/// Heuristic detection of names of individuals. No model, no dictionary of real people: language patterns for English
/// and Polish (titles, relation constructions, capitalised runs, a small given-name lexicon, Polish surname shapes).
/// Recall is limited by design and measured (docs/privacy/pii-detector.md).
/// </summary>
internal sealed class NameRules
{
    private static readonly TimeSpan Timeout = RegexBudget.Timeout;
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    private const string Name = @"\p{Lu}[\p{L}\p{M}'’]{0,40}(?:-\p{Lu}[\p{L}\p{M}'’]{0,40}){0,2}";
    private const string Particle = @"(?:von|van|der|den|de|di|da|del|zu|le|la|bin|al|el)";
    private const string Names = $@"(?<names>{Name}(?:[ \t]+(?:{Particle}[ \t]+){{0,2}}{Name}){{0,2}})";

    private const string RelationEn =
        @"(?:manager|boss|supervisor|superior|lead|team\s+lead|tech\s+lead|director|colleague|coworker|co-worker|teammate|mentor|ceo|cto|cfo|coo|cio|vp|founder|owner|head|recruiter|hr\s+(?:manager|person|rep\w*)|account\s+manager|project\s+manager|product\s+owner|scrum\s+master|line\s+manager|team\s+leader)";

    private const string RelationPl =
        @"(?:szef\p{L}*|manager\p{L}*|menedżer\p{L}*|menadżer\p{L}*|kierownik\p{L}*|kierownicz\p{L}*|przełożon\p{L}*|dyrektor\p{L}*|prezes\p{L}*|lider\p{L}*|kole[gż]\p{L}*|współpracownik\p{L}*|współpracownic\p{L}*|opiekun\p{L}*|mentor\p{L}*|rekruter\p{L}*|właściciel\p{L}*)";

    private const string DeterminerEn = @"(?:my|our|his|her|their|the|a|an|new|old|former|previous|direct|immediate)";
    private const string DeterminerPl = @"(?:m[oó]j\p{L}*|moj\p{L}*|moim|nasz\p{L}*|jego|jej|ich|now[\p{L}]*|były|byłego|byłej|poprzedni\p{L}*|bezpośredni\p{L}*)";

    private const string Connector =
        @"(?:\s*[,:\-–—]\s*|\s+)(?:(?:named|called|is|was|being|były|była|był|jest|to|o\s+imieniu|imieniem|nazwiskiem|zwan[aey])\s+)*";

    private static Regex R(string pattern, RegexOptions extra = RegexOptions.None) => new(pattern, Opts | extra, Timeout);

    private static readonly Regex Title = R(
        $@"(?<![\p{{L}}\p{{N}}])(?i:Mr|Mrs|Ms|Miss|Mx|Dr|Prof|Professor|Sir|Madam|Pan|Pani|Panem|Panią|Pana|Panu|Panie|Państwo|mgr|inż|ks|dr\s+hab|prof)(?:\.|\s)\s*{Names}");

    private static readonly Regex TitleP = R($@"(?<![\p{{L}}\p{{N}}])(?i:p)\.\s*{Names}");

    private static readonly Regex Introduced = R(
        $@"(?<![\p{{L}}\p{{N}}])(?i:named|called|o\s+imieniu|imieniem|nazwiskiem|na\s+imię)\s+{Names}");

    private static readonly Regex Relation = R(
        $@"(?<![\p{{L}}\p{{N}}])(?i:(?:{DeterminerEn}\s+(?:\p{{L}}+\s+)?{RelationEn}|(?:{DeterminerPl}\s+)?{RelationPl})){Connector}{Names}");

    private static readonly Regex RelationReversed = R(
        $@"(?<names>{Name}(?:[ \t]+(?:{Particle}[ \t]+){{0,2}}{Name}){{0,2}})\s*[,(\-–—]\s*(?i:(?:my|our|mój|moja|nasz\p{{L}}*|moim|mojego|mojej)\s+(?:\p{{L}}+\s+)?(?:{RelationEn}|{RelationPl}))(?![\p{{L}}])");

    private static readonly Regex ReportsTo = R(
        $@"(?<![\p{{L}}\p{{N}}])(?i:report(?:ed|s|ing)?\s+(?:directly\s+)?to|worked\s+under|raportowa\p{{L}}*\s+do|podlega\p{{L}}*\s+(?:pod\s+)?(?:bezpośrednio\s+)?)\s+{Names}");

    private static readonly Regex Initials = R(@"(?<![\p{L}\p{N}])(?<names>(?:\p{Lu}\.\s*){1,2}\p{Lu}[\p{Ll}\p{M}'’\-]+)");

    private static readonly Regex Token = R(@"\p{L}[\p{L}\p{M}'’]*(?:-\p{L}[\p{L}\p{M}'’]*)*");

    private static readonly Regex PolishSurname = R(
        @"^\p{Lu}\p{Ll}{2,}(?:ski|ska|skiego|skiej|skim|ską|scy|ccy|cki|cka|ckiego|ckiej|ckim|cką|dzki|dzka|dzkiego|dzkiej|dzkim|dzką|wicz|wicza|wiczem|wiczowi|wiczu|owski|owska|owskiego|owskiej|owskim|owską)$");

    private readonly bool _failClosed;
    private readonly Regex[] _allow;

    public NameRules(PiiOptions options)
    {
        _failClosed = options.FailClosed;
        _allow = options.AllowList
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a =>
            {
                var words = a.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
                var suffix = a.Trim().Length >= 5 ? @"\p{L}{0,3}" : "";
                return R($@"(?<![\p{{L}}\p{{N}}]){string.Join(@"\s+", words)}{suffix}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);
            })
            .ToArray();
    }

    private sealed record Tok(int Start, int End, string Text, string Lower, bool NameShaped, bool Blocked, bool SentenceStart);

    public void Detect(string text, List<PiiFinding> into)
    {
        var allowed = new List<(int Start, int End)>();
        foreach (var rx in _allow)
            foreach (Match m in rx.Matches(text)) allowed.Add((m.Index, m.Index + m.Length));

        var tokens = Tokenise(text, allowed);
        var covered = new bool[tokens.Count];

        // Rules with an explicit cue: the capture group is a name, whatever the lexicon says.
        foreach (var rx in new[] { Title, TitleP, Introduced, Relation, RelationReversed, ReportsTo, Initials })
            foreach (Match m in rx.Matches(text))
            {
                var g = m.Groups["names"];
                if (g.Success) AddRun(tokens, covered, g.Index, g.Index + g.Length, PiiBasis.Heuristic, into, isInitials: rx == Initials);
            }

        // Runs of two or more capitalised tokens: "Firstname Lastname".
        for (var i = 0; i < tokens.Count; i++)
        {
            if (covered[i] || !Eligible(tokens[i])) continue;
            var j = i;
            while (j + 1 < tokens.Count)
            {
                var next = j + 1;
                var gap = text.Substring(tokens[j].End, tokens[next].Start - tokens[j].End);
                if (gap is not (" " or "\t")) break;
                if (Eligible(tokens[next]) && !covered[next]) { j = next; continue; }
                var p = next;
                while (p < tokens.Count && Lexicon.NameParticles.Contains(tokens[p].Text)
                       && p + 1 < tokens.Count && text.Substring(tokens[p].End, tokens[p + 1].Start - tokens[p].End) == " ") p++;
                if (p > next && p < tokens.Count && Eligible(tokens[p]) && !covered[p]) { j = p; continue; }
                break;
            }

            if (j > i)
            {
                MarkRun(tokens, covered, i, j, PiiBasis.Heuristic, into);
                i = j;
            }
        }

        // Single tokens.
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (covered[i] || !Eligible(t)) continue;

            var givenName = Lexicon.GivenNames.Contains(t.Lower);
            if (givenName && (!t.SentenceStart || !Lexicon.AmbiguousGiven.Contains(t.Lower)))
            {
                Emit(t, PiiBasis.Heuristic, into);
                covered[i] = true;
            }
            else if (!t.SentenceStart && PolishSurname.IsMatch(t.Text))
            {
                Emit(t, PiiBasis.Heuristic, into);
                covered[i] = true;
            }
            else if (_failClosed && (givenName || !t.SentenceStart))
            {
                Emit(t, PiiBasis.FailClosed, into);
                covered[i] = true;
            }
        }
    }

    private static bool Eligible(Tok t) => t.NameShaped && !t.Blocked;

    private List<Tok> Tokenise(string text, List<(int Start, int End)> allowed)
    {
        var list = new List<Tok>();
        foreach (Match m in Token.Matches(text))
        {
            var lower = m.Value.ToLowerInvariant();
            var shaped = IsNameShaped(m.Value);
            var blocked = Lexicon.Stop.Contains(lower)
                          || allowed.Any(a => m.Index >= a.Start && m.Index + m.Length <= a.End)
                          || lower.Split('-').Any(part => part.Length > 1 && Lexicon.Stop.Contains(part) && shaped);
            list.Add(new Tok(m.Index, m.Index + m.Length, m.Value, lower, shaped, blocked, IsSentenceStart(text, m.Index)));
        }

        return list;
    }

    /// <summary>First letter upper-case and at least one lower-case letter after it ("Anna", "O'Neil"; not "HR", not "iPhone").</summary>
    private static bool IsNameShaped(string s)
    {
        if (s.Length < 2 || !char.IsUpper(s[0])) return false;
        return s.Skip(1).Any(char.IsLower);
    }

    private static bool IsSentenceStart(string text, int index)
    {
        var i = index - 1;
        while (i >= 0)
        {
            var c = text[i];
            if (c is '\n' or '\r') return true;
            if (char.IsWhiteSpace(c) || c is '"' or '\'' or '“' or '„' or '(' or '[' or '-' or '–' or '—' or '*' or '•' or '«') { i--; continue; }
            return c is '.' or '!' or '?' or '…' or ':' or ';';
        }

        return true;
    }

    private void AddRun(List<Tok> tokens, bool[] covered, int start, int end, PiiBasis basis, List<PiiFinding> into, bool isInitials)
    {
        if (isInitials)
        {
            // Initials are not tokens ("J."): flag the whole span unless its surname token is blocked.
            var last = tokens.FirstOrDefault(t => t.End == end);
            if (last is { Blocked: true }) return;
            into.Add(new PiiFinding(PiiKind.PersonName, start, end - start, basis));
            foreach (var idx in Enumerable.Range(0, tokens.Count).Where(k => tokens[k].Start >= start && tokens[k].End <= end)) covered[idx] = true;
            return;
        }

        var inside = Enumerable.Range(0, tokens.Count).Where(k => tokens[k].Start >= start && tokens[k].End <= end).ToList();
        var runStart = -1;
        var runEnd = -1;
        foreach (var k in inside)
        {
            var t = tokens[k];
            var ok = t.NameShaped ? !t.Blocked : IsAllCapsName(t) || (Lexicon.NameParticles.Contains(t.Text) && runStart >= 0);
            if (t.NameShaped && t.Blocked) ok = false;
            if (ok) { if (runStart < 0) runStart = k; runEnd = k; }
            else if (runStart >= 0) { MarkRun(tokens, covered, runStart, runEnd, basis, into); runStart = -1; }
        }

        if (runStart >= 0) MarkRun(tokens, covered, runStart, runEnd, basis, into);
    }

    private static bool IsAllCapsName(Tok t) => t.Text.Length >= 2 && t.Text.All(c => char.IsUpper(c) || c is '-' or '\'') && !Lexicon.Stop.Contains(t.Lower);

    private static void MarkRun(List<Tok> tokens, bool[] covered, int from, int to, PiiBasis basis, List<PiiFinding> into)
    {
        // Trim particles at the ends ("de" in "de Smith" is still part of the name, but "Smith de" is not).
        while (to > from && Lexicon.NameParticles.Contains(tokens[to].Text)) to--;
        for (var k = from; k <= to; k++) covered[k] = true;
        into.Add(new PiiFinding(PiiKind.PersonName, tokens[from].Start, tokens[to].End - tokens[from].Start, basis));
    }

    private static void Emit(Tok t, PiiBasis basis, List<PiiFinding> into) =>
        into.Add(new PiiFinding(PiiKind.PersonName, t.Start, t.End - t.Start, basis));
}
