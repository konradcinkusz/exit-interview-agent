namespace ExitInterviewAgent.Agent.Mock;

/// <summary>
/// A clock that moves only when told to. Demos and tests advance it as a simulated interviewee "types", so the
/// duration band of a run is reproducible instead of depending on how fast the machine is.
/// </summary>
public sealed class SimulatedClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
