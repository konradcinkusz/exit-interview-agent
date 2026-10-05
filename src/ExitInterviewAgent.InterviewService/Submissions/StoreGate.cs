using ExitInterviewAgent.ServiceDefaults;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// The InMemory provider enforces neither unique indexes nor transactions, so on that path (tests, a fresh clone) the
/// write section is serialised in-process to keep "exactly one success" true. On PostgreSQL the gate is a no-op and the
/// database's unique indexes and atomic deletes decide races, which is what the concurrency tests exercise.
/// </summary>
public sealed class StoreGate(DatabaseMode mode)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        if (mode.IsRelational)
        {
            return NoOp.Instance;
        }
        await _gate.WaitAsync(ct);
        return new Release(_gate);
    }

    private sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();
        public void Dispose() { }
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
