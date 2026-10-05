using System.Globalization;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.Records;
using ExitInterviewAgent.ServiceDefaults;
using ExitInterviewAgent.Signals;
using Microsoft.Net.Http.Headers;

namespace ExitInterviewAgent.InterviewService.Endpoints;

/// <summary>
/// Employer signals, read-only, over the published snapshot (ADR-0052..0055). Policy <c>account</c> for now: opening these to anonymous
/// readers is a later, separate decision with its own threat-model entry. There is no endpoint that ranks, searches or filters employers
/// by what was measured, none that returns a record or a quote, and none that returns a count below the minimum group size.
/// Failures are problem details with a stable <c>code</c>; the same answer is given for an unknown employer and one with too few records.
/// </summary>
public static class SignalsEndpointNames
{
    public const string ListEmployers = "ListSignalsEmployers";
    public const string GetEmployer = "GetSignalsEmployer";
}

public static class SignalsEndpoints
{
    private const string ProblemType = "urn:exit-interview-agent:problem:";
    private const string DeletionNote = "The snapshot is rebuilt once per publication period, never on submission. A record deleted after publication is still counted until the next snapshot.";

    public static RouteGroupBuilder MapSignalsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/signals/employers", async (int? page, int? limit, HttpContext http, SnapshotReader reader, TimeProvider time, CancellationToken ct) =>
            {
                var (p, l) = ApiExtensions.ClampPage(page, limit);
                var result = await reader.ListAsync(p, l, ct);
                if (result.Snapshot is null)
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Json(new SignalsEmployerList(null, [], p, l, 0));
                }
                return Cached(http, result.Snapshot, time, $"p{p}l{l}",
                    () => new SignalsEmployerList(SnapshotInfoOf(result.Snapshot), result.Employers, p, l, result.Total));
            })
            .RequireRateLimiting(SignalsRateLimit.Policy)
            .WithName(SignalsEndpointNames.ListEmployers)
            .WithSummary("Employers with at least one displayable cell in the published snapshot, alphabetical. No scores, no ranking.")
            .WithDescription("Account token required (policy account). " + DeletionNote)
            .Produces<SignalsEmployerList>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/signals/employers/{employerRef}", async (string employerRef, HttpContext http, SnapshotReader reader, TimeProvider time, CancellationToken ct) =>
            {
                if (!EmployerRef.IsValid(employerRef))
                {
                    return Problem(SignalsCodes.InvalidEmployerRef, StatusCodes.Status400BadRequest);
                }
                var found = await reader.GetAsync(employerRef, ct);
                if (found is not { } hit)
                {
                    // The same body whether the employer is unknown or has no displayable cell: the response must not say which.
                    var current = await reader.CurrentAsync(ct);
                    http.Response.Headers.CacheControl = current is null ? "no-store" : CacheControlFor(current, time);
                    return Problem(SignalsCodes.EmployerNotFound, StatusCodes.Status404NotFound);
                }
                return Cached(http, hit.Snapshot, time, employerRef, () => SignalsMapping.ToContract(hit.Snapshot, hit.View));
            })
            .RequireRateLimiting(SignalsRateLimit.Policy)
            .WithName(SignalsEndpointNames.GetEmployer)
            .WithSummary("The displayable cells of one employer: per topic n, mean with a 95% interval, reliability, coverage, and single-band cuts.")
            .WithDescription("Account token required (policy account). Insufficient data is a status, never a number. " + DeletionNote)
            .Produces<SignalsEmployer>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return group;
    }

    private static SignalsSnapshotInfo SnapshotInfoOf(SnapshotInfo s)
        => new(s.GeneratedAt, s.IntervalHours, s.RulesVersion, s.MinimumGroupSize, DeletionsAppearAtNextPublication: true);

    /// <summary>
    /// Caching consistent with batching: the representation cannot change before the next batch, so a client may keep it until then
    /// (private: the endpoint needs an account), and a conditional request is answered with 304 and no body.
    /// </summary>
    private static IResult Cached<T>(HttpContext http, SnapshotInfo snapshot, TimeProvider time, string key, Func<T> body)
    {
        var etag = new EntityTagHeaderValue($"\"s{snapshot.Seq}-{key}\"", isWeak: true);
        http.Response.Headers.CacheControl = CacheControlFor(snapshot, time);
        http.Response.Headers.Vary = HeaderNames.Authorization;
        http.Response.Headers.ETag = etag.ToString();
        http.Response.Headers.LastModified = snapshot.GeneratedAt.ToString("R", CultureInfo.InvariantCulture);
        if (http.Request.Headers.IfNoneMatch.Any(v => v == etag.ToString() || v == "*"))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return Results.Json(body());
    }

    private static string CacheControlFor(SnapshotInfo snapshot, TimeProvider time)
    {
        var untilNextBatch = snapshot.GeneratedAt.AddHours(snapshot.IntervalHours) - time.GetUtcNow();
        return $"private, max-age={Math.Max(60, (int)untilNextBatch.TotalSeconds)}";
    }

    private static IResult Problem(string code, int status)
        => Results.Problem(statusCode: status, title: code, type: ProblemType + code.ToLowerInvariant(), extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Maps the module's published views onto the public contract: the one place the two vocabularies meet.</summary>
internal static class SignalsMapping
{
    public static SignalsEmployer ToContract(SnapshotInfo snapshot, EmployerView view) => new(
        new SignalsSnapshotInfo(snapshot.GeneratedAt, snapshot.IntervalHours, snapshot.RulesVersion, snapshot.MinimumGroupSize, true),
        view.EmployerRef,
        view.RespondentsBand,
        [.. view.Topics.Select(t => new SignalsTopic(
            t.Topic,
            t.Overall is null ? SignalsVocabulary.TopicInsufficientData : SignalsVocabulary.TopicOk,
            t.Overall is null ? null : Stats(t.Overall),
            [.. t.Cuts.Select(c => new SignalsCut(
                c.Dimension,
                c.Published ? SignalsVocabulary.CutPublished : SignalsVocabulary.CutSuppressed,
                [.. c.Cells.Select(b => new SignalsBandCell(b.Band, b.Status, b.Stats is null ? null : Stats(b.Stats)))]))]))]);

    private static SignalsStats Stats(StatsView s) => new(
        s.N, s.Mean, new SignalsInterval(s.Lower, s.Upper, MeanInterval.Level, MeanInterval.Method),
        s.Reliability, s.Coverage,
        s.Distribution?.Select(g => new SignalsGroup(g.Key, g.Count)).ToList(),
        s.Verification?.Select(g => new SignalsGroup(g.Key, g.Count)).ToList());
}
