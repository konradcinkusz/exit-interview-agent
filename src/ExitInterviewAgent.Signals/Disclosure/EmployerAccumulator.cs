namespace ExitInterviewAgent.Signals;

/// <summary>
/// Folds the observations of ONE employer into counters and, at the end, applies the disclosure rules and returns the view (or
/// null when nothing about the employer may be shown). Memory is a few hundred integers whatever the number of records: nothing
/// per record is kept, which is also why no record-level data can leak from here.
/// </summary>
internal sealed class EmployerAccumulator(DisclosureRules rules, IDisclosurePolicy policy)
{
    private const int Levels = Vocabulary.MaxRating;
    private const int VerificationStates = 3;
    private static readonly int[][] DistributionBins = [[1, 2], [3], [4, 5]];
    private static readonly string[] DistributionKeys = ["low", "mid", "high"];
    private static readonly string[] VerificationKeys = ["unchecked", "unverified", "verified"];

    private readonly int[][] _respondentsByBand = [.. Vocabulary.Dimensions.Select(d => new int[Vocabulary.Bands(d).Count])];
    private readonly Cell[] _overall = [.. Vocabulary.Topics.Select(_ => new Cell())];
    private readonly Cell[][][] _bands =
        [.. Vocabulary.Topics.Select(_ => Vocabulary.Dimensions.Select(d => Vocabulary.Bands(d).Select(_ => new Cell()).ToArray()).ToArray())];

    private int _respondents;

    public int Rejected { get; private set; }

    /// <summary>Adds one observation. A value outside the closed vocabulary rejects the whole observation (counted, never described).</summary>
    public void Add(Observation observation)
    {
        if (!Vocabulary.TryBand(Dimension.Tenure, observation.TenureBand, out var tenure)
            || (observation.SeniorityBand is not null && !Vocabulary.TryBand(Dimension.Seniority, observation.SeniorityBand, out _))
            || (observation.FunctionBand is not null && !Vocabulary.TryBand(Dimension.Function, observation.FunctionBand, out _))
            || observation.Ratings.Any(r => !Vocabulary.TryTopic(r.Topic, out _) || r.Rating is < Vocabulary.MinRating or > Vocabulary.MaxRating))
        {
            Rejected++;
            return;
        }

        Vocabulary.TryBand(Dimension.Seniority, observation.SeniorityBand, out var seniority);
        Vocabulary.TryBand(Dimension.Function, observation.FunctionBand, out var function);
        int[] bandOf = [tenure, observation.SeniorityBand is null ? -1 : seniority, observation.FunctionBand is null ? -1 : function];

        _respondents++;
        for (var d = 0; d < bandOf.Length; d++)
        {
            if (bandOf[d] >= 0)
            {
                _respondentsByBand[d][bandOf[d]]++;
            }
        }

        var seen = 0;
        foreach (var rating in observation.Ratings)
        {
            Vocabulary.TryTopic(rating.Topic, out var topic);
            var bit = 1 << topic;
            if ((seen & bit) != 0)
            {
                continue; // a topic is counted once per record
            }
            seen |= bit;
            if (rating.Rating is not { } value)
            {
                continue; // covered without a number: a respondent, never a rating
            }
            _overall[topic].Add(value, observation.Verification);
            for (var d = 0; d < bandOf.Length; d++)
            {
                if (bandOf[d] >= 0)
                {
                    _bands[topic][d][bandOf[d]].Add(value, observation.Verification);
                }
            }
        }
    }

    public EmployerView? Complete(string employerRef)
    {
        var k = rules.K;
        var topics = new List<TopicView>(Vocabulary.Topics.Count);
        var anyDisplayed = false;
        for (var t = 0; t < Vocabulary.Topics.Count; t++)
        {
            var overall = _overall[t];
            if (!policy.IsDisplayable(overall.N, k))
            {
                topics.Add(new TopicView(Vocabulary.Topics[t], null, []));
                continue;
            }
            anyDisplayed = true;
            var cuts = Vocabulary.Dimensions.Select(d => BuildCut(t, d, overall)).ToArray();
            topics.Add(new TopicView(Vocabulary.Topics[t], BuildStats(overall, _respondents, withGroups: true), cuts));
        }
        return anyDisplayed ? new EmployerView(employerRef, rules.RespondentsBand(_respondents), topics) : null;
    }

    private CutView BuildCut(int topic, Dimension dimension, Cell overall)
    {
        var d = (int)dimension;
        var bands = Vocabulary.Bands(dimension);
        var cells = _bands[topic][d];
        var counts = cells.Select(c => c.N).ToArray();
        var plan = policy.Plan(counts, overall.N - counts.Sum(), rules.K);
        if (!plan.Contains(CellDisposition.Shown))
        {
            return new CutView(Vocabulary.Name(dimension), false, []);
        }
        var views = new List<BandView>(bands.Count);
        for (var b = 0; b < bands.Count; b++)
        {
            views.Add(plan[b] switch
            {
                CellDisposition.Shown => new BandView(bands[b], BandStatuses.Ok, BuildStats(cells[b], _respondentsByBand[d][b], withGroups: false)),
                CellDisposition.None => new BandView(bands[b], BandStatuses.None, null),
                _ => new BandView(bands[b], BandStatuses.Suppressed, null), // the standard policy never produces one inside a published cut
            });
        }
        return new CutView(Vocabulary.Name(dimension), true, views);
    }

    private StatsView BuildStats(Cell cell, int population, bool withGroups)
    {
        var estimate = MeanInterval.Estimate(cell.Rating);
        return new StatsView(
            estimate.N, estimate.Mean, estimate.Lower, estimate.Upper,
            rules.ReliabilityFor(estimate.N), Coverages.For(estimate.N, population),
            withGroups ? Groups(DistributionBins.Select(bin => bin.Sum(level => cell.Rating[level - 1])).ToArray(), DistributionKeys) : null,
            withGroups ? Groups(cell.Verification, VerificationKeys) : null);
    }

    /// <summary>A fixed partition of a displayed cell (rating bins, verification levels): published whole or not at all, by the same clean-partition rule.</summary>
    private IReadOnlyList<GroupView>? Groups(int[] counts, string[] keys)
    {
        var plan = policy.Plan(counts, 0, rules.K);
        return plan.Contains(CellDisposition.Shown)
            ? [.. keys.Select((key, i) => new GroupView(key, counts[i]))]
            : null;
    }

    private sealed class Cell
    {
        public int[] Rating { get; } = new int[Levels];
        public int[] Verification { get; } = new int[VerificationStates];
        public int N { get; private set; }

        public void Add(int rating, VerificationState verification)
        {
            Rating[rating - 1]++;
            Verification[(int)verification]++;
            N++;
        }
    }
}
