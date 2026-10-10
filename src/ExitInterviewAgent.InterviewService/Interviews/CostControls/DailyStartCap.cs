using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews.CostControls;

/// <summary>
/// The global brake on starts per UTC day (web-app-plan §2, ADR-0078). The count is in memory and resets at midnight UTC; a
/// restart resets it too, which is accepted: it limits a runaway, it does not account for money (the spend limit in the
/// model provider's console does that, see the runbook). A start reserves a slot before it runs and gives the slot back when
/// it does not produce a session, so a refused or invalid request never uses the cap up.
/// </summary>
public sealed class DailyStartCap(TimeProvider clock, IOptions<CostControlOptions> options, ILogger<DailyStartCap> logger)
{
    private readonly object _gate = new();
    private DateOnly _day;
    private int _reserved;
    private bool _warnedToday;

    /// <summary>The day the slot was reserved for, or null when the cap is reached (then <paramref name="retryAfterSeconds"/> is set).</summary>
    public DateOnly? TryReserve(out int retryAfterSeconds)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(now);
            if (today != _day)
            {
                _day = today;
                _reserved = 0;
                _warnedToday = false;
            }
            retryAfterSeconds = SecondsUntilMidnight(today, now);
            var cap = options.Value.MaxStartsPerDay;
            if (_reserved < cap)
            {
                _reserved++;
                return today;
            }
            if (!_warnedToday)
            {
                _warnedToday = true;
                // The number is configuration, not a person: safe to log.
                logger.LogWarning("The daily start cap of {Cap} is reached: new interviews answer 503 until midnight UTC", cap);
            }
            return null;
        }
    }

    /// <summary>Gives back a slot reserved for <paramref name="day"/>. A slot from an earlier day is already gone.</summary>
    public void Release(DateOnly day)
    {
        lock (_gate)
        {
            if (day == _day && _reserved > 0) _reserved--;
        }
    }

    private static int SecondsUntilMidnight(DateOnly today, DateTime now)
    {
        var midnight = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return Math.Max(1, (int)Math.Ceiling((midnight - now).TotalSeconds));
    }
}
