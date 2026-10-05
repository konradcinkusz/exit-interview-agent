namespace ExitInterviewAgent.Signals;

/// <summary>
/// The parameters of the disclosure-control rules. <see cref="Version"/> names the rule set: it changes whenever the rules, the
/// statistics or the shape of the published numbers change, and it is stored with every snapshot and returned by the API.
/// </summary>
public sealed record DisclosureRules(int MinimumGroupSize)
{
    /// <summary>Rule set 1: clean partitions (ADR-0053), regularised t interval (ADR-0054), fixed three-bin distribution, no composite.</summary>
    public const string Version = "1";

    /// <summary>
    /// The smallest k that means anything. The cross-snapshot guarantee is k - 1 records of "others" (ADR-0053); k = 2 would
    /// promise one, which is no group at all. Configuration below this stops the service at startup.
    /// </summary>
    public const int FloorForMinimumGroupSize = 3;

    public const int DefaultMinimumGroupSize = 5;

    public int K => MinimumGroupSize;

    /// <summary>Reliability follows n relative to k, so a stricter k never reads as "good" earlier.</summary>
    public string ReliabilityFor(int n) => n < 2 * K ? Reliabilities.Low : n < 6 * K ? Reliabilities.Moderate : Reliabilities.High;

    /// <summary>The employer's respondent count, shown only as a band whose lower edge is at least k.</summary>
    public string RespondentsBand(int respondents)
    {
        var edges = new[] { K, 2 * K, 5 * K, 10 * K };
        for (var i = edges.Length - 1; i >= 0; i--)
        {
            if (respondents >= edges[i])
            {
                return i == edges.Length - 1 ? $"{edges[i]}+" : $"{edges[i]}-{edges[i + 1] - 1}";
            }
        }
        throw new ArgumentOutOfRangeException(nameof(respondents), "A respondent band is only defined at or above k.");
    }
}

/// <summary>Stable vocabularies of the published numbers.</summary>
public static class Reliabilities
{
    public const string Low = "low";
    public const string Moderate = "moderate";
    public const string High = "high";
}

public static class Coverages
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    /// <summary>Share of the population (employer or band) that gave a rating for the topic, banded: 75% or more, 40-74%, below 40%.</summary>
    public static string For(int rated, int population)
    {
        if (population <= 0)
        {
            return Low;
        }
        var share = (double)rated / population;
        return share >= 0.75 ? High : share >= 0.40 ? Medium : Low;
    }
}
