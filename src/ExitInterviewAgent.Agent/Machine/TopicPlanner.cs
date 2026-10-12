using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Machine;

/// <summary>How much of the follow-up policy a topic gets: core topics in full, light topics one scale check (spec §3.4).</summary>
public enum Depth { Core, Light }

/// <summary>
/// Orders the remaining topics by what the interviewee has already said about them (spec §3.3). Pure: no I/O, no model. It reads only
/// the masked reply text through the same cue fold as <see cref="ReplyAnalyzer"/>, and a sentence that
/// <see cref="ReplyAnalyzer.LooksLikeInjection"/> flags contributes nothing, so an injected instruction cannot reorder the interview.
/// Only counts and the order are kept; the reply text is never stored here.
/// </summary>
/// <remarks>
/// A topic's salience is the sum over sentences of the replies on other topics that hit its cues: 1 for a sentence, 2 when that sentence
/// also carries a practice cue (a judgement, not a mention), at most once per sentence and topic, capped at <see cref="SalienceCap"/>.
/// The cues are a floor, not a classifier: a paraphrase they do not name is not seen.
/// </remarks>
public sealed partial class TopicPlanner
{
    private const int SalienceCap = 4;
    private const int FallbackCore = 2;

    private readonly IReadOnlyList<Topic> _order;
    private readonly Dictionary<Topic, int> _weight = [];
    private readonly HashSet<Topic> _mentioned = [];

    public TopicPlanner(InterviewProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _order = protocol.Topics.Select(t => t.Topic).ToArray();
    }

    /// <summary>Adds the cue hits of one masked reply. Sentences that <see cref="ReplyAnalyzer.LooksLikeInjection"/> flags are skipped.</summary>
    /// <param name="answering">The topic this reply answers; its own cues do not count towards it. Null when the reply answers no topic.</param>
    public void Observe(string maskedReply, Topic? answering)
    {
        ArgumentNullException.ThrowIfNull(maskedReply);
        foreach (var sentence in SentenceBreak().Split(maskedReply))
        {
            if (sentence.Trim().Length == 0 || ReplyAnalyzer.LooksLikeInjection(sentence)) continue;
            var folded = ReplyAnalyzer.Fold(sentence);
            var weight = HasPracticeCue(sentence, folded) ? 2 : 1;
            foreach (var topic in _order)
            {
                if (topic == answering || !Hits(topic, sentence, folded)) continue;
                _weight[topic] = _weight.GetValueOrDefault(topic) + weight;
                _mentioned.Add(topic);
            }
        }
    }

    /// <summary>Salience per topic: cue hits outside the topic's own turns, weighted and capped at <see cref="SalienceCap"/>.</summary>
    public int Salience(Topic topic) => Math.Min(SalienceCap, _weight.GetValueOrDefault(topic));

    /// <summary>True when an earlier reply on another topic hit the topic's cues: the OPEN turn becomes a callback.</summary>
    public bool Mentioned(Topic topic) => _mentioned.Contains(topic);

    /// <summary>
    /// The plan for the remaining topics: core (at most <paramref name="maxCoreTopics"/>, salience at least 1, by salience descending then
    /// protocol order), then light (protocol order). With no remaining topic carrying any salience, the plan is protocol order with the
    /// first two remaining topics core (bounded by <paramref name="maxCoreTopics"/>), so the first topics are never thinner than today.
    /// </summary>
    public IReadOnlyList<(Topic Topic, Depth Depth)> Plan(IReadOnlySet<Topic> done, int maxCoreTopics)
    {
        ArgumentNullException.ThrowIfNull(done);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCoreTopics);
        var remaining = _order.Select((topic, index) => (Topic: topic, Index: index)).Where(t => !done.Contains(t.Topic)).ToList();

        if (remaining.All(t => Salience(t.Topic) == 0))
        {
            var fallbackCore = Math.Min(FallbackCore, maxCoreTopics);
            return remaining.Select((t, i) => (Topic: t.Topic, Depth: i < fallbackCore ? Depth.Core : Depth.Light)).ToList();
        }

        var core = remaining
            .Where(t => Salience(t.Topic) >= 1)
            .OrderByDescending(t => Salience(t.Topic))
            .ThenBy(t => t.Index)
            .Take(maxCoreTopics)
            .Select(t => t.Topic)
            .ToList();
        var light = remaining.Select(t => t.Topic).Except(core).ToList();
        return core.Select(t => (Topic: t, Depth: Depth.Core)).Concat(light.Select(t => (Topic: t, Depth: Depth.Light))).ToList();
    }

    private static bool HasPracticeCue(string sentence, string folded)
    {
        var (positive, negative) = ReplyAnalyzer.CueCounts(sentence + " " + folded);
        return positive + negative > 0;
    }

    private static bool Hits(Topic topic, string sentence, string folded)
    {
        var cue = CueFor(topic);
        return cue is not null && (cue.IsMatch(sentence) || cue.IsMatch(folded));
    }

    /// <summary>
    /// One cue per topic, PL and EN. Each Polish letter with a diacritic is written as a class with its ASCII base, so the same pattern reads
    /// the text as typed and as its fold. Stems carry a wildcard suffix for inflection. Reason for leaving has no cue: it is the last topic.
    /// </summary>
    private static Regex? CueFor(Topic topic) => topic switch
    {
        Topic.Onboarding => OnboardingCue(),
        Topic.Management => ManagementCue(),
        Topic.Growth => GrowthCue(),
        Topic.PayVsPromises => PayCue(),
        Topic.Culture => CultureCue(),
        _ => null,
    };

    [GeneratedRegex(@"\b(wdro[zż]\w*|onboard\w*|pierwsz(e|ych)\s+(dni|tygodni)\w*|szkoleni\w*|first\s+(days|weeks)\b|training\w*)", Opt, 200)]
    private static partial Regex OnboardingCue();

    [GeneratedRegex(@"\b(szef\w*|kierown\w*|prze[łl]o[żz]on\w*|manager\w*|lider\w*|team\s+lead\w*|boss\w*)", Opt, 200)]
    private static partial Regex ManagementCue();

    [GeneratedRegex(@"\b(rozw[óo]j\w*|awans\w*|szkoleni(e|a)\s+rozwojow\w*|karier\w*|nauk\w*|promot\w*|career\w*|learn\w*)", Opt, 200)]
    private static partial Regex GrowthCue();

    [GeneratedRegex(@"\b(pensj\w*|wyp[łl]at\w*|wynagrodz\w*|podwy[żz]k\w*|premi\w*|stawk\w*|obi[ęe](c|t)\w*|umow\w*|salar\w*|pay\w*|raises?\b|bonus\w*|promis\w*)", Opt, 200)]
    private static partial Regex PayCue();

    [GeneratedRegex(@"\b(atmosfer\w*|atmosphere\w*|zesp[óo][łl]\w*|w[sś]p[óo][łl]pr\w*|lud[zź]\w*|kultur\w*|culture\w*|toksycz\w*|toxic\w*|plotk\w*|colleague\w*|team(?!\s+lead)\w*)", Opt, 200)]
    private static partial Regex CultureCue();

    /// <summary>Sentence boundaries: a full stop, question or exclamation mark, semicolon or line break.</summary>
    [GeneratedRegex(@"[.!?;\r\n]+")]
    private static partial Regex SentenceBreak();

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
}
