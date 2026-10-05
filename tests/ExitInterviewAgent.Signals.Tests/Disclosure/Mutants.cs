namespace ExitInterviewAgent.Signals.Tests.Disclosure;

// Deliberately broken disclosure policies. Each one is a plausible mistake, and the attack suite must catch every one of them: a
// suite that passes on a broken policy proves nothing (testing-strategy: a test that cannot fail is worse than none).

/// <summary>Per-cell threshold only: no complementary suppression at all.</summary>
internal sealed class PerCellOnly : IDisclosurePolicy
{
    public bool IsDisplayable(int n, int k) => n >= k;

    public CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k)
        => [.. cells.Select(n => n == 0 ? CellDisposition.None : n >= k ? CellDisposition.Shown : CellDisposition.Suppressed)];
}

/// <summary>
/// The textbook complementary suppression: hide the small cells, then hide the smallest shown cell while the remainder is still a
/// group of fewer than k. Safe against one snapshot; NOT safe against two snapshots one record apart (ADR-0053).
/// </summary>
internal sealed class ClassicComplementary : IDisclosurePolicy
{
    public bool IsDisplayable(int n, int k) => n >= k;

    public CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k)
    {
        var plan = cells.Select(n => n == 0 ? CellDisposition.None : n >= k ? CellDisposition.Shown : CellDisposition.Suppressed).ToArray();
        while (true)
        {
            var hidden = cells.Where((_, i) => plan[i] == CellDisposition.Suppressed).Sum() + unassigned;
            var shown = Enumerable.Range(0, cells.Count).Where(i => plan[i] == CellDisposition.Shown).ToArray();
            if (hidden is 0 || hidden >= k || shown.Length == 0)
            {
                return plan;
            }
            plan[shown.MinBy(i => cells[i])] = CellDisposition.Suppressed;
        }
    }
}

/// <summary>The clean-partition rule with the threshold off by one.</summary>
internal sealed class ThresholdOffByOne : IDisclosurePolicy
{
    public bool IsDisplayable(int n, int k) => n >= k - 1;

    public CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k)
        => StandardDisclosurePolicy.Instance.Plan(cells, unassigned, k - 1);
}

/// <summary>The clean-partition rule that forgets the records outside every sub-cell (an optional band left out).</summary>
internal sealed class IgnoresUnassigned : IDisclosurePolicy
{
    public bool IsDisplayable(int n, int k) => n >= k;

    public CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k)
        => StandardDisclosurePolicy.Instance.Plan(cells, 0, k);
}

/// <summary>The employer x topic cell displayed from k - 1 records.</summary>
internal sealed class DisplaysBelowK : IDisclosurePolicy
{
    public bool IsDisplayable(int n, int k) => n >= k - 1;

    public CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k) => StandardDisclosurePolicy.Instance.Plan(cells, unassigned, k);
}
