namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// The only timestamp granularity the store keeps for records and ledger entries: the Monday of the ISO week, in UTC
/// (ADR-0027). Purge decisions use the END of the bucket, so a row is never purged before its age is reached and is
/// kept at most one week longer.
/// </summary>
public static class TimeBuckets
{
    public static DateOnly WeekStart(DateTimeOffset instant)
    {
        var day = DateOnly.FromDateTime(instant.UtcDateTime);
        var sinceMonday = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-sinceMonday);
    }

    /// <summary>First day after the bucket.</summary>
    public static DateOnly WeekEnd(DateOnly weekStart) => weekStart.AddDays(7);
}
