using System.Net;
using System.Net.Http.Headers;
using ExitInterviewAgent.Agent.Tracing;

namespace ExitInterviewAgent.Providers;

/// <summary>Which single header may carry the credential. Every other credential-bearing header is removed from every request.</summary>
public enum AuthScheme
{
    /// <summary>No credential is sent (a local model, a keyless gateway).</summary>
    None,
    /// <summary><c>x-api-key</c> (Anthropic).</summary>
    XApiKey,
    /// <summary><c>Authorization: Bearer</c> (OpenAI-compatible endpoints).</summary>
    Bearer,
}

/// <summary>Per-call state the transport reports into: attempts made and the last HTTP status. Flows with the async call, so the handler needs no reference to the chat client.</summary>
internal sealed class ProviderCallScope
{
    private static readonly AsyncLocal<ProviderCallScope?> Holder = new();

    public static ProviderCallScope? Current => Holder.Value;

    public int Attempts;
    public int LastStatus;

    public static ProviderCallScope Begin()
    {
        var scope = new ProviderCallScope();
        Holder.Value = scope;
        return scope;
    }

    public static void End() => Holder.Value = null;
}

/// <summary>
/// The transport policy for every provider, applied below the SDKs (which are configured not to retry) so that one set of rules
/// governs all three: one credential header and no other (the Anthropic SDK also forwards <c>ANTHROPIC_AUTH_TOKEN</c> from the
/// environment as <c>Authorization</c>; that is stripped here), a timeout per attempt, bounded retries with jittered backoff on
/// 408/429/5xx/timeouts/connection errors, <c>Retry-After</c> honoured up to a cap, cancellation respected, and a failure
/// surfaced as <see cref="ProviderException"/> built from the status code alone: the response body is never read, so it can never
/// reach a message, a log or a span.
/// </summary>
internal sealed class ResilientHttpHandler : DelegatingHandler
{
    private static readonly string[] CredentialHeaders = ["Authorization", "Proxy-Authorization", "x-api-key", "api-key", "X-Auth-Token", "anthropic-auth-token"];

    private readonly ResilienceOptions _options;
    private readonly AuthScheme _scheme;
    private readonly TimeProvider _time;
    private readonly Func<double> _random;
    private readonly string _provider;

    public ResilientHttpHandler(HttpMessageHandler inner, ResilienceOptions options, AuthScheme scheme, string providerLabel, TimeProvider time, Func<double> random)
        : base(inner)
    {
        _options = options;
        _scheme = scheme;
        _provider = providerLabel;
        _time = time;
        _random = random;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var credential = Credential(request);
        byte[]? body = null;
        if (request.Content is not null) body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ProviderCallScope.Current is { } scope) scope.Attempts = attempt;

            var (response, status, failure, retryAfter) = await AttemptAsync(request, body, credential, cancellationToken).ConfigureAwait(false);
            if (response is not null) return response;

            var transient = failure is ProviderFailureKind.Timeout or ProviderFailureKind.Network or ProviderFailureKind.RateLimited or ProviderFailureKind.ServerError;
            if (!transient || attempt > _options.MaxRetries || retryAfter > _options.MaxDelay)
                throw new ProviderException(failure, status == 0 ? null : status, attempt);

            var delay = retryAfter ?? Backoff(attempt);
            RecordRetry(attempt, status, failure);
            await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>One attempt with its own timeout, which ends (and its timer is released) before any backoff wait begins.</summary>
    private async Task<(HttpResponseMessage? Response, int Status, ProviderFailureKind Failure, TimeSpan? RetryAfter)> AttemptAsync(
        HttpRequestMessage request, byte[]? body, (string Name, string Value)? credential, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var attemptRequest = Clone(request, body, credential);
        try
        {
            var response = await base.SendAsync(attemptRequest, linked.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (ProviderCallScope.Current is { } scope) scope.LastStatus = status;
            if (response.IsSuccessStatusCode) return (response, status, default, null);

            var retryAfter = RetryAfterOf(response);
            response.Dispose();
            return (null, status, KindOf(status), retryAfter);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, 0, ProviderFailureKind.Timeout, null);
        }
        catch (HttpRequestException)
        {
            return (null, 0, ProviderFailureKind.Network, null);
        }
    }

    internal static ProviderFailureKind KindOf(int status) => status switch
    {
        401 or 403 => ProviderFailureKind.Authentication,
        408 => ProviderFailureKind.Timeout,
        429 => ProviderFailureKind.RateLimited,
        >= 500 => ProviderFailureKind.ServerError,
        _ => ProviderFailureKind.BadRequest,
    };

    /// <summary>Equal jitter: half the exponential step is fixed, half is random, capped at <see cref="ResilienceOptions.MaxDelay"/>.</summary>
    private TimeSpan Backoff(int attempt)
    {
        var step = Math.Min(_options.MaxDelay.TotalSeconds, _options.BaseDelay.TotalSeconds * Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(step / 2 + _random() * step / 2);
    }

    private TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header?.Date is { } date) return date - _time.GetUtcNow() is { } wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        return null;
    }

    private void RecordRetry(int attempt, int status, ProviderFailureKind kind)
    {
        var reason = status > 0 ? status.ToString(System.Globalization.CultureInfo.InvariantCulture) : SpanTags.Snake(kind.ToString());
        ProviderTelemetry.Retries.Add(1,
            new KeyValuePair<string, object?>(ProviderTelemetry.Attr.ProviderName, _provider),
            new KeyValuePair<string, object?>(ProviderTelemetry.Attr.RetryReason, reason));
        System.Diagnostics.Activity.Current.Event(ProviderTelemetry.Events.Retry, (ProviderTelemetry.Attr.RetryAttempt, attempt), (ProviderTelemetry.Attr.HttpStatusCode, status));
    }

    /// <summary>The credential header exactly as the SDK set it, if it is the one this provider may send.</summary>
    private (string Name, string Value)? Credential(HttpRequestMessage request)
    {
        var name = _scheme switch { AuthScheme.XApiKey => "x-api-key", AuthScheme.Bearer => "Authorization", _ => null };
        if (name is null) return null;
        return request.Headers.TryGetValues(name, out var values) && values.FirstOrDefault() is { Length: > 0 } v && !(v.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) ? (name, v) : null;
    }

    private HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body, (string Name, string Value)? credential)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version, VersionPolicy = source.VersionPolicy };
        foreach (var header in source.Headers)
        {
            if (CredentialHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase)) continue;
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (credential is { } c) clone.Headers.TryAddWithoutValidation(c.Name, c.Value);
        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            foreach (var header in source.Content!.Headers)
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }
        foreach (var option in source.Options) clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        return clone;
    }
}

public static class ProviderHttp
{
    /// <summary>
    /// The production transport: no automatic redirects (so a key header is never forwarded to another host by a 3xx), no cookies,
    /// pooled connections that are recycled. A test replaces it with a fake handler.
    /// </summary>
    public static HttpMessageHandler CreateTransport() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(30),
        AutomaticDecompression = DecompressionMethods.All,
    };

    internal static HttpClient CreateClient(ProviderKind kind, ResilienceOptions resilience, AuthScheme scheme, ProviderRuntime runtime)
    {
        var handler = new ResilientHttpHandler(runtime.Transport ?? CreateTransport(), resilience, scheme, ProviderTelemetry.ProviderLabel(kind), runtime.Time, runtime.Jitter);
        // Timeouts are the handler's (per attempt) and the chat client's (whole call); HttpClient's own timeout would cut across the retries.
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
