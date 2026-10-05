using System.Collections;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExitInterviewAgent.InterviewService.Infrastructure.Logging;

/// <summary>
/// Defence in depth for "no PII in any log or trace" (brief §6, ADR-0014). The first line of defence is that nothing
/// here ever has an email: the validated principal keeps only <c>sub</c>, <c>client_id</c> and <c>scope</c>. This is the
/// second: whatever reaches <c>ILogger</c> (a request URL with an address in its query, an exception message from a
/// library, a structured argument) has email addresses replaced before any provider, console or OTLP, sees it.
/// </summary>
public static partial class LogScrubber
{
    public const string Replacement = "[email-redacted]";

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    public static string Scrub(string text) => text.Contains('@') ? EmailPattern().Replace(text, Replacement) : text;

    public static IServiceCollection AddEmailScrubbingLogs(this IServiceCollection services)
    {
        // The default ILoggerFactory registration is the concrete LoggerFactory; keep it as the inner factory
        // (providers, filters and OpenTelemetry attach to it as usual) and put the scrubbing factory in front.
        services.AddSingleton<LoggerFactory>();
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(sp => new ScrubbingLoggerFactory(sp.GetRequiredService<LoggerFactory>())));
        return services;
    }
}

internal sealed class ScrubbingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new ScrubbingLogger(inner.CreateLogger(categoryName));
    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
    public void Dispose() => inner.Dispose();
}

internal sealed class ScrubbingLogger(ILogger inner) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!inner.IsEnabled(logLevel))
        {
            return;
        }
        var message = LogScrubber.Scrub(formatter(state, exception));
        inner.Log(logLevel, eventId, new ScrubbedState(state, message), ScrubException(exception), static (s, _) => s.Message);
    }

    private static Exception? ScrubException(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }
        var text = exception.ToString();
        var scrubbed = LogScrubber.Scrub(text);
        return ReferenceEquals(text, scrubbed) || text == scrubbed ? exception : new ScrubbedException(exception.GetType().Name, scrubbed);
    }
}

/// <summary>The structured state with every string value scrubbed; the message is the scrubbed formatted message.</summary>
internal sealed class ScrubbedState : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] _items;

    public ScrubbedState(object? state, string message)
    {
        Message = message;
        _items = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? [.. pairs.Select(p => new KeyValuePair<string, object?>(p.Key, ScrubValue(p.Value)))]
            : [];
    }

    public string Message { get; }
    public int Count => _items.Length;
    public KeyValuePair<string, object?> this[int index] => _items[index];
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => Message;

    private static object? ScrubValue(object? value) => value switch
    {
        null => null,
        string s => LogScrubber.Scrub(s),
        _ when value.GetType().IsPrimitive || value is Enum || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan => value,
        _ => value.ToString() is { } text && LogScrubber.Scrub(text) is var scrubbed && scrubbed != text ? scrubbed : value,
    };
}

/// <summary>Stands in for an exception whose text contained an email; keeps the type name, drops the address.</summary>
internal sealed class ScrubbedException(string typeName, string scrubbedText) : Exception($"{typeName}: {scrubbedText}")
{
    public override string ToString() => Message;
}
