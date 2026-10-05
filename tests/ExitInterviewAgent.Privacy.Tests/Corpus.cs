using System.Text;
using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Privacy.Tests;

internal sealed record Gold(string Kind, int Start, int Length)
{
    public int End => Start + Length;
}

internal sealed record Sample(int Line, string Text, IReadOnlyList<Gold> Gold);

/// <summary>
/// Reads the synthetic corpora. Markup <c>{{kind:text}}</c> marks a gold PII span; everything else is plain text.
/// Lines starting with '#' and blank lines are ignored. Offsets refer to the text with the markup removed.
/// </summary>
internal static class Corpus
{
    private static readonly Regex Markup = new(@"\{\{(\w+):(.*?)\}\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<Sample> Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", name);
        var samples = new List<Sample>();
        var n = 0;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            n++;
            if (raw.Length == 0 || raw[0] == '#') continue;
            samples.Add(Parse(n, raw));
        }

        return samples;
    }

    public static Sample Parse(int line, string raw)
    {
        var sb = new StringBuilder();
        var gold = new List<Gold>();
        var pos = 0;
        foreach (Match m in Markup.Matches(raw))
        {
            sb.Append(raw, pos, m.Index - pos);
            gold.Add(new Gold(m.Groups[1].Value, sb.Length, m.Groups[2].Length));
            sb.Append(m.Groups[2].Value);
            pos = m.Index + m.Length;
        }

        sb.Append(raw, pos, raw.Length - pos);
        return new Sample(line, sb.ToString(), gold);
    }
}
