namespace ExitInterviewAgent.Signals.Tests.Disclosure;

/// <summary>
/// The differencing adversary (threat model T-01), at the level of the suppression decision. The adversary sees the published cut of
/// ONE snapshot, or of two snapshots that differ by one record of their own, and may subtract anything from anything. The claim:
/// every group of OTHER people whose size they can determine has 0 or at least k - 1 members (k in a single snapshot, k - 1 once
/// their own record has moved between two). The tests enumerate EVERY small partition, not a sample.
///
/// What this does not and cannot show: protection against an adversary who controls more than one record, or who knows who else
/// submitted (OPEN-PROBLEMS OP-3, docs/privacy/AGGREGATION.md section 7).
/// </summary>
public sealed class AdversaryTests
{
    /// <summary>
    /// One partition of one displayed cell: counts of the sub-cells and of the records outside all of them. The adversary's own record
    /// is in <c>OwnCell</c> (index of a sub-cell, or <c>cells.Length</c> for "outside all"), or null when it is not in this snapshot.
    /// </summary>
    private sealed record Snapshot(int[] Cells, int Unassigned, int? OwnCell)
    {
        public int Total => Cells.Sum() + Unassigned;
    }

    /// <returns>A description of the first group below the bound the adversary can determine, or null if there is none.</returns>
    private static string? FindLeak(IDisclosurePolicy policy, int k, Snapshot first, Snapshot? second)
    {
        var snapshots = second is null ? new[] { first } : [first, second];
        var width = first.Cells.Length;

        // What the adversary determines about the OTHER records: the exact count of every sub-cell published in any snapshot, and the
        // total of any snapshot that displays the employer x topic cell at all.
        var known = new int?[width];
        int? totalOthers = null;
        foreach (var s in snapshots)
        {
            var own = s.OwnCell is null ? 0 : 1;
            if (policy.IsDisplayable(s.Total, k))
            {
                totalOthers = s.Total - own;
                var plan = policy.Plan(s.Cells, s.Unassigned, k);
                for (var i = 0; i < width; i++)
                {
                    if (plan[i] == CellDisposition.Suppressed)
                    {
                        continue;
                    }
                    var others = s.Cells[i] - (s.OwnCell == i ? 1 : 0);
                    known[i] = others;
                    if (plan[i] == CellDisposition.Shown && others is > 0 and var small && small < k - (second is null ? 0 : 1))
                    {
                        return $"a shown cell of {s.Cells[i]} leaves {small} other records";
                    }
                }
            }
        }
        if (totalOthers is not { } total)
        {
            return null;
        }
        // The remainder: everything the adversary can subtract the known cells from the total to reach.
        var remainder = total - known.Where(x => x is not null).Sum(x => x!.Value);
        var bound = k - (second is null ? 0 : 1);
        return remainder > 0 && remainder < bound ? $"a remainder of {remainder} other records is recoverable by subtraction (bound {bound})" : null;
    }

    /// <summary>Every partition of up to 3 sub-cells plus the unassigned part, with counts 0..2k, and every one-record neighbour.</summary>
    private static string? FirstViolation(IDisclosurePolicy policy, int k)
    {
        var range = Enumerable.Range(0, 2 * k + 1).ToArray();
        foreach (var a in range)
            foreach (var b in range)
                foreach (var c in range)
                    foreach (var u in range)
                    {
                        int[] cells = [a, b, c];
                        var single = new Snapshot(cells, u, null);
                        if (FindLeak(policy, k, single, null) is { } leak)
                        {
                            return $"single snapshot {a},{b},{c} +{u}: {leak}";
                        }

                        // The adversary's record is one of the records of the second snapshot (added) or of the first (removed).
                        for (var own = 0; own <= cells.Length; own++)
                        {
                            var added = Add(cells, u, own, +1);
                            if (added is not null && FindLeak(policy, k, single, added with { OwnCell = own }) is { } l1)
                            {
                                return $"{a},{b},{c} +{u}, own record added to {own}: {l1}";
                            }
                            var removed = Add(cells, u, own, -1);
                            if (removed is not null && FindLeak(policy, k, single with { OwnCell = own }, removed) is { } l2)
                            {
                                return $"{a},{b},{c} +{u}, own record removed from {own}: {l2}";
                            }
                        }
                    }
        return null;
    }

    private static Snapshot? Add(int[] cells, int u, int index, int delta)
    {
        var next = (int[])cells.Clone();
        var unassigned = u;
        if (index == cells.Length)
        {
            unassigned += delta;
        }
        else
        {
            next[index] += delta;
        }
        return next.Any(n => n < 0) || unassigned < 0 ? null : new Snapshot(next, unassigned, null);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_clean_partition_rule_leaks_no_group_below_k_and_none_below_k_minus_one_across_two_snapshots(int k)
        => Assert.Null(FirstViolation(StandardDisclosurePolicy.Instance, k));

    [Fact]
    public void Mutant_without_complementary_suppression_is_caught()
        => Assert.NotNull(FirstViolation(new PerCellOnly(), 5));

    [Fact]
    public void Mutant_textbook_complementary_suppression_passes_one_snapshot_and_fails_two()
    {
        const int k = 5;
        // One snapshot only: the textbook rule holds.
        foreach (var a in Enumerable.Range(0, 11))
            foreach (var b in Enumerable.Range(0, 11))
                foreach (var c in Enumerable.Range(0, 11))
                    foreach (var u in Enumerable.Range(0, 11))
                    {
                        Assert.Null(FindLeak(new ClassicComplementary(), k, new Snapshot([a, b, c], u, null), null));
                    }
        // Two snapshots one record apart: it does not.
        var violation = FirstViolation(new ClassicComplementary(), k);
        Assert.NotNull(violation);
        Assert.Contains("own record", violation);
    }

    [Fact]
    public void Mutant_threshold_off_by_one_is_caught() => Assert.NotNull(FirstViolation(new ThresholdOffByOne(), 5));

    [Fact]
    public void Mutant_that_forgets_the_unassigned_remainder_is_caught() => Assert.NotNull(FirstViolation(new IgnoresUnassigned(), 5));

    [Fact]
    public void The_known_boundary_k_minus_one_other_records_are_recoverable_by_a_self_inserting_adversary()
    {
        // The honest limit, as a test (docs/privacy/AGGREGATION.md section 7): an employer with k - 1 rated records shows nothing;
        // after the adversary adds one record of their own the cell appears, and "everything minus my record" is exactly k - 1 people.
        const int k = 5;
        var policy = StandardDisclosurePolicy.Instance;
        Assert.False(policy.IsDisplayable(k - 1, k));
        Assert.True(policy.IsDisplayable(k, k));
        var others = k - 1;
        Assert.Equal(4, others); // a group of four, not of one: this is what "k = 5" buys against one malicious account
    }
}
