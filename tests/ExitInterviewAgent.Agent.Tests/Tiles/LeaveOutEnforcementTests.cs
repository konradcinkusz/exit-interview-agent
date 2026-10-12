using System.Text.Json;
using ExitInterviewAgent.Agent.Tests.Tiles.RedTeamFixtures;
using ExitInterviewAgent.Agent.Tiles;
using static ExitInterviewAgent.Agent.Tests.Tiles.TileTestSupport;

namespace ExitInterviewAgent.Agent.Tests.Tiles;

/// <summary>
/// The leave-out enforcement table of spec §3.3. Every code of the platform profiles' <c>leaveOut</c> vocabulary must have a row,
/// so that no code is silently unenforced. A row with a drop code is enforced by the guard, and a red-team fixture (spec §4) must
/// give that code. A row without one is enforced by the prompt, and the table says so.
/// </summary>
public class LeaveOutEnforcementTests
{
    private static readonly (string Code, string? GuardCode, string Enforcement)[] Table =
    [
        ("individuals", TileDropReason.PiiFound, "PII guard (fail closed) and the prompt"),
        ("contact", TileDropReason.PiiFound, "PII guard (fail closed) and the prompt"),
        ("employer_name", null, "the prompt and the company token; not detectable, the employer is unknown to the program"),
        ("executives", null, "the prompt only: the PII guard catches a capitalised name, not a title"),
        ("confidential", null, "the prompt and the warning; not detectable lexically"),
        ("accusations", TileDropReason.BannedTerm, "absolute banned terms and the prompt"),
        ("protected", TileDropReason.BannedTerm, "absolute banned terms (health) and the prompt; other characteristics are the prompt only"),
        ("identifying_detail", TileDropReason.IdentifyingDetail, "the identifying detail check and the prompt"),
    ];

    private static IReadOnlyList<string> LeaveOutVocabulary()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "schemas", "platform-profiles.v1.schema.json")));
        return doc.RootElement.GetProperty("$defs").GetProperty("profile").GetProperty("properties").GetProperty("leaveOut")
            .GetProperty("items").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    [Fact]
    public void Every_leave_out_code_of_the_profile_schema_has_exactly_one_row()
    {
        var codes = LeaveOutVocabulary();

        Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal), Table.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(Table.Length, Table.Select(r => r.Code).Distinct().Count());
    }

    [Fact]
    public void The_base_set_of_spec_invariant_6_is_in_the_vocabulary()
    {
        var codes = LeaveOutVocabulary();

        foreach (var required in new[] { "individuals", "employer_name", "confidential", "accusations", "protected", "contact" })
            Assert.Contains(required, codes);
    }

    public static IEnumerable<object[]> Guarded_codes() =>
        Table.Where(r => r.GuardCode is not null).Select(r => new object[] { r.Code, r.GuardCode! });

    [Theory]
    [MemberData(nameof(Guarded_codes))]
    public void A_code_the_guard_enforces_has_a_red_team_fixture_that_gives_its_drop_code(string code, string guardCode)
    {
        Assert.True(RedTeamTests.Cases.Any(c => c.ExpectedCode == guardCode), $"No red-team fixture gives {guardCode} for {code}.");
    }

    [Fact]
    public void The_prompt_only_codes_are_the_ones_without_a_drop_code()
    {
        var promptOnly = Table.Where(r => r.GuardCode is null).Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();

        Assert.Equal(["confidential", "employer_name", "executives"], promptOnly);
        Assert.NotEmpty(RedTeamTests.PromptOnly);
    }

    [Fact]
    public void Each_guard_drop_code_in_the_table_is_a_real_drop_code()
    {
        var real = new[] { TileDropReason.PiiFound, TileDropReason.BannedTerm, TileDropReason.IdentifyingDetail };

        Assert.All(Table.Where(r => r.GuardCode is not null), r => Assert.Contains(r.GuardCode!, real));
    }
}
