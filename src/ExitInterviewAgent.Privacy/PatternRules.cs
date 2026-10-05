using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Privacy;

/// <summary>Structural rules: formats that identify a person or an account regardless of the language around them.</summary>
internal static class PatternRules
{
    private static readonly TimeSpan Timeout = RegexBudget.Timeout;
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    private static Regex R(string pattern, RegexOptions extra = RegexOptions.None) => new(pattern, Opts | extra, Timeout);

    private static readonly Regex Email = R(
        @"[\p{L}\p{N}][\p{L}\p{N}._%+\-']{0,63}@[\p{L}\p{N}](?:[\p{L}\p{N}\-]{0,61}[\p{L}\p{N}])?(?:\.[\p{L}\p{N}](?:[\p{L}\p{N}\-]{0,61}[\p{L}\p{N}])?){0,6}\.\p{L}{2,24}");

    private static readonly Regex EmailObfuscated = R(
        @"[\p{L}\p{N}][\p{L}\p{N}._%+\-']{0,63}\s?(?:\[at\]|\(at\)|\{at\})\s?[\p{L}\p{N}\-]{1,63}(?:\s?(?:\[dot\]|\(dot\)|\.)\s?[\p{L}\p{N}\-]{1,63}){1,6}", RegexOptions.IgnoreCase);

    private static readonly Regex UrlScheme = R(@"(?:https?|ftp)://[^\s<>""'`]+|www\.[^\s<>""'`]+", RegexOptions.IgnoreCase);

    private static readonly Regex UrlBare = R(
        @"(?<![\p{L}\p{N}@.\-])(?:[a-z0-9](?:[a-z0-9\-]*[a-z0-9])?\.)+(?:com|org|net|pl|eu|io|co|uk|de|info|biz|dev|app|ai|me|us|edu|gov|xyz|online|tech|cloud)(?![\p{L}\p{N}\-])(?:/[^\s<>""'`]*)?",
        RegexOptions.IgnoreCase);

    private static readonly Regex IpV4 = R(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.]*\d)");

    private static readonly Regex IpV6 = R(@"(?<![\p{L}\p{N}:])(?:[0-9a-f]{1,4}:){7}[0-9a-f]{1,4}(?![\p{L}\p{N}:])", RegexOptions.IgnoreCase);

    private static readonly Regex Handle = R(@"(?<![\p{L}\p{N}_@.])@[A-Za-z0-9_](?:[A-Za-z0-9_.]{1,28}[A-Za-z0-9_])");

    private static readonly Regex SocialId = R(
        @"\b(?:linkedin|twitter|instagram|facebook|github|telegram|discord|skype|whatsapp|signal)\s*[:/]\s*@?(?<id>[a-z0-9][a-z0-9_.\-]{2,29})", RegexOptions.IgnoreCase);

    // Single separators only: "10 000 - 12 000" (a salary range) has two separators between its groups and does not match.
    private static readonly Regex PhoneCandidate = R(
        @"(?<![\p{L}\p{N}.,])(?<plus>\+|00)?(?:\(\d{1,4}\)[ .\-]?)?\d(?:[ .\-]?\d){8,14}(?![\p{L}\p{N}])");

    private static readonly Regex PhoneKeyword = R(
        @"\b(?:tel|telefon|phone|mobile|cell|call|zadzwo[ńn]|numer|nr|kom|komórka|whatsapp)\b\.?[:\s]*(?<num>[+(]?\d[\d\s().\-]{5,18}\d)", RegexOptions.IgnoreCase);

    private static readonly Regex DigitRun11 = R(@"(?<![\d])\d{11}(?!\d)");
    private static readonly Regex Nip = R(@"(?<![\d\-])(?:\d{3}-\d{3}-\d{2}-\d{2}|\d{3}-\d{2}-\d{2}-\d{3}|\d{10})(?![\d\-])");
    private static readonly Regex KeywordedId = R(
        @"\b(?:regon|krs|nip|pesel|dowód|dowod|paszport|passport|ssn|nino|id\s*card)\b[\s:#.=\-]*(?<num>(?-i:(?:PL)?[A-Z]{0,3})\s?\d[\d\- ]{6,16}\d)", RegexOptions.IgnoreCase);
    private static readonly Regex PlIdCard = R(@"(?<![A-Za-z0-9])[A-Z]{3}\s?\d{6}(?![A-Za-z0-9])");
    private static readonly Regex Passport = R(@"(?<![A-Za-z0-9])[A-Z]{2}\s?\d{7}(?![A-Za-z0-9])");
    private static readonly Regex Ssn = R(@"(?<![\d\-])\d{3}-\d{2}-\d{4}(?![\d\-])");
    private static readonly Regex UkNino = R(@"(?<![A-Za-z0-9])[A-CEGHJ-PR-TW-Z]{2}\s?\d{2}\s?\d{2}\s?\d{2}\s?[A-D](?![A-Za-z0-9])");
    private static readonly Regex Iban = R(@"(?<![A-Za-z0-9])(?:[A-Z]{2}\d{2}(?:\s?\d{4}){3,7}(?:\s?\d{1,4})?|\d{2}(?:\s?\d{4}){6})(?![A-Za-z0-9])");

    private static readonly Regex EmployeeLabelled = R(
        @"\b(?:(?:employee|emp|staff|badge|payroll|worker|personnel|pracownik\p{L}*|hr)\s*(?:id|no\.?|number|nr\.?|numer|#)|numer\s+pracownika|nr\s+pracownika)(?:\s+(?:is|was|to|jest|był))?\s*[:#=]?\s*(?<num>[A-Za-z]{0,4}[-_]?\d{3,10})\b", RegexOptions.IgnoreCase);
    private static readonly Regex EmployeeBare = R(@"(?<![A-Za-z0-9])(?:EMP|EID|PRC|EE|ID)[-_ ]?\d{4,8}(?![A-Za-z0-9])");

    public static void Detect(string text, bool failClosed, List<PiiFinding> into)
    {
        Add(Email, text, PiiKind.Email, into);
        Add(EmailObfuscated, text, PiiKind.Email, into);
        AddUrls(text, into);
        Add(IpV4, text, PiiKind.IpAddress, into, m => !IsVersionContext(text, m.Index));
        Add(IpV6, text, PiiKind.IpAddress, into);
        Add(Handle, text, PiiKind.Handle, into);
        AddGroup(SocialId, "id", text, PiiKind.Handle, into);
        AddNationalIds(text, into);
        AddGroup(EmployeeLabelled, "num", text, PiiKind.EmployeeId, into);
        Add(EmployeeBare, text, PiiKind.EmployeeId, into);
        AddPhones(text, failClosed, into);
    }

    private static void Add(Regex rx, string text, PiiKind kind, List<PiiFinding> into, Func<Match, bool>? accept = null, PiiBasis basis = PiiBasis.Pattern)
    {
        foreach (Match m in rx.Matches(text))
            if (accept?.Invoke(m) ?? true) into.Add(new PiiFinding(kind, m.Index, m.Length, basis));
    }

    private static void AddGroup(Regex rx, string group, string text, PiiKind kind, List<PiiFinding> into)
    {
        foreach (Match m in rx.Matches(text))
        {
            var g = m.Groups[group];
            if (g.Success && g.Length > 0) into.Add(new PiiFinding(kind, g.Index, g.Length, PiiBasis.Pattern));
        }
    }

    /// <summary>"version 2.14.7.3" is shaped like an IPv4 address but is not one.</summary>
    private static bool IsVersionContext(string text, int index)
    {
        var from = Math.Max(0, index - 14);
        return Regex.IsMatch(text[from..index], @"(?i)(?<![\p{L}])(?:version|ver\.?|v|release|build|rev|wersja|wersji)\s*$", Opts, Timeout);
    }

    private static void AddUrls(string text, List<PiiFinding> into)
    {
        foreach (var rx in new[] { UrlScheme, UrlBare })
            foreach (Match m in rx.Matches(text))
            {
                var length = TrimTrailingPunctuation(m.Value);
                var value = m.Value[..length];
                if (Lexicon.NotHosts.Contains(value)) continue;
                if (length > 0) into.Add(new PiiFinding(PiiKind.Url, m.Index, length, PiiBasis.Pattern));
            }
    }

    private static int TrimTrailingPunctuation(string s)
    {
        var end = s.Length;
        while (end > 0 && ".,;:!?)]}\"'".Contains(s[end - 1])) end--;
        return end;
    }

    private static void AddNationalIds(string text, List<PiiFinding> into)
    {
        // A contiguous 11-digit run (PESEL shape) or a 10-digit NIP-shaped run is masked whatever its checksum says
        // (a mistyped id is still an id); a valid checksum is what makes it a Pattern rather than a Heuristic finding.
        foreach (Match m in DigitRun11.Matches(text))
            into.Add(new PiiFinding(PiiKind.NationalId, m.Index, m.Length, PeselValid(m.Value) ? PiiBasis.Pattern : PiiBasis.Heuristic));

        foreach (Match m in Nip.Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsAsciiDigit).ToArray());
            into.Add(new PiiFinding(PiiKind.NationalId, m.Index, m.Length,
                NipValid(digits) || m.Value.Contains('-') ? PiiBasis.Pattern : PiiBasis.Heuristic));
        }

        AddGroup(KeywordedId, "num", text, PiiKind.NationalId, into);
        Add(PlIdCard, text, PiiKind.NationalId, into, m => !Lexicon.CurrencyCodes.Contains(m.Value[..3]));
        Add(Passport, text, PiiKind.NationalId, into, m => !Lexicon.CurrencyCodes.Contains(m.Value[..2]));
        Add(Ssn, text, PiiKind.NationalId, into);
        Add(UkNino, text, PiiKind.NationalId, into);
        Add(Iban, text, PiiKind.NationalId, into);
    }

    private static void AddPhones(string text, bool failClosed, List<PiiFinding> into)
    {
        foreach (Match m in PhoneCandidate.Matches(text))
        {
            var digits = m.Value.Count(char.IsAsciiDigit);
            var hasPlus = m.Groups["plus"].Success;
            if (digits is < 9 or > 15) continue;
            if (IsThousandsGrouped(m.Value) && !hasPlus) continue;
            if (IsDateLike(m.Value)) continue;
            into.Add(new PiiFinding(PiiKind.Phone, m.Index, m.Length, PiiBasis.Pattern));
        }

        AddGroup(PhoneKeyword, "num", text, PiiKind.Phone, into);

        if (failClosed)
            foreach (Match m in Regex.Matches(text, @"(?<![\d.,])\d{7,8}(?![\d])", Opts, Timeout))
                if (!IsDateLike(m.Value)) into.Add(new PiiFinding(PiiKind.Phone, m.Index, m.Length, PiiBasis.FailClosed));
    }

    /// <summary>"1 200 000 000": a one- or two-digit lead group and then groups of exactly three, separated by spaces.</summary>
    private static bool IsThousandsGrouped(string s)
    {
        var groups = s.Split(' ');
        return groups.Length >= 3 && groups[0].Length is 1 or 2 && groups.Skip(1).All(g => g.Length == 3 && g.All(char.IsAsciiDigit))
               && groups[0].All(char.IsAsciiDigit);
    }

    private static bool IsDateLike(string s) =>
        Regex.IsMatch(s, @"^\d{4}[-./]\d{1,2}[-./]\d{1,2}$|^\d{1,2}[-./]\d{1,2}[-./]\d{4}$", Opts, Timeout);

    public static bool PeselValid(string d)
    {
        if (d.Length != 11 || !d.All(char.IsAsciiDigit)) return false;
        ReadOnlySpan<int> w = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += w[i] * (d[i] - '0');
        return (10 - sum % 10) % 10 == d[10] - '0';
    }

    public static bool NipValid(string d)
    {
        if (d.Length != 10 || !d.All(char.IsAsciiDigit)) return false;
        ReadOnlySpan<int> w = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += w[i] * (d[i] - '0');
        var check = sum % 11;
        return check != 10 && check == d[9] - '0';
    }
}
