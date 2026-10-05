using System.Text;

namespace ExitInterviewAgent.Records;

/// <summary>A quote that failed fidelity: where it is, never what it says.</summary>
public sealed record QuoteMismatch(Topic Topic, int QuoteIndex);

public sealed record QuoteVerification(IReadOnlyList<QuoteMismatch> Mismatches, int QuotesChecked)
{
    public bool AllVerbatim => Mismatches.Count == 0;
}

/// <summary>
/// Transcript fidelity: every quote must be a verbatim substring of the transcript after whitespace
/// normalization. Verify against the same PII-masked transcript the extractor saw, since quotes are taken from it.
/// </summary>
public static class QuoteVerifier
{
    /// <summary>
    /// Normalization, applied identically to transcript and quote: every maximal run of Unicode white space
    /// (char.IsWhiteSpace: spaces, tabs, line breaks, no-break space and so on) becomes one U+0020, then leading and
    /// trailing space is trimmed. Nothing else changes: case, punctuation, diacritics and Unicode composition
    /// are compared ordinally, so a quote that merely "looks" the same but differs in any other way fails.
    /// A quote that normalizes to the empty string never verifies.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static QuoteVerification VerifyQuotes(string transcript, InterviewRecord record)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(record);
        var haystack = Normalize(transcript);
        var mismatches = new List<QuoteMismatch>();
        var checkedCount = 0;
        foreach (var (topic, entry) in record.Topics.Enumerate())
            for (var i = 0; i < entry.Quotes.Count; i++)
            {
                checkedCount++;
                var needle = Normalize(entry.Quotes[i]);
                if (needle.Length == 0 || !haystack.Contains(needle, StringComparison.Ordinal))
                    mismatches.Add(new QuoteMismatch(topic, i));
            }
        return new QuoteVerification(mismatches, checkedCount);
    }
}
