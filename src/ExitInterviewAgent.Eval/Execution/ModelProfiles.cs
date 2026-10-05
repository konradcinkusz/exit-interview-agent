using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Eval.Scenarios;
using Microsoft.Extensions.AI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExitInterviewAgent.Eval.Execution;

/// <summary>What a provider factory is given: settings from the profile and the resolved environment values. Never logged.</summary>
public sealed record ProviderSettings(string Provider, string? Model, string? Endpoint, IReadOnlyDictionary<string, string> Env);

/// <summary>
/// The registration point for real model providers. The harness never constructs a provider itself: T6's project registers a factory per
/// provider name (<c>anthropic</c>, <c>openai-compatible</c>, <c>ollama</c>) and a profile in <c>evals/profiles.yaml</c> names one.
/// A profile whose provider has no factory is reported as <c>skipped:no-provider</c>, never as a pass.
/// </summary>
public static class ProviderFactories
{
    private static readonly Dictionary<string, Func<ProviderSettings, IChatClient>> Map = new(StringComparer.Ordinal);

    public static void Register(string provider, Func<ProviderSettings, IChatClient> factory) => Map[provider] = factory;

    public static bool TryGet(string provider, out Func<ProviderSettings, IChatClient>? factory) => Map.TryGetValue(provider, out factory);

    public static IReadOnlyCollection<string> Registered => Map.Keys;
}

/// <summary>A named way to obtain an <c>IChatClient</c>, or the reason it cannot run here.</summary>
public sealed record ModelProfile(string Name, string Description, string ModelId, Func<IChatClient>? Factory, string? SkipReason)
{
    public bool Runnable => Factory is not null;

    public static ModelProfile Mock { get; } = new("mock", "Scripted offline model: templates and rules, no network, no randomness. A test seam, not a quality baseline.", ScriptedChatClient.ModelId, () => new ScriptedChatClient(), null);
}

/// <summary>Reads <c>evals/profiles.yaml</c> and resolves each entry against the registered provider factories and the environment.</summary>
public static class ProfileCatalog
{
    private sealed class FileModel
    {
        public List<Entry> Profiles { get; set; } = [];
        public Entry? Judge { get; set; }
    }

    private sealed class Entry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Provider { get; set; } = "";
        public string? Endpoint_Env { get; set; }
        public string? Model_Env { get; set; }
        public List<string> Requires_Env { get; set; } = [];
    }

    /// <summary>The built-in mock plus every profile in the file (skipped ones included, with their reason).</summary>
    public static IReadOnlyList<ModelProfile> LoadAll(string? path = null, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var profiles = new List<ModelProfile> { ModelProfile.Mock };
        var file = Read(path);
        profiles.AddRange(file.Profiles.Select(e => Resolve(e, env)));
        return profiles;
    }

    /// <summary>The judge profile, or null if the file declares none.</summary>
    public static ModelProfile? LoadJudge(string? path = null, Func<string, string?>? env = null)
    {
        var file = Read(path);
        return file.Judge is null ? null : Resolve(file.Judge, env ?? Environment.GetEnvironmentVariable);
    }

    private static FileModel Read(string? path)
    {
        path ??= RepoLayout.ProfilesPath;
        if (!File.Exists(path)) return new FileModel();
        var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        return deserializer.Deserialize<FileModel>(File.ReadAllText(path)) ?? new FileModel();
    }

    private static ModelProfile Resolve(Entry e, Func<string, string?> env)
    {
        var missing = e.Requires_Env.Where(v => string.IsNullOrWhiteSpace(env(v))).ToList();
        var model = e.Model_Env is null ? null : env(e.Model_Env);
        if (e.Model_Env is not null && string.IsNullOrWhiteSpace(model)) missing.Add(e.Model_Env);
        var shown = string.IsNullOrWhiteSpace(model) ? "(unset)" : model;
        if (missing.Count > 0)
            return new ModelProfile(e.Name, e.Description, shown, null, $"skipped:no-credential (environment not set: {string.Join(", ", missing.Distinct())})");
        if (!ProviderFactories.TryGet(e.Provider, out var factory))
            return new ModelProfile(e.Name, e.Description, shown, null, $"skipped:no-provider (no factory registered for '{e.Provider}')");
        var values = e.Requires_Env.Where(v => env(v) is not null).ToDictionary(v => v, v => env(v)!);
        var settings = new ProviderSettings(e.Provider, model, e.Endpoint_Env is null ? null : env(e.Endpoint_Env), values);
        return new ModelProfile(e.Name, e.Description, shown, () => factory!(settings), null);
    }
}
