using System.Text.Json;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;

namespace ExitInterviewAgent.Agent.Tests.Roles;

/// <summary>
/// The guard corpus (interview-dynamics spec §6.1 Q-10): labelled Polish and English turns, each with the move it answers. A good turn must
/// pass; a bad turn must be rejected with exactly the reason it names. Good turns may fail at most 5% (the spec's allowance); bad turns may
/// not pass at all. The corpus is the spec's test data: change a row only with the reason in the PR.
/// </summary>
public class GuardCorpusTests
{
    private static readonly PiiGuard Pii = new(["Widgetron"]);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Moves =
        ["warmup", "open", "example", "elaborate", "ladder", "consequence", "scale", "scaleFollowUp", "retention", "anythingElse", "clarify", "redirect", "deep"];

    private sealed record Turn(string Id, string Move, string Text, string Label, string Expect, string? Said);

    private static IReadOnlyList<Turn> Load(string file) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", file))
            .Where(line => line.Trim().Length > 0)
            .Select(line => JsonSerializer.Deserialize<Turn>(line, Json) ?? throw new InvalidDataException($"Bad corpus row: {line}"))
            .ToArray();

    /// <summary>The turn kind and shape the machine will use for each move (spec §3.2); WP5 wires the same mapping into the runner.</summary>
    private static (TurnKind Kind, QuestionShape Shape) Shape(string move) => move switch
    {
        "warmup" or "open" or "retention" or "anythingElse" => (TurnKind.Topic, QuestionShape.Default),
        "scale" => (TurnKind.Topic, QuestionShape.Scale),
        "example" => (TurnKind.Probe, QuestionShape.Default),
        "elaborate" or "ladder" or "consequence" or "scaleFollowUp" => (TurnKind.Probe, QuestionShape.Follow),
        "clarify" => (TurnKind.Clarification, QuestionShape.Default),
        "redirect" => (TurnKind.Redirect, QuestionShape.Default),
        "deep" => (TurnKind.DeepProbe, QuestionShape.Default),
        _ => throw new InvalidDataException($"Unknown move '{move}'."),
    };

    private static string Verdict(Turn turn)
    {
        var (kind, shape) = Shape(turn.Move);
        return QuestionGuard.Check(turn.Text, kind, Pii, shape, turn.Said).Reason;
    }

    private static readonly Lazy<IReadOnlyList<Turn>> Polish = new(() => Load("turns.pl.jsonl"));
    private static readonly Lazy<IReadOnlyList<Turn>> English = new(() => Load("turns.en.jsonl"));

    [Fact]
    public void The_polish_corpus_has_at_least_sixty_good_and_forty_bad_turns_and_covers_every_move()
    {
        var pl = Polish.Value;

        Assert.True(pl.Count(t => t.Label == "good") >= 60, "fewer than 60 good Polish turns");
        Assert.True(pl.Count(t => t.Label == "bad") >= 40, "fewer than 40 bad Polish turns");
        Assert.Equal(Moves.Order(), pl.Where(t => t.Label == "good").Select(t => t.Move).Distinct().Order());
    }

    [Fact]
    public void The_english_corpus_has_at_least_twenty_good_turns_and_covers_every_move()
    {
        var en = English.Value;

        Assert.True(en.Count(t => t.Label == "good") >= 20, "fewer than 20 good English turns");
        Assert.Equal(Moves.Order(), en.Where(t => t.Label == "good").Select(t => t.Move).Distinct().Order());
    }

    [Fact]
    public void Every_row_has_a_unique_id_a_known_move_and_a_consistent_label_and_expectation()
    {
        foreach (var turn in Polish.Value.Concat(English.Value))
        {
            Assert.Contains(turn.Move, Moves);
            Assert.Contains(turn.Label, new[] { "good", "bad" });
            Assert.False(turn.Label == "good" && turn.Expect != "ok", $"{turn.Id}: a good turn expects ok");
            Assert.False(turn.Label == "bad" && turn.Expect == "ok", $"{turn.Id}: a bad turn must name its reason");
        }
        var ids = Polish.Value.Concat(English.Value).Select(t => t.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Polish_good_turns_are_rejected_at_most_five_per_cent_of_the_time()
    {
        var good = Polish.Value.Where(t => t.Label == "good").ToArray();
        var rejected = good.Where(t => Verdict(t) != "ok").Select(t => $"{t.Id}: {Verdict(t)}").ToArray();

        Assert.True(rejected.Length * 20 <= good.Length, $"false rejects {rejected.Length}/{good.Length}: {string.Join("; ", rejected)}");
    }

    [Fact]
    public void Every_bad_polish_turn_is_caught_with_its_named_reason()
    {
        var misses = Polish.Value.Where(t => t.Label == "bad").Select(t => (t.Id, Expected: t.Expect, Actual: Verdict(t))).Where(m => m.Actual != m.Expected).ToArray();

        Assert.Empty(misses);
    }

    [Fact]
    public void Every_english_turn_gets_its_verdict()
    {
        var misses = English.Value.Select(t => (t.Id, Expected: t.Label == "good" ? "ok" : t.Expect, Actual: Verdict(t))).Where(m => m.Actual != m.Expected).ToArray();

        Assert.Empty(misses);
    }
}
