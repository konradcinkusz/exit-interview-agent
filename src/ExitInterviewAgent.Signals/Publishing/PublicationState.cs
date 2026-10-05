namespace ExitInterviewAgent.Signals;

/// <summary>What the publisher knows about itself, for the health check. No counts of anything below k live here; only times and a flag.</summary>
public sealed class PublicationState
{
    private readonly object _gate = new();
    private bool _lastRunFailed;

    public bool LastRunFailed
    {
        get { lock (_gate) { return _lastRunFailed; } }
        private set { lock (_gate) { _lastRunFailed = value; } }
    }

    public void Succeeded() => LastRunFailed = false;

    public void Failed() => LastRunFailed = true;
}

/// <summary>Completes when the signals schema is ready. The kernel's signal belongs to the interview context; this one is the module's own.</summary>
public sealed class SignalsSchemaSignal
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsCompleted => _done.Task.IsCompleted;
    public Exception? Failure { get; private set; }
    public Task WaitAsync(CancellationToken ct) => _done.Task.WaitAsync(ct);
    internal void Complete() => _done.TrySetResult();
    internal void Fail(Exception ex)
    {
        Failure = ex;
        _done.TrySetResult();
    }
}
