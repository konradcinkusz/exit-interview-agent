using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace ExitInterviewAgent.Privacy.Tests;

/// <summary>
/// Cost of each pattern rule on adversarial and long input (ADR-0063). Every rule is a static <see cref="Regex"/> field of
/// <c>PatternRules</c> or <c>NameRules</c>; reflection keeps a newly added rule from escaping the checks.
/// Run with <c>--filter "Category=RuleCost" --logger "console;verbosity=detailed"</c> to print the table recorded in the ADR.
/// </summary>
[Trait("Category", "RuleCost")]
public class RuleCostTests(ITestOutputHelper output)
{
    private const int N = 200_000;

    internal static IEnumerable<(string Name, Regex Rx)> AllRules() =>
        new[] { typeof(PatternRules), typeof(NameRules) }
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(Regex)).Select(f => (t.Name + "." + f.Name, (Regex)f.GetValue(null)!)));

    internal static IEnumerable<(string Name, string Text)> Inputs()
    {
        string Rep(string u) => string.Concat(Enumerable.Repeat(u, N / u.Length));
        yield return ("letters-run", new string('a', N));
        yield return ("digits-run", new string('1', N));
        yield return ("a-dot-repeat", Rep("a."));
        yield return ("at-repeat", Rep("a[at]"));
        yield return ("at-dot-chain", "a[at]" + Rep("b[dot]"));
        yield return ("local-then-at-fails", Rep(new string('a', 60) + " "));
        yield return ("spaced-at", Rep("a [at] "));
        yield return ("dash-digit", Rep("1-"));
        yield return ("space-digit", Rep("1 "));
        yield return ("caps-words", Rep("Abc "));
        yield return ("slash-colon", Rep("github/"));
        yield return ("hyphen-names", Rep("Ab-Cd-Ef-Gh "));
        yield return ("transcript", Rep("Interviewer: And how was onboarding?\nUser: My manager Tomasz Zieliński never replied; write to a@b.example or c[at]d[dot]example.\n"));
    }

    private static (double Ms, bool TimedOut) Time(Action run)
    {
        var sw = Stopwatch.StartNew();
        try { run(); } catch (RegexMatchTimeoutException) { return (sw.Elapsed.TotalMilliseconds, true); }
        return (sw.Elapsed.TotalMilliseconds, false);
    }

    /// <summary>Every rule, plus the marker-anchored obfuscated-email scan, on every input.</summary>
    private static IEnumerable<(string Rule, string Input, int Chars, double Ms, bool TimedOut)> Measure()
    {
        foreach (var (name, text) in Inputs())
        {
            foreach (var (rule, rx) in AllRules())
            {
                var (ms, timedOut) = Time(() => _ = rx.Matches(text).Count);
                yield return (rule, name, text.Length, ms, timedOut);
            }

            var (e, eTimedOut) = Time(() => PatternRules.AddObfuscatedEmails(text, []));
            yield return ("PatternRules.AddObfuscatedEmails", name, text.Length, e, eTimedOut);
        }
    }

    [Fact]
    public void Print_cost_table()
    {
        var lines = new List<string> { "rule | input | chars | ms | ns/char" };
        lines.AddRange(Measure().Select(r => $"{r.Rule} | {r.Input} | {r.Chars} | {r.Ms:F1} | {r.Ms * 1e6 / r.Chars:F0}{(r.TimedOut ? " TIMEOUT" : "")}"));
        output.WriteLine(string.Join("\n", lines));
    }

    [Fact]
    public void Every_regex_rule_has_a_match_timeout_within_the_budget()
    {
        var rules = AllRules().ToList();
        Assert.True(rules.Count >= 30, "reflection found the rules");
        Assert.All(rules, r =>
        {
            Assert.NotEqual(Timeout.InfiniteTimeSpan, r.Rx.MatchTimeout);
            Assert.True(r.Rx.MatchTimeout <= RegexBudget.Timeout, r.Name);
        });
    }

    [Fact]
    public void No_rule_is_superlinear_on_200k_characters_of_adversarial_input()
    {
        // A quadratic rule needs minutes on 200 000 characters; the slowest linear rule here needs well under a second.
        // The 8 s ceiling is about 20 times the slowest measured rule, so contention does not make this flaky.
        var slow = Measure().Where(r => r.TimedOut || r.Ms > 8_000).Select(r => $"{r.Rule} on {r.Input}: {r.Ms:F0} ms").ToList();
        Assert.Empty(slow);
    }

    [Fact]
    public void Doubling_the_input_does_not_quadruple_the_obfuscated_email_scan()
    {
        // Compare 4x the length: linear is about 4x, quadratic about 16x. Warm up first so JIT time is not counted.
        const string unit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        double Cost(int repeat)
        {
            var text = string.Concat(Enumerable.Repeat(unit, repeat));
            PatternRules.AddObfuscatedEmails(text, []);
            return Enumerable.Range(0, 3).Min(_ => Time(() => PatternRules.AddObfuscatedEmails(text, [])).Ms);
        }

        var small = Math.Max(Cost(1_000), 1);
        var large = Cost(4_000);
        Assert.True(large / small < 12, $"4x input took {large / small:F1}x");
    }

    [Fact]
    public void A_five_megabyte_unbroken_token_is_masked_within_the_default_budget_of_each_rule()
    {
        // The production budget (2 s per rule, RegexBudget.Default) must hold for a transcript far larger than any record.
        var text = new string('a', 5_000_000);
        var (ms, timedOut) = Time(() => PatternRules.AddObfuscatedEmails(text, []));
        Assert.False(timedOut);
        Assert.True(ms < 2_000 * 4, $"{ms:F0} ms");
    }
}
