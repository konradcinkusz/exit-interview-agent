using System.Text;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Personas;

/// <summary>
/// Plays a <see cref="PersonaDefinition"/> as the interviewee. Deterministic: the reply to the n-th turn of a kind is a
/// pure function of (persona, seed, kind, topic, n), so the same seed gives the same interview byte for byte.
/// It reads only the turn's kind and topic (the cue), never the interviewer's wording, so no interviewer text can steer it.
/// </summary>
public sealed class PersonaInterviewee : IInterviewee
{
    private readonly PersonaDefinition _persona;
    private readonly ulong _seed;
    private readonly SimulatedClock? _clock;
    private readonly Func<string, string>? _decorate;
    private readonly Dictionary<string, int> _occurrences = [];

    /// <param name="clock">Advanced by a simulated think and typing time per reply, so durations are reproducible.</param>
    /// <param name="decorate">Applied to every reply; the canary test uses it to plant a marker in all interviewee text.</param>
    public PersonaInterviewee(PersonaDefinition persona, int seed, SimulatedClock? clock = null, Func<string, string>? decorate = null)
    {
        _persona = persona;
        _seed = unchecked((ulong)seed);
        _clock = clock;
        _decorate = decorate;
    }

    public PersonaDefinition Persona => _persona;

    public Task<string?> ReplyAsync(IntervieweeTurn turn, CancellationToken ct)
    {
        var reply = Pick(turn);
        var words = ReplyAnalyzer.CountWords(reply);
        _clock?.Advance(TimeSpan.FromSeconds(_persona.Typing.ThinkSeconds) + TimeSpan.FromSeconds(words * 60.0 / _persona.Typing.WordsPerMinute));
        return Task.FromResult<string?>(_decorate is null ? reply : _decorate(reply));
    }

    public Task DeliverAsync(IntervieweeTurn turn, CancellationToken ct) => Task.CompletedTask;

    private string Pick(IntervieweeTurn turn)
    {
        var r = _persona.Responses;
        var topic = turn.Topic is { } t ? Wire.Name(t) : null;
        var (slot, alternatives) = turn.Kind switch
        {
            TurnKind.Opening => ("consent", r.Consent),
            TurnKind.ConsentReask => ("consent-reask", r.ConsentReask ?? r.Consent),
            TurnKind.Topic when topic is not null => ($"topic:{topic}", r.Topics[topic]),
            TurnKind.Probe => ($"probe:{topic}", ByTopic(r.Probes, topic)),
            TurnKind.Clarification => ($"clarification:{topic}", ByTopic(r.Clarifications, topic)),
            TurnKind.Redirect => ($"redirect:{topic}", ByTopic(r.Redirects, topic)),
            _ => ("fallback", r.Fallback),
        };
        alternatives ??= r.Fallback;
        _occurrences[slot] = _occurrences.GetValueOrDefault(slot) + 1;
        var index = (int)((SplitMix64(_seed ^ Fnv(_persona.Id + "|" + slot)) + (ulong)_occurrences[slot] - 1) % (ulong)alternatives.Count);
        return alternatives[index];
    }

    private static IReadOnlyList<string>? ByTopic(IReadOnlyDictionary<string, IReadOnlyList<string>>? map, string? topic) =>
        map is null ? null : topic is not null && map.TryGetValue(topic, out var specific) ? specific : map.GetValueOrDefault("*");

    /// <summary>SplitMix64 and FNV-1a: fixed algorithms, so a seed means the same thing on every platform and runtime version.</summary>
    private static ulong SplitMix64(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    private static ulong Fnv(string s)
    {
        unchecked
        {
            var h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(s)) h = (h ^ b) * 1099511628211UL;
            return h;
        }
    }
}
