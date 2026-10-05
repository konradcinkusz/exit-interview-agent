using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.Cli;

internal enum Failure
{
    /// <summary>The name did not resolve. No byte was sent.</summary>
    DnsFailed,
    /// <summary>The connection was refused or could not be opened. No byte was sent.</summary>
    ConnectFailed,
    /// <summary>The TLS handshake failed (certificate, protocol). No request was sent; never retried: it is not transient.</summary>
    TlsFailed,
    /// <summary>No answer in time. The server may or may not have received the request.</summary>
    TimedOut,
    /// <summary>The user cancelled. The server may or may not have received the request.</summary>
    Cancelled,
    /// <summary>A 3xx answer. Never followed.</summary>
    Redirected,
    /// <summary>The answer was larger than the cap.</summary>
    ResponseTooLarge,
    /// <summary>The answer was not what this contract produces.</summary>
    BadResponse,
    /// <summary>The connection broke or the exchange failed in some other way.</summary>
    Broken,
}

/// <summary>What one call produced. Nothing in it can carry a request secret except <see cref="Accepted.ReceiptCode"/>, which the server just issued.</summary>
internal abstract record CallResult
{
    public sealed record Accepted(RedactedSecret ReceiptCode) : CallResult;
    public sealed record Deleted : CallResult;
    public sealed record Rejected(int Status, string Code, IReadOnlyList<FieldError> Errors, IReadOnlyList<string> Kinds, TimeSpan? RetryAfter) : CallResult;
    public sealed record Failed(Failure Reason, int? Status = null) : CallResult;
}

/// <summary>
/// The only code that talks to the submission service. What it guarantees (each is tested, and the redirect and retry rules are mutation-checked):
/// <list type="bullet">
/// <item>redirects are never followed (the handler is built with <c>AllowAutoRedirect = false</c>, and any 3xx that comes back is a failure): a redirect would forward the secret header to another host;</item>
/// <item>exactly one secret header per request, no cookies, no <c>Authorization</c>, no other custom header; <c>User-Agent</c> is the product name alone;</item>
/// <item>TLS settings are the platform defaults and are not touched; the address was already checked by <see cref="ServerUrl"/>;</item>
/// <item>timeouts: 10 s to connect, 30 s for the whole call; responses are read through a 64 KiB cap;</item>
/// <item>no retry of a submission, or of anything, except <b>one</b> more attempt when the connection could not be opened at all (name not resolved, connection refused), i.e. before any byte of the request left;</item>
/// <item>no exception text is ever surfaced (it can contain host names and addresses): every failure is one of <see cref="Failure"/>.</item>
/// </list>
/// </summary>
internal sealed class SubmissionClient : IDisposable
{
    public const int MaxResponseBytes = 64 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private const string SubmitPath = "/api/v1/submissions/ticketed";
    private const string ReceiptsPath = "/api/v1/receipts";
    private static readonly Regex SafeCode = new("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SafePath = new(@"^[A-Za-z0-9_/\[\].\-]{0,160}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retryDelay;

    /// <param name="handler">Tests and in-process hosting only; the real CLI passes null and gets <see cref="CreateHandler"/>.</param>
    public SubmissionClient(ServerUrl server, HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? retryDelay = null)
    {
        _http = new HttpClient(handler ?? CreateHandler(), disposeHandler: handler is null)
        {
            BaseAddress = server.Base,
            Timeout = Timeout.InfiniteTimeSpan, // the whole call is bounded below, bodies included
            MaxResponseContentBufferSize = MaxResponseBytes,
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("exit-interview", null));
        _timeout = timeout ?? DefaultTimeout;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(750);
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = ConnectTimeout,
        MaxResponseHeadersLength = 16, // KiB
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
    };

    public Task<CallResult> SubmitAsync(ReadOnlyMemory<byte> record, RedactedSecret ticket, CancellationToken ct) =>
        SendAsync(() =>
        {
            var content = new ByteArrayContent(record.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, SubmitPath) { Content = content };
            request.Headers.TryAddWithoutValidation(SubmissionHeaders.Ticket, ticket.Reveal());
            return request;
        }, HttpStatusCode.Created, ct);

    public Task<CallResult> DeleteReceiptAsync(RedactedSecret receiptCode, CancellationToken ct) =>
        SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, ReceiptsPath);
            request.Headers.TryAddWithoutValidation(SubmissionHeaders.ReceiptCode, receiptCode.Reveal());
            return request;
        }, HttpStatusCode.NoContent, ct);

    private async Task<CallResult> SendAsync(Func<HttpRequestMessage> build, HttpStatusCode success, CancellationToken userCancellation)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(userCancellation);
            deadline.CancelAfter(_timeout);
            var ct = userCancellation;
            try
            {
                using var request = build();
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                return await ReadAsync(response, success, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new CallResult.Failed(Failure.Cancelled); }
            catch (OperationCanceledException) { return new CallResult.Failed(Failure.TimedOut); }
            catch (HttpRequestException e) when (e.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError && attempt == 1)
            {
                // The only retry there is: nothing left this computer, so nothing can be duplicated.
                try { await Task.Delay(_retryDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return new CallResult.Failed(Failure.Cancelled); }
            }
            catch (HttpRequestException e)
            {
                return new CallResult.Failed(e.HttpRequestError switch
                {
                    HttpRequestError.NameResolutionError => Failure.DnsFailed,
                    HttpRequestError.ConnectionError => Failure.ConnectFailed,
                    HttpRequestError.SecureConnectionError => Failure.TlsFailed,
                    _ => Failure.Broken,
                });
            }
            catch (IOException) { return new CallResult.Failed(Failure.Broken); }
        }
    }

    private static async Task<CallResult> ReadAsync(HttpResponseMessage response, HttpStatusCode success, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400) return new CallResult.Failed(Failure.Redirected, status);

        var body = await ReadCappedAsync(response, ct).ConfigureAwait(false);
        if (body is null) return new CallResult.Failed(Failure.ResponseTooLarge, status);

        if (response.StatusCode == success)
        {
            if (success == HttpStatusCode.NoContent) return new CallResult.Deleted();
            return TryReceipt(body) is { } receipt ? new CallResult.Accepted(receipt) : new CallResult.Failed(Failure.BadResponse, status);
        }

        var retryAfter = RetryAfter(response);
        var (code, errors, kinds) = ParseProblem(body);
        return new CallResult.Rejected(status, code, errors, kinds, retryAfter);
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static RedactedSecret? TryReceipt(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("receiptCode", out var code) && code.ValueKind == JsonValueKind.String
                && RedactedSecret.TryCreate(code.GetString(), out var secret)) return secret;
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// Reads only what the contract defines and keeps a value only if it has the shape of a code or a schema path. The server is not
    /// trusted to print to a terminal: nothing else (no title, no free text, no control characters) is ever carried further.
    /// </summary>
    private static (string Code, IReadOnlyList<FieldError> Errors, IReadOnlyList<string> Kinds) ParseProblem(byte[] body)
    {
        var code = "UNKNOWN";
        var errors = new List<FieldError>();
        var kinds = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (code, errors, kinds);
            if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { } cs && SafeCode.IsMatch(cs)) code = cs;
            else if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String && e.GetString() == "rate_limited") code = SubmissionCodes.RateLimited;
            if (root.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray().Take(50))
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("code", out var ec) && ec.GetString() is { } ecs && SafeCode.IsMatch(ecs)
                        && item.TryGetProperty("path", out var ep) && ep.GetString() is { } eps && SafePath.IsMatch(eps))
                        errors.Add(new FieldError(ecs, eps));
            if (root.TryGetProperty("kinds", out var ks) && ks.ValueKind == JsonValueKind.Array)
                foreach (var item in ks.EnumerateArray().Take(16))
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } k && Regex.IsMatch(k, "^[A-Za-z]{1,24}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) kinds.Add(k);
        }
        catch (JsonException) { }
        return (code, errors, kinds);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return Clamp(delta);
        if (header?.Date is { } date) return Clamp(date - DateTimeOffset.UtcNow);
        return null;

        static TimeSpan Clamp(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : t;
    }

    public void Dispose() => _http.Dispose();
}
