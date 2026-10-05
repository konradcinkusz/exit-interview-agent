using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.InterviewService.Tests.Support;

/// <summary>Records every log line (message, structured attributes, exception) so tests can assert on what was logged.</summary>
public sealed class CaptureLoggerProvider : ILoggerProvider, ILogger
{
    public List<string> Lines { get; } = [];
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var attributes = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(';', pairs.Select(p => $"{p.Key}={p.Value}")) : "";
        lock (Lines) Lines.Add($"{formatter(state, exception)}|{attributes}|{exception}");
    }
    public void Dispose() { }
}
