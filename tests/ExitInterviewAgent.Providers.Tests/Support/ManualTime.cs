namespace ExitInterviewAgent.Providers.Tests.Support;

/// <summary>A clock the test advances by hand. Every <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> and every timer-based cancellation in the code under test runs on it, so backoff is asserted exactly and nothing sleeps.</summary>
public sealed class ManualTime : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private int _created;
    private DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every due time (relative) a timer was created with, in order: the delays the code asked for.</summary>
    public List<TimeSpan> RequestedDelays { get; } = [];

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }

    public override long GetTimestamp() { lock (_gate) return _now.UtcTicks; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _created);
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
            if (dueTime != Timeout.InfiniteTimeSpan) RequestedDelays.Add(dueTime);
        }
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves time to the earliest pending timer and fires it. False when nothing is pending.</summary>
    public bool AdvanceToNextTimer()
    {
        ManualTimer? next;
        lock (_gate)
        {
            next = _timers.Where(t => t.Due is not null).OrderBy(t => t.Due).FirstOrDefault();
            if (next is null) return false;
            if (next.Due > _now) _now = next.Due.Value;
            next.Due = null;
        }
        next.Fire();
        return true;
    }

    /// <summary>Drives <paramref name="task"/> to completion, advancing the clock whenever it is waiting on a timer.</summary>
    public async Task<T> Drive<T>(Task<T> task)
    {
        for (var i = 0; i < 3_000 && !task.IsCompleted; i++)
        {
            // Let the code under test run to its next wait before the clock moves, so time never jumps past work that is still in flight.
            // "Quiet" = no timer was created for three consecutive polls and the task is still not done.
            var created = Volatile.Read(ref _created);
            var quiet = 0;
            while (quiet < 3 && !task.IsCompleted)
            {
                await Task.Delay(4);
                quiet = Volatile.Read(ref _created) == created ? quiet + 1 : 0;
                created = Volatile.Read(ref _created);
            }
            if (!task.IsCompleted) AdvanceToNextTimer();
        }
        if (!task.IsCompleted) throw new TimeoutException("The driven task did not complete within the test budget.");
        return await task;
    }

    private void Remove(ManualTimer timer) { lock (_gate) _timers.Remove(timer); }

    private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate) Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
