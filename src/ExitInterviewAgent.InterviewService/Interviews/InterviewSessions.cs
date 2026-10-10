using System.Security.Cryptography;
using System.Text.Json;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Interviews.CostControls;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>An endpoint answer: the value on success, or the status and stable code on refusal.</summary>
public readonly record struct Api<T>(T? Value, int Status, string? Code)
{
    public static Api<T> Ok(int status, T value) => new(value, status, null);
    public static Api<T> Fail(int status, string code) => new(default, status, code);
}

/// <summary>
/// The session API (web-app-plan §10): start, reply, state, result, delete. It owns the rules the endpoints must not repeat:
/// ownership, the one open session, the credit, the refund of a failed session, and the end of a background interview.
/// Nothing here logs or traces interview text (ADR-0076).
/// </summary>
public sealed class InterviewSessions(
    InterviewSessionStore store,
    IInterviewModelFactory models,
    ICreditGate credits,
    ICreditRefund refunds,
    IOptionsMonitor<InterviewServiceOptions> options,
    TimeProvider clock,
    CostMetrics metrics,
    ILogger<InterviewSessions> logger)
{
    /// <summary>The employer is not asked in the web interview; the record carries this placeholder. Submission (later) states a real one.</summary>
    internal const string UnnamedEmployer = "employer-not-stated";

    private const int MaxReplyChars = 2000;

    public async Task<Api<InterviewStarted>> StartAsync(string owner, CreateInterviewRequest request, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!o.Enabled) return Api<InterviewStarted>.Fail(503, InterviewCodes.InterviewsDisabled);
        if (!InterviewMapping.TryParseLanguage(request.Language, out var language) || !InterviewMapping.TryParseTenure(request.Tenure, out var tenure))
            return Api<InterviewStarted>.Fail(400, InterviewCodes.InvalidRequest);

        // The provider is checked before anything is consumed: a start that cannot run must not cost a credit.
        InterviewModel model;
        try
        {
            model = models.Create();
        }
        catch (InterviewUnavailableException)
        {
            return Api<InterviewStarted>.Fail(503, InterviewCodes.InterviewsDisabled);
        }

        var session = new InterviewSession(NewId(), owner, language, tenure, clock.GetUtcNow());
        if (!store.TryAdd(session))
        {
            model.Dispose();
            return Api<InterviewStarted>.Fail(409, InterviewCodes.InterviewInProgress);
        }
        if (!await credits.TryConsumeAsync(owner, session.Id, ct).ConfigureAwait(false))
        {
            store.Remove(owner, session.Id);
            model.Dispose();
            return Api<InterviewStarted>.Fail(402, InterviewCodes.PaymentRequired);
        }

        _ = Task.Run(() => RunAsync(session, model));

        var first = await WaitForEventAsync(session, o, ct).ConfigureAwait(false);
        if (first?.Turn is not { } turn) return Api<InterviewStarted>.Fail(503, InterviewCodes.ProviderUnavailable);
        return Api<InterviewStarted>.Ok(201, new InterviewStarted(session.Id, InterviewSession.ToSnake(first.Status.ToString()), language, session.ExpiresAt(o), turn));
    }

    public async Task<Api<InterviewReplyResponse>> ReplyAsync(string owner, string id, InterviewReplyRequest request, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var lookup = Lookup(owner, id, out var session);
        if (session is null) return LookupFailure<InterviewReplyResponse>(lookup);
        session.Touch(clock.GetUtcNow());

        var text = request.Text;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxReplyChars) return Api<InterviewReplyResponse>.Fail(422, InterviewCodes.ReplyInvalid);
        if (session.BeginReply() is { } refusal) return Api<InterviewReplyResponse>.Fail(409, refusal);

        session.TrySendReply(text);
        var next = await WaitForEventAsync(session, o, ct).ConfigureAwait(false);
        if (next is null) return Api<InterviewReplyResponse>.Fail(503, InterviewCodes.ProviderUnavailable);
        if (next.Turn is { } turn) return Api<InterviewReplyResponse>.Ok(200, new InterviewReplyResponse(InterviewSession.ToSnake(session.Status.ToString()), turn, null));
        if (next.Status == SessionStatus.Failed) return Api<InterviewReplyResponse>.Fail(503, InterviewCodes.ProviderUnavailable);
        return Api<InterviewReplyResponse>.Ok(200, new InterviewReplyResponse(InterviewSession.ToSnake(next.Status.ToString()), next.FinalTurn, next.Ending));
    }

    public Api<InterviewState> Get(string owner, string id)
    {
        var lookup = Lookup(owner, id, out var session);
        if (session is null) return LookupFailure<InterviewState>(lookup);
        session.Touch(clock.GetUtcNow());
        return Api<InterviewState>.Ok(200, session.ToState(options.CurrentValue));
    }

    public Api<InterviewResultResponse> Result(string owner, string id)
    {
        var lookup = Lookup(owner, id, out var session);
        if (session is null) return LookupFailure<InterviewResultResponse>(lookup);
        if (session.Status != SessionStatus.Completed || session.Result is null) return Api<InterviewResultResponse>.Fail(409, InterviewCodes.NotCompleted);
        return Api<InterviewResultResponse>.Ok(200, session.Result);
    }

    /// <summary>Withdraws the session and wipes it. No refund: the person chose this.</summary>
    public bool Delete(string owner, string id)
    {
        var open = store.Find(owner, id, out var session) == SessionLookup.Found && session is { IsTerminal: false };
        var removed = store.Remove(owner, id);
        if (removed && open) metrics.Withdrawn();
        return removed;
    }

    private SessionLookup Lookup(string owner, string id, out InterviewSession? session) => store.Find(owner, id, out session);

    private static Api<T> LookupFailure<T>(SessionLookup lookup) => lookup == SessionLookup.Gone
        ? Api<T>.Fail(410, InterviewCodes.Gone)
        : Api<T>.Fail(404, InterviewCodes.NotFound);

    private async Task<SessionEvent?> WaitForEventAsync(InterviewSession session, InterviewServiceOptions o, CancellationToken ct)
    {
        try
        {
            var next = await session.NextEventAsync(o.TurnTimeout, ct).ConfigureAwait(false);
            // Timed out: the interview cannot go on without the next turn, so it ends as failed (and its credit comes back).
            if (next is null) await EndAsync(session, SessionStatus.Failed, InterviewCodes.ProviderUnavailable, null).ConfigureAwait(false);
            return next;
        }
        catch (OperationCanceledException)
        {
            // The client went away while the interview was working: nobody can continue this session, so it ends as failed.
            await EndAsync(session, SessionStatus.Failed, InterviewCodes.RequestCancelled, null).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The background interview: runs the agent with the channel interviewee and ends the session with its outcome.</summary>
    private async Task RunAsync(InterviewSession session, InterviewModel model)
    {
        var protocol = InterviewProtocol.For(session.Language);
        var meter = new ModelMeter(protocol.Limits);
        var runOptions = new InterviewOptions(UnnamedEmployer, new RecordContext(session.Tenure))
        {
            Protocol = protocol,
            Clock = clock,
        };
        try
        {
            var runner = InterviewRunner.Create(model.Client, runOptions, meter);
            var result = await runner.RunAsync(new ChannelInterviewee(session), session.Cancellation.Token).ConfigureAwait(false);
            await FinishAsync(session, result, model, meter).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
            // Deleted, expired or ended by the request path: nothing is left to say.
        }
        catch (Exception e)
        {
            // The type only: an exception message may carry content.
            logger.LogWarning("Interview ended as failed: {ExceptionType}", e.GetType().Name);
            await EndAsync(session, SessionStatus.Failed, InterviewCodes.ProviderUnavailable, null).ConfigureAwait(false);
        }
        finally
        {
            // The usage counts every model call of the interview, including its tiles and a cancelled run (cost is cost).
            metrics.Usage(meter.Tokens, meter.Calls);
            model.Dispose();
        }
    }

    private async Task FinishAsync(InterviewSession session, InterviewResult result, InterviewModel model, ModelMeter meter)
    {
        switch (result.Outcome)
        {
            case InterviewOutcome.Completed when result.RecordJson is not null:
                var tiles = await BuildTilesAsync(session, result, model, meter).ConfigureAwait(false);
                using (var doc = JsonDocument.Parse(result.RecordJson))
                {
                    var payload = new InterviewResultResponse(doc.RootElement.Clone(), tiles, new InterviewUsage(meter.Calls, meter.Tokens));
                    await EndAsync(session, SessionStatus.Completed, result.EndReason, payload).ConfigureAwait(false);
                }
                return;
            case InterviewOutcome.Withdrawn:
            case InterviewOutcome.ConsentNotGiven:
            case InterviewOutcome.Abandoned:
                await EndAsync(session, SessionStatus.Stopped, result.EndReason, null).ConfigureAwait(false);
                return;
            default:
                // PII guard and extraction failures are the service's fault: nothing may be submitted, and the credit comes back.
                await EndAsync(session, SessionStatus.Failed, result.EndReason, null).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>The first ending wins. A failed session returns its credit once; a stopped or completed one keeps it.</summary>
    private async Task EndAsync(InterviewSession session, SessionStatus status, string reason, InterviewResultResponse? payload)
    {
        if (!session.Finish(status, reason, payload, clock.GetUtcNow())) return;
        store.MarkClosed(session.Owner, session.Id);
        switch (status)
        {
            case SessionStatus.Completed: metrics.Completed(); break;
            case SessionStatus.Failed: metrics.Failed(); break;
            case SessionStatus.Stopped: metrics.Withdrawn(); break;
        }
        if (status == SessionStatus.Failed && !session.Refunded)
        {
            session.Refunded = true;
            try
            {
                await refunds.RefundAsync(session.Owner, session.Id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError("Credit refund failed: {ExceptionType}", e.GetType().Name);
            }
        }
        session.Cancel();
    }

    private async Task<InterviewTiles> BuildTilesAsync(InterviewSession session, InterviewResult result, InterviewModel model, ModelMeter meter)
    {
        var notice = InterviewNotice.For(session.Language);
        try
        {
            var generator = new TileGenerator(new ModelTileWriter(new MeteredChatClient(model.Client, meter)), new TileGuard(new PiiGuard()));
            var set = await generator.GenerateAsync(new TileInput(result.Record!, result.Transcript?.Render()), session.Cancellation.Token).ConfigureAwait(false);
            return new InterviewTiles(
                set.Tiles.Select(t => new InterviewTile(InterviewSession.ToSnake(t.Kind.ToString()), t.Text)).ToList(),
                set.Dropped.Select(d => new InterviewDroppedTile(d.ReasonCode)).ToList(),
                notice);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning("Tiles unavailable: {ExceptionType}", e.GetType().Name);
            return new InterviewTiles([], [new InterviewDroppedTile("tiles_unavailable")], notice);
        }
    }

    private static string NewId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Maps between the wire words of the contract and the agent's enums.</summary>
public static class InterviewMapping
{
    public static bool TryParseLanguage(string? value, out string language)
    {
        language = value ?? string.Empty;
        return value is "pl" or "en";
    }

    public static bool TryParseTenure(string? value, out TenureBand band)
    {
        band = value switch
        {
            "lt_6m" => TenureBand.LessThan6Months,
            "6m_1y" => TenureBand.SixToTwelveMonths,
            "1y_3y" => TenureBand.OneToThreeYears,
            "3y_5y" => TenureBand.ThreeToFiveYears,
            "5y_10y" => TenureBand.FiveToTenYears,
            "gt_10y" => TenureBand.OverTenYears,
            _ => default,
        };
        return value is "lt_6m" or "6m_1y" or "1y_3y" or "3y_5y" or "5y_10y" or "gt_10y";
    }
}
