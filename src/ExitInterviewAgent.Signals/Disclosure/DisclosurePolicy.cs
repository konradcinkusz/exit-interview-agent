namespace ExitInterviewAgent.Signals;

/// <summary>What a published cut does with one of its cells.</summary>
internal enum CellDisposition
{
    /// <summary>Published with its statistics. Always a cell of at least k records.</summary>
    Shown,

    /// <summary>The cell is empty. A zero is not a group of people, so it is a published fact, not a suppression.</summary>
    None,

    /// <summary>Withheld. Says nothing about how many records are behind it.</summary>
    Suppressed,
}

/// <summary>
/// The suppression decisions, behind a seam so the tests can swap in broken variants (the mutants of the attack suite) and show
/// that the attack tests fail on each. Production code uses <see cref="StandardDisclosurePolicy"/> only.
/// </summary>
internal interface IDisclosurePolicy
{
    /// <summary>Is a cell of <paramref name="n"/> rated responses published at all? (The employer x topic cell.)</summary>
    bool IsDisplayable(int n, int k);

    /// <summary>
    /// Decides a partition of a displayed cell: the counts of its sub-cells and the count of the records outside every sub-cell
    /// (<paramref name="unassigned"/>: an optional band that was left out). Returns one disposition per sub-cell.
    /// </summary>
    CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k);
}

/// <summary>
/// The clean-partition rule (ADR-0053). A cut of a cell is published only when EVERY sub-cell is empty or holds at least k records
/// and the records outside every sub-cell are none or at least k. Otherwise the whole cut is withheld.
///
/// This is complementary suppression taken to its limit, chosen over the textbook "hide the small cells, then hide the smallest
/// shown cell until the remainder is big enough" because that variant is only safe against one snapshot: when a single record
/// moves a hidden cell over the threshold between two batches, the old remainder minus the newly shown cell isolates a group
/// below k (ADR-0053 and <c>AdversaryTests</c> show it). With a clean partition no remainder of fewer than k records exists, in
/// any snapshot, so no subtraction, within or across two snapshots one record apart, isolates fewer than k - 1 other records.
/// </summary>
internal sealed class StandardDisclosurePolicy : IDisclosurePolicy
{
    public static readonly StandardDisclosurePolicy Instance = new();

    public bool IsDisplayable(int n, int k) => n >= k;

    public CellDisposition[] Plan(IReadOnlyList<int> cells, int unassigned, int k)
    {
        var clean = (unassigned == 0 || unassigned >= k) && cells.All(n => n == 0 || n >= k);
        return [.. cells.Select(n => !clean ? CellDisposition.Suppressed : n == 0 ? CellDisposition.None : CellDisposition.Shown)];
    }
}
