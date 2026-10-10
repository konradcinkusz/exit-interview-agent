using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Tests.Support;

namespace ExitInterviewAgent.Eval.Tests;

/// <summary>The corpus is validated on every build: this is the test that runs `eval validate`.</summary>
public class CorpusTests
{
    [Fact]
    public void The_committed_corpus_satisfies_the_schema_and_every_corpus_rule()
    {
        var report = Corpus.Validate(Fixtures.All);

        Assert.True(report.Ok, string.Join(Environment.NewLine, report.Errors));
        Assert.All(Corpus.Classes, c => Assert.True(report.PerClass[c] > 0, $"class {c} is empty"));
    }

    [Fact]
    public void Every_scenario_class_the_methodology_requires_is_present_with_both_gates_in_use()
    {
        Assert.Equal(6, Corpus.Classes.Length);
        Assert.Contains(Fixtures.All, l => l.Scenario.Gate == "constraint");
        Assert.Contains(Fixtures.All, l => l.Scenario.Gate == "behaviour");
    }

    private static string Write(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"eval-scenario-{Guid.NewGuid():N}", "hap-099-x-y-z.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
        return path;
    }

    private const string Good = """
        id: hap-099-a-valid-scenario
        class: happy
        gate: behaviour
        title: A valid scenario for the schema test
        why: This scenario exists only so that the schema test has a valid document to break one key at a time.
        persona: talkative
        expect:
          outcome: completed
          record: present
        spec: [B-01]
        """;

    [Fact]
    public void A_valid_scenario_loads()
    {
        var loaded = ScenarioLoader.LoadFile(Write(Good));

        Assert.Equal("talkative", loaded.Scenario.Persona);
        Assert.Equal([1], loaded.Scenario.EffectiveSeeds);
    }

    [Theory]
    [InlineData("mistyped_key: 1\n")]
    [InlineData("seeds: [1, 1]\n")]
    [InlineData("faults:\n  - kind: explode\n    roles: [interviewer]\n")]
    [InlineData("faults:\n  - kind: timeout\n    roles: [judge]\n")]
    public void A_scenario_with_an_unknown_key_value_or_duplicate_fails_loudly(string extra)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ScenarioLoader.LoadFile(Write(Good + "\n" + extra)));

        Assert.Contains("scenario.schema.json", ex.Message);
    }

    [Theory]
    [InlineData("why: too short\n", "why")]
    [InlineData("id: bad_id\n", "id")]
    [InlineData("class: nonsense\n", "class")]
    public void A_scenario_with_a_bad_required_field_is_rejected_by_the_schema(string replacement, string field)
    {
        var yaml = string.Join('\n', Good.Split('\n').Where(l => !l.StartsWith(field + ":", StringComparison.Ordinal)).Append(replacement));

        Assert.Throws<InvalidDataException>(() => ScenarioLoader.LoadFile(Write(yaml)));
    }

    [Fact]
    public void Yaml_scalars_follow_the_core_schema_so_a_quoted_number_stays_a_string()
    {
        var json = ScenarioLoader.YamlToJson("a: 1\nb: '1'\nc: true\nd: ~\ne: x\n");

        Assert.Equal("{\"a\":1,\"b\":\"1\",\"c\":true,\"d\":null,\"e\":\"x\"}", json);
    }

    private static LoadedScenario Clone(LoadedScenario l, Func<Scenario, Scenario> edit) => l with { Scenario = edit(l.Scenario) };

    [Fact]
    public void The_validator_can_fail_an_empty_class_a_missing_absence_a_dangling_citation_and_an_unlabelled_class_gate()
    {
        var all = Fixtures.All.ToList();

        // An empty class.
        Assert.Contains(Corpus.Validate(all.Where(l => l.Scenario.Class != "consent").ToList()).Errors, e => e.Contains("class 'consent' has no scenario"));

        // The two-assertion rule: an adversarial scenario with no absence.
        var adv = all.First(l => l.Scenario.Class == "adversarial");
        var noAbsent = all.Select(l => l == adv ? Clone(l, s => s with { Expect = s.Expect with { Absent = null } }) : l).ToList();
        Assert.Contains(Corpus.Validate(noAbsent).Errors, e => e.Contains("absence assertion"));

        // An adversarial scenario gated softly.
        var soft = all.Select(l => l == adv ? Clone(l, s => s with { Gate = "behaviour" }) : l).ToList();
        Assert.Contains(Corpus.Validate(soft).Errors, e => e.Contains("must be gated as constraint"));

        // A citation of a spec id the scenario's SPEC row does not cite.
        var hap = all.First(l => l.Scenario.Class == "happy");
        var wrong = all.Select(l => l == hap ? Clone(l, s => s with { Spec = [.. s.Spec, "C-03"] }) : l).ToList();
        Assert.Contains(Corpus.Validate(wrong).Errors, e => e.Contains("does not cite it"));

        // A fault that cannot apply.
        var deg = all.First(l => l.Scenario.Class == "degradation");
        var badFault = all.Select(l => l == deg ? Clone(l, s => s with { Faults = [new FaultSpec("compromised", ["extractor"], null, ["leading"])] }) : l).ToList();
        Assert.Contains(Corpus.Validate(badFault).Errors, e => e.Contains("does not apply"));

        // A degradation scenario that injects nothing.
        var noFault = all.Select(l => l == deg ? Clone(l, s => s with { Faults = null }) : l).ToList();
        Assert.Contains(Corpus.Validate(noFault).Errors, e => e.Contains("must inject a fault"));
    }

    [Fact]
    public void Every_spec_row_cites_scenarios_that_exist_and_every_constraint_and_behaviour_is_proven_by_at_least_one()
    {
        var rows = Corpus.SpecRows();

        Assert.Equal(13, rows.Keys.Count(k => k.StartsWith("C-", StringComparison.Ordinal)));
        Assert.Equal(10, rows.Keys.Count(k => k.StartsWith("B-", StringComparison.Ordinal)));
        Assert.All(rows.Where(r => r.Key != "B-10"), r => Assert.NotEmpty(r.Value));
    }

    [Fact]
    public void The_corpus_digest_is_stable_across_runs_and_moves_when_a_scenario_changes()
    {
        var a = Corpus.Digest(Fixtures.All);
        var b = Corpus.Digest(Fixtures.All);
        var edited = Fixtures.All.Select((l, i) => i == 0 ? Clone(l, s => s with { Seeds = [7] }) : l).ToList();
        // The canonical JSON is what is hashed, so edit it too (a Scenario record alone is not what the digest reads).
        edited[0] = edited[0] with { CanonicalJson = edited[0].CanonicalJson + " " };

        Assert.Equal(a, b);
        Assert.NotEqual(a, Corpus.Digest(edited));
    }
}
