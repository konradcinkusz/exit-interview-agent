using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExitInterviewAgent.Cli.Tests.Support;

/// <summary>One request as the fake saw it: raw headers (so tests can assert exactly which ones were sent), the path with its query, the body bytes.</summary>
public sealed record SeenRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string[]> Headers, byte[] Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? string.Join(",", v) : null;
}

public sealed record FakeReply(int Status, string? Body = null, string ContentType = "application/json", IReadOnlyDictionary<string, string>? Headers = null, int? PadBody = null);

/// <summary>
/// A real HTTP listener on a loopback port, so the CLI's real <c>SocketsHttpHandler</c> (and its redirect behaviour) is exercised.
/// CONTRACT NOTE: this fake mirrors the T5 server contract (ADR-0029, ADR-0030, <c>SubmissionEndpoints</c>): the ticket in header
/// <c>X-Submission-Ticket</c> (the query string is ignored), problem+json <c>{type, title, status, code, errors?, kinds?}</c> with
/// <c>type = urn:exit-interview-agent:problem:&lt;code lower case&gt;</c> and <c>title = code</c>, 201 <c>{receiptCode}</c>, 204 on a well-formed
/// receipt code and 400 INVALID_RECEIPT_CODE otherwise, <c>{error:"rate_limited",retryAfter:60}</c> with <c>Retry-After</c> on 429.
/// If that contract changes, this file must change with it; the end-to-end test against the real service (InterviewService.Tests,
/// CliEndToEndTests) is what notices when it does not.
/// </summary>
public sealed class FakeSubmissionBackend : IDisposable
{
    public const string TicketPath = "/api/v1/submissions/ticketed";
    public const string ReceiptsPath = "/api/v1/receipts";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly List<SeenRequest> _seen = [];

    public Uri BaseUrl { get; }
    public HashSet<string> ValidTickets { get; } = [];

    /// <summary>Overrides the answer for a request; null falls through to the contract-mirroring default.</summary>
    public Func<SeenRequest, FakeReply?>? Script { get; set; }

    public IReadOnlyList<SeenRequest> Requests { get { lock (_seen) return [.. _seen]; } }

    public FakeSubmissionBackend()
    {
        var port = FreePort();
        BaseUrl = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUrl.ToString());
        _listener.Start();
        _loop = Task.Run(Loop);
    }

    public static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public static string NewReceiptCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(34)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static FakeReply Problem(int status, string code, IEnumerable<(string Code, string Path)>? errors = null, IEnumerable<string>? kinds = null)
    {
        var o = new JsonObject
        {
            ["type"] = "urn:exit-interview-agent:problem:" + code.ToLowerInvariant(),
            ["title"] = code,
            ["status"] = status,
            ["code"] = code,
        };
        if (errors is not null) o["errors"] = new JsonArray([.. errors.Select(e => (JsonNode)new JsonObject { ["code"] = e.Code, ["path"] = e.Path })]);
        if (kinds is not null) o["kinds"] = new JsonArray([.. kinds.Select(k => (JsonNode)JsonValue.Create(k)!)]);
        return new FakeReply(status, o.ToJsonString(), "application/problem+json");
    }

    public static FakeReply RateLimited(int seconds = 60) =>
        new(429, $"{{\"error\":\"rate_limited\",\"retryAfter\":{seconds}}}", Headers: new Dictionary<string, string> { ["Retry-After"] = seconds.ToString() });

    private async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            using var ms = new MemoryStream();
            await ctx.Request.InputStream.CopyToAsync(ms);
            var headers = ctx.Request.Headers.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => ctx.Request.Headers.GetValues(k)!, StringComparer.OrdinalIgnoreCase);
            var seen = new SeenRequest(ctx.Request.HttpMethod, ctx.Request.Url!.PathAndQuery, headers, ms.ToArray());
            lock (_seen) _seen.Add(seen);

            var reply = Script?.Invoke(seen) ?? Default(seen);
            ctx.Response.StatusCode = reply.Status;
            ctx.Response.ContentType = reply.ContentType;
            foreach (var (k, v) in reply.Headers ?? new Dictionary<string, string>()) ctx.Response.Headers[k] = v;
            var body = reply.PadBody is { } pad ? new string('x', pad) : reply.Body ?? string.Empty;
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch (Exception) { try { ctx.Response.Abort(); } catch (Exception) { } }
    }

    private FakeReply Default(SeenRequest r)
    {
        var path = r.PathAndQuery.Split('?')[0];
        if (path == TicketPath && r.Method == "POST")
        {
            var ticket = r.Header("X-Submission-Ticket");
            if (ticket is null || !ValidTickets.Remove(ticket)) return Problem(401, "TICKET_INVALID");
            return new FakeReply(201, JsonSerializer.Serialize(new { receiptCode = NewReceiptCode() }));
        }
        if (path == ReceiptsPath && r.Method == "DELETE")
        {
            var code = r.Header("X-Receipt-Code");
            return code is { Length: 46 } ? new FakeReply(204) : Problem(400, "INVALID_RECEIPT_CODE");
        }
        return new FakeReply(404, "{}");
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        _stop.Dispose();
    }
}
