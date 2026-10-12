using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Agent.Tiles.Platforms;

namespace ExitInterviewAgent.Agent.Tests.Tiles.Platforms;

/// <summary>
/// Invariant 7 (platform-drafts-spec §3.2): every enabled profile has a golden rendering in <c>pl</c> and in <c>en</c>, drawn from the
/// scripted mock on the golden record (<c>full.json</c>) through the real generator and guard. The meta-test fails when a golden is
/// missing, and when a golden is left behind for a profile that is no longer enabled. A changed rendering is a changed golden: run with
/// <c>UPDATE_PLATFORM_GOLDENS=1</c>, then review every line of the diff.
/// </summary>
public class PlatformGoldenTests
{
    private const string UpdateVariable = "UPDATE_PLATFORM_GOLDENS";

    private static readonly string[] Languages = ["pl", "en"];

    public static IEnumerable<object[]> EnabledCases() =>
        PlatformProfiles.Default.Enabled.SelectMany(p => Languages.Select(l => new object[] { p.Id, l }));

    private static string GoldenDir() =>
        Path.Combine(TileTestSupport.RepoRoot(), "tests", "ExitInterviewAgent.Agent.Tests", "Tiles", "Platforms", "Golden");

    /// <summary>
    /// The golden text of one draft. Fixed lines, one per field, a section per line with its key, its label and its text, and the copy
    /// text last. Every value is written as it is; the texts carry no line breaks, so each section stays on one line.
    /// </summary>
    internal static string Render(PlatformDraft draft)
    {
        var text = new StringBuilder();
        text.Append("platform: ").Append(draft.Id).Append('\n');
        text.Append("name: ").Append(draft.Name).Append('\n');
        text.Append("family: ").Append(JsonNamingPolicy.SnakeCaseLower.ConvertName(draft.Family.ToString())).Append('\n');
        text.Append("risk: ").Append(JsonNamingPolicy.SnakeCaseLower.ConvertName(draft.Risk.ToString())).Append('\n');
        text.Append("url: ").Append(draft.Url).Append('\n');
        text.Append("warning: ").Append(draft.Warning).Append('\n');
        foreach (var field in draft.FieldsOnSite) text.Append("field: ").Append(field).Append('\n');
        foreach (var section in draft.Sections)
            text.Append("section ").Append(section.Key).Append(" | ").Append(section.Label).Append(" | ").Append(section.Text).Append('\n');
        text.Append("copy:\n").Append(draft.CopyText).Append('\n');
        return text.ToString();
    }

    /// <summary>The tile set the scripted mock gives for the golden record in one language, through the real generator and guard.</summary>
    private static async Task<TileSet> ScriptedSetAsync(string language)
    {
        var record = TileTestSupport.WithLanguage(TileTestSupport.Full(), language);
        var generator = new TileGenerator(new ModelTileWriter(new ScriptedChatClient()), new TileGuard(new PiiGuard()));
        return await generator.GenerateAsync(new TileInput(record), CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(EnabledCases))]
    public async Task The_scripted_rendering_matches_its_golden_file(string id, string language)
    {
        var drafts = PlatformDraftBuilder.Build(await ScriptedSetAsync(language), PlatformProfiles.Default, language);
        var draft = Assert.Single(drafts, d => d.Id == id);
        var actual = Render(draft);
        var path = Path.Combine(GoldenDir(), $"{id}.{language}.txt");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(GoldenDir());
            File.WriteAllText(path, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        Assert.True(File.Exists(path), $"Missing golden {Path.GetFileName(path)}; run with {UpdateVariable}=1 and review the file.");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    [Fact]
    public void Every_enabled_profile_has_a_golden_in_pl_and_en_and_no_other_golden_exists()
    {
        var expected = PlatformProfiles.Default.Enabled
            .SelectMany(p => Languages.Select(l => $"{p.Id}.{l}.txt"))
            .Order(StringComparer.Ordinal)
            .ToList();
        var actual = Directory.GetFiles(GoldenDir(), "*.txt")
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, actual);
    }
}
