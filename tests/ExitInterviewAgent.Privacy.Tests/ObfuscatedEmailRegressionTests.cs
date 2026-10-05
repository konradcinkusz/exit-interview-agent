using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Privacy.Tests;

/// <summary>
/// ADR-0063: the marker-anchored obfuscated-email rule must detect everything the single-regex rule it replaced detected.
/// The old regex is kept here as the oracle; the corpus is the named regression set, the fuzz is a seeded random cross-check.
/// </summary>
public class ObfuscatedEmailRegressionTests
{
    private static readonly Regex Reference = new(
        @"[\p{L}\p{N}][\p{L}\p{N}._%+\-']{0,63}\s?(?:\[at\]|\(at\)|\{at\})\s?[\p{L}\p{N}\-]{1,63}(?:\s?(?:\[dot\]|\(dot\)|\.)\s?[\p{L}\p{N}\-]{1,63}){1,6}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(20));

    private static List<(int, int)> Old(string text) => Reference.Matches(text).Select(m => (m.Index, m.Length)).ToList();

    private static List<(int, int)> New(string text)
    {
        var found = new List<PiiFinding>();
        PatternRules.AddObfuscatedEmails(text, found);
        return found.Select(f => (f.Start, f.Length)).ToList();
    }

    public static IEnumerable<object[]> CorpusLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Corpus", "email-obfuscated.txt"))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(CorpusLines))]
    public void Every_corpus_sample_is_still_detected_exactly_as_before(string line)
    {
        Assert.NotEmpty(Old(line)); // the corpus only holds samples the old rule detected
        Assert.Equal(Old(line), New(line));
        Assert.DoesNotContain("[at]", new PiiDetector().Mask(line).MaskedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_corpus_is_not_empty()
        => Assert.True(CorpusLines().Count() >= 30);

    [Fact]
    public void Seeded_random_text_gives_the_same_spans_as_the_reference_rule()
    {
        string[] pieces =
        [
            "a", "B", "9", "x.y", "-", "_", "'", "+", "%", " ", " ", "\t", "\n", "[at]", "(AT)", "{at}", "[dot]", "(dot)", ".", ",", "@", "é", "ß", "²", "\u00a0",
            "\u2003", "\u0085", "name", "host", "[", "]", "(", ")", "com",
            new string('q', 70), new string('w', 64), new string('e', 63),
        ];
        var rnd = new Random(20261005);
        for (var i = 0; i < 20_000; i++)
        {
            var text = string.Concat(Enumerable.Range(0, rnd.Next(1, 24)).Select(_ => pieces[rnd.Next(pieces.Length)]));
            var expected = Old(text);
            var actual = New(text);
            Assert.True(expected.SequenceEqual(actual), $"text #{i} {Escape(text)}: old [{string.Join(",", expected)}] new [{string.Join(",", actual)}]");
        }
    }

    private static string Escape(string s) => string.Concat(s.Select(c => c < 32 || c > 126 ? $"\\u{(int)c:x4}" : c.ToString()));
}
