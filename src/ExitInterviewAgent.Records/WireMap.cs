using System.Collections.Frozen;

namespace ExitInterviewAgent.Records;

/// <summary>Two-way mapping between an enum (members declared in wire order, from 0) and its schema wire names.</summary>
public static class Wire
{
    private static class Map<T> where T : struct, Enum
    {
        public static string[] Names = [];
        public static FrozenDictionary<string, T> Parse = FrozenDictionary<string, T>.Empty;
    }

    static Wire()
    {
        Register<TenureBand>("lt_6m", "6m_1y", "1y_3y", "3y_5y", "5y_10y", "gt_10y");
        Register<SeniorityBand>("junior", "mid", "senior", "management");
        Register<FunctionBand>("engineering", "product_design", "sales_marketing", "operations_support", "corporate_functions", "other");
        Register<DurationBand>("lt_10m", "10m_20m", "20m_40m", "gt_40m");
        Register<TurnBand>("lt_10", "10_20", "20_40", "gt_40");
        Register<Confidence>("low", "medium", "high");
        Register<TopicStatus>("no_data", "covered");
        Register<Topic>("onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving");
    }

    private static void Register<T>(params string[] names) where T : struct, Enum
    {
        if (Enum.GetValues<T>().Length != names.Length)
            throw new InvalidOperationException($"Wire names for {typeof(T).Name} do not cover every member.");
        Map<T>.Names = names;
        Map<T>.Parse = names.Select((n, i) => (n, (T)(object)i)).ToFrozenDictionary(p => p.n, p => p.Item2);
    }

    public static string Name<T>(T value) where T : struct, Enum => Map<T>.Names[Convert.ToInt32(value)];

    public static IReadOnlyList<string> Names<T>() where T : struct, Enum => Map<T>.Names;

    public static bool TryParse<T>(string? name, out T value) where T : struct, Enum
    {
        value = default;
        return name is not null && Map<T>.Parse.TryGetValue(name, out value);
    }
}
