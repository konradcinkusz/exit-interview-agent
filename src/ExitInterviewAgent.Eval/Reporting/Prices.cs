using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExitInterviewAgent.Eval.Reporting;

public sealed class ModelPrice
{
    public double Input_Per_Mtok { get; set; }
    public double Output_Per_Mtok { get; set; }
}

/// <summary>An operator-supplied price table. The repository ships none: prices change, and a number written down once is a claim nobody re-verified.</summary>
public sealed class PriceTable
{
    public string As_Of { get; set; } = "";
    public string Source { get; set; } = "";
    public string Currency { get; set; } = "";
    public Dictionary<string, ModelPrice> Models { get; set; } = [];

    public static PriceTable Load(string path) =>
        new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build().Deserialize<PriceTable>(File.ReadAllText(path));

    public double? CostOf(string modelId, long inputTokens, long outputTokens) =>
        Models.TryGetValue(modelId, out var p) ? (inputTokens * p.Input_Per_Mtok + outputTokens * p.Output_Per_Mtok) / 1_000_000.0 : null;
}
