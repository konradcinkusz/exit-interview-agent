using System.Collections.Frozen;

namespace ExitInterviewAgent.Signals;

/// <summary>The three single-band cuts. There is no cross-product: a cell is never defined by two bands (ADR-0007, ADR-0053).</summary>
public enum Dimension { Tenure = 0, Seniority = 1, Function = 2 }

/// <summary>
/// The closed vocabulary the module accepts: wire names of the record schema (ADR-0007), written out here on purpose. The module
/// does not reference the record library, so a change of the schema reaches it as a failing adapter test, not as a silent
/// re-labelling.
/// </summary>
public static class Vocabulary
{
    public const int MinRating = 1;
    public const int MaxRating = 5;

    public static readonly IReadOnlyList<string> Topics =
        ["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"];

    private static readonly string[][] BandsByDimension =
    [
        ["lt_6m", "6m_1y", "1y_3y", "3y_5y", "5y_10y", "gt_10y"],
        ["junior", "mid", "senior", "management"],
        ["engineering", "product_design", "sales_marketing", "operations_support", "corporate_functions", "other"],
    ];

    private static readonly string[] DimensionNames = ["tenure", "seniority", "function"];

    private static readonly FrozenDictionary<string, int> TopicIndex =
        Topics.Select((t, i) => (t, i)).ToFrozenDictionary(p => p.t, p => p.i);

    private static readonly FrozenDictionary<string, int>[] BandIndex =
        [.. BandsByDimension.Select(b => b.Select((n, i) => (n, i)).ToFrozenDictionary(p => p.n, p => p.i))];

    public static IReadOnlyList<string> Bands(Dimension dimension) => BandsByDimension[(int)dimension];

    public static string Name(Dimension dimension) => DimensionNames[(int)dimension];

    public static IReadOnlyList<Dimension> Dimensions { get; } = [Dimension.Tenure, Dimension.Seniority, Dimension.Function];

    internal static bool TryTopic(string? name, out int index)
    {
        index = -1;
        return name is not null && TopicIndex.TryGetValue(name, out index);
    }

    internal static bool TryBand(Dimension dimension, string? name, out int index)
    {
        index = -1;
        return name is not null && BandIndex[(int)dimension].TryGetValue(name, out index);
    }
}
