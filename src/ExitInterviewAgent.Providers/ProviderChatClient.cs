using System.Diagnostics;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Providers;

/// <summary>Everything a provider client needs from its host that tests (and T7) may want to replace.</summary>
public sealed class ProviderRuntime
{
    /// <summary>The innermost HTTP handler. Null: <see cref="ProviderHttp.CreateTransport"/>. Tests pass a fake.</summary>
    public HttpMessageHandler? Transport { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>A value in [0, 1) for backoff jitter.</summary>
    public Func<double> Jitter { get; init; } = Random.Shared.NextDouble;

    /// <summary>The hard budget this client counts against. Null: one is created from the protocol defaults by the factory.</summary>
    public InterviewBudget? Budget { get; init; }

    /// <summary>Receives provider ids, enum names, status codes and counts only.</summary>
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Wraps a provider SDK's <see cref="IChatClient"/>: the hard budget before each call, one <c>provider.call</c> span and the
/// GenAI metrics around it, usage accounting from the provider's response, the whole-call deadline, and the single place where
/// every failure becomes a <see cref="ProviderException"/>. Nothing from a provider's exception (message, inner exception, headers,
/// body) is carried over: only its type name and, for HTTP failures, the status code the handler recorded. Streaming is off.
/// </summary>
public sealed class ProviderChatClient : DelegatingChatClient
{
    private readonly ProviderSettings _settings;
    private readonly ProviderRuntime _runtime;
    private readonly ChatClientMetadata _metadata;
    private readonly string _provider;
    private readonly string _modelLabel;
    private readonly IDisposable? _owned;
    private int _consecutiveFailures;

    public ProviderChatClient(IChatClient inner, ProviderSettings settings, ProviderRuntime runtime, InterviewBudget budget, IDisposable? owned = null) : base(inner)
    {
        _owned = owned;
        _settings = settings;
        _runtime = runtime;
        Budget = budget;
        _provider = ProviderTelemetry.ProviderLabel(settings.Kind);
        _modelLabel = ProviderTelemetry.Label(settings.Model);
        // No provider URI: where the model lives is the user's business and is not exposed through the metadata the agent traces.
        _metadata = new ChatClientMetadata(_provider, null, _modelLabel);
    }

    public InterviewBudget Budget { get; }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        Budget.EnsureAvailable();

        using var span = ProviderTelemetry.Source.StartActivity(ProviderTelemetry.Spans.Call, ActivityKind.Client);
        span.Code(ProviderTelemetry.Attr.OperationName, "chat").Code(ProviderTelemetry.Attr.ProviderName, _provider).Code(ProviderTelemetry.Attr.RequestModel, _modelLabel);

        var started = _runtime.Time.GetTimestamp();
        var scope = ProviderCallScope.Begin();
        using var deadline = new CancellationTokenSource(_settings.Resilience.OverallTimeout, _runtime.Time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var response = await base.GetResponseAsync(list, options, linked.Token).ConfigureAwait(false);
            var elapsed = _runtime.Time.GetElapsedTime(started);
            Interlocked.Exchange(ref _consecutiveFailures, 0);

            var estimated = response.Usage?.InputTokenCount is null || response.Usage.OutputTokenCount is null;
            var input = response.Usage?.InputTokenCount ?? list.Sum(m => ModelMeter.Estimate(m.Text));
            var output = response.Usage?.OutputTokenCount ?? ModelMeter.Estimate(response.Text);
            Budget.Record(input, output, elapsed, estimated);

            span.Code(ProviderTelemetry.Attr.ResponseModel, response.ModelId ?? _modelLabel)
                .Tag(ProviderTelemetry.Attr.Attempts, scope.Attempts).Tag(ProviderTelemetry.Attr.UsageEstimated, estimated)
                .Tag(ProviderTelemetry.Attr.UsageInputTokens, input).Tag(ProviderTelemetry.Attr.UsageOutputTokens, output);
            span?.SetTag(ProviderTelemetry.Attr.FinishReasons, new[] { ProviderTelemetry.FinishReasonCode(response.FinishReason) });
            if (scope.LastStatus > 0) span.Tag(ProviderTelemetry.Attr.HttpStatusCode, scope.LastStatus);

            RecordMetrics(elapsed, input, output, error: null);
            return response;
        }
        catch (Exception ex)
        {
            var elapsed = _runtime.Time.GetElapsedTime(started);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                span?.SetStatus(ActivityStatusCode.Error, "cancelled");
                span.Code(ProviderTelemetry.Attr.ErrorType, "cancelled");
                throw;
            }

            var failure = Map(ex, deadline.IsCancellationRequested, scope.Attempts);
            if (failure.Kind != ProviderFailureKind.BudgetExceeded && Interlocked.Increment(ref _consecutiveFailures) >= _settings.Resilience.MaxConsecutiveFailures)
                failure = new ProviderException(ProviderFailureKind.Unavailable, failure.StatusCode, Math.Max(scope.Attempts, failure.Attempts));

            span?.SetStatus(ActivityStatusCode.Error, "provider_call_failed");
            span.Code(ProviderTelemetry.Attr.ErrorType, failure.FailureCode).Tag(ProviderTelemetry.Attr.Attempts, Math.Max(scope.Attempts, failure.Attempts));
            if (failure.StatusCode is { } status) span.Tag(ProviderTelemetry.Attr.HttpStatusCode, status);
            RecordMetrics(elapsed, 0, 0, failure.FailureCode);
            _runtime.Logger?.LogWarning("Provider call failed: {Provider} {Kind} status {Status} attempts {Attempts}", _provider, failure.Kind, failure.StatusCode ?? 0, Math.Max(scope.Attempts, failure.Attempts));
            throw failure;
        }
        finally
        {
            ProviderCallScope.End();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _owned?.Dispose();
        base.Dispose(disposing);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is off: every provider call is metered as one call.");

    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? _metadata : base.GetService(serviceType, serviceKey);

    /// <summary>Walks the exception chain for the handler's <see cref="ProviderException"/>; anything else is reduced to its type name.</summary>
    private static ProviderException Map(Exception ex, bool deadlineHit, int attempts)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is ProviderException p) return p;
        if (deadlineHit && ex is OperationCanceledException) return new ProviderException(ProviderFailureKind.Timeout, attempts: attempts);
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is HttpRequestException or System.Net.Sockets.SocketException or IOException) return new ProviderException(ProviderFailureKind.Network, attempts: attempts);
        return new ProviderException(ProviderFailureKind.InvalidResponse, detail: SpanTags.SafeCode(ex.GetType().Name));
    }

    private void RecordMetrics(TimeSpan elapsed, long input, long output, string? error)
    {
        var common = new TagList
        {
            { ProviderTelemetry.Attr.OperationName, "chat" },
            { ProviderTelemetry.Attr.ProviderName, _provider },
            { ProviderTelemetry.Attr.RequestModel, _modelLabel },
        };
        var duration = common;
        if (error is not null) duration.Add(ProviderTelemetry.Attr.ErrorType, error);
        ProviderTelemetry.OperationDuration.Record(elapsed.TotalSeconds, duration);
        if (error is not null) return;
        var inTags = common;
        inTags.Add(ProviderTelemetry.Attr.TokenType, "input");
        ProviderTelemetry.TokenUsage.Record(input, inTags);
        var outTags = common;
        outTags.Add(ProviderTelemetry.Attr.TokenType, "output");
        ProviderTelemetry.TokenUsage.Record(output, outTags);
    }
}
