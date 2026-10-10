using System.Data;
using ExitInterviewAgent.InterviewService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// The credit ledger (web-app-plan §4, ADR-0077): an append-only table of +N purchases, -1 consumes and +1 refunds. The balance is
/// the sum of an account's rows; nothing is updated in place. Three rules are held by the database and by this class together:
/// <list type="bullet">
/// <item>a provider event adds its credits once (a row per event id, and the event table's unique index);</item>
/// <item>a session takes one credit once, and its settlement (one row per session, W11) is written once: a failed or lost session
/// gets its credit back in the same transaction as its settlement row, and no other outcome refunds;</item>
/// <item>a credit is never spent below zero, even under parallel requests.</item>
/// </list>
/// Every write takes the process lock, so one instance cannot race itself. On PostgreSQL each write also runs in a SERIALIZABLE
/// transaction and is retried on a serialization failure, so a second instance cannot either. Interview text never reaches this class.
/// </summary>
public sealed class CreditLedger(IServiceScopeFactory scopes, TimeProvider clock)
{
    private const int MaxAttempts = 5;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The account's balance. A read: it needs no lock, and it is the sum of committed rows.</summary>
    public async Task<int> BalanceAsync(string account, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        return await SumAsync(db, account, ct);
    }

    /// <summary>
    /// Takes one credit for a session. False when the balance is empty, or when this session has already taken its credit
    /// (a second call never charges twice). Nothing is written on false.
    /// </summary>
    public Task<bool> ConsumeAsync(string account, string sessionId, CancellationToken ct) =>
        WriteAsync(async db =>
        {
            if (await db.CreditEntries.AnyAsync(c => c.Reason == CreditReason.Consume && c.Reference == sessionId, ct)) return false;
            if (await SumAsync(db, account, ct) < 1) return false;
            db.CreditEntries.Add(Entry(account, -1, CreditReason.Consume, sessionId, StartOfHour(clock.GetUtcNow())));
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    /// <summary>
    /// Settles a session once (W11): writes its settlement row and, for <see cref="SessionOutcome.Failed"/> and
    /// <see cref="SessionOutcome.Lost"/>, returns its credit (when it took one and has not been refunded). Both writes are one
    /// transaction, so a credit is never returned without its settlement or the reverse. Returns true only for the call that wrote the
    /// settlement; every later call for the same session returns false and writes nothing.
    /// </summary>
    public async Task<bool> SettleAsync(string account, string sessionId, SessionOutcome outcome, CancellationToken ct)
    {
        try
        {
            return await WriteAsync(async db =>
            {
                if (await db.SessionSettlements.AnyAsync(s => s.SessionId == sessionId, ct)) return false;
                db.SessionSettlements.Add(new SessionSettlementRow
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    AccountRef = account,
                    Outcome = outcome,
                    SettledWeek = WeekOf(clock.GetUtcNow()),
                });
                if (RefundReasonFor(outcome) is { } reason
                    && await db.CreditEntries.AnyAsync(c => c.Reason == CreditReason.Consume && c.Reference == sessionId, ct)
                    && !await db.CreditEntries.AnyAsync(c => (c.Reason == CreditReason.Refund || c.Reason == CreditReason.RefundLostSession) && c.Reference == sessionId, ct))
                {
                    db.CreditEntries.Add(Entry(account, +1, reason, sessionId));
                }
                await db.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent settlement won the unique index (another request or another instance): this one is the replay.
            if (await IsSettledAsync(sessionId, ct)) return false;
            throw;
        }
    }

    /// <summary>
    /// The consumes that have no settlement yet and started in an hour at or before <paramref name="startedBefore"/>, excluding the
    /// sessions this process still holds. The startup sweep reads these; the settlement call decides each one again.
    /// </summary>
    public async Task<IReadOnlyList<UnsettledConsume>> UnsettledConsumesAsync(DateTimeOffset startedBefore, IReadOnlyCollection<string> exclude, int take, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        var excluded = exclude.ToArray();
        return await db.CreditEntries
            .Where(c => c.Reason == CreditReason.Consume && c.StartedHour != null && c.StartedHour <= startedBefore
                && !excluded.Contains(c.Reference) && !db.SessionSettlements.Any(s => s.SessionId == c.Reference))
            .OrderBy(c => c.StartedHour)
            .Take(take)
            .Select(c => new UnsettledConsume(c.AccountRef, c.Reference!))
            .ToListAsync(ct);
    }

    private async Task<bool> IsSettledAsync(string sessionId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        return await db.SessionSettlements.AnyAsync(s => s.SessionId == sessionId, ct);
    }

    /// <summary>
    /// Records a verified purchase. The first delivery of a provider event adds its credits; every later delivery of the same event
    /// is a <see cref="PurchaseOutcome.Replay"/> and adds nothing.
    /// </summary>
    public async Task<PurchaseOutcome> RecordPurchaseAsync(PaymentEvent evt, CancellationToken ct)
    {
        if (evt.Kind != PaymentEventKind.Purchase || evt.AccountRef is null || evt.Currency is null || evt.Quantity < 1)
        {
            throw new ArgumentException("Only a purchase with an account, a currency and a quantity can be recorded.", nameof(evt));
        }

        try
        {
            return await WriteAsync(async db =>
            {
                if (await db.PaymentEvents.AnyAsync(p => p.ProviderEventId == evt.ProviderEventId, ct)) return PurchaseOutcome.Replay;
                var week = WeekOf(clock.GetUtcNow());
                db.PaymentEvents.Add(new PaymentEventRow
                {
                    Id = Guid.NewGuid(),
                    ProviderEventId = evt.ProviderEventId,
                    Kind = "purchase",
                    AccountRef = evt.AccountRef,
                    Quantity = evt.Quantity,
                    AmountMinorUnits = evt.AmountMinorUnits,
                    Currency = evt.Currency,
                    ReceivedWeek = week,
                });
                db.CreditEntries.Add(Entry(evt.AccountRef, evt.Quantity, CreditReason.Purchase, evt.ProviderEventId));
                await db.SaveChangesAsync(ct);
                return PurchaseOutcome.Applied;
            }, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent delivery won the unique index: this one is the replay. Any other write failure is rethrown.
            if (await IsKnownAsync(evt.ProviderEventId, ct)) return PurchaseOutcome.Replay;
            throw;
        }
    }

    private async Task<bool> IsKnownAsync(string providerEventId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        return await db.PaymentEvents.AnyAsync(p => p.ProviderEventId == providerEventId, ct);
    }

    private static async Task<int> SumAsync(InterviewDbContext db, string account, CancellationToken ct) =>
        await db.CreditEntries.Where(c => c.AccountRef == account).SumAsync(c => (int?)c.Delta, ct) ?? 0;

    /// <summary>
    /// Runs one write under the process lock. On a relational provider the work is one SERIALIZABLE transaction, retried on a
    /// serialization failure (PostgreSQL SQLSTATE 40001) with a fresh context, so the retry sees the committed rows.
    /// </summary>
    private async Task<T> WriteAsync<T>(Func<InterviewDbContext, Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await RunOnceAsync(work, ct);
                }
                catch (Exception e) when (attempt < MaxAttempts && IsSerializationFailure(e))
                {
                    // Another writer committed first: the next attempt reads its rows and decides again.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> RunOnceAsync<T>(Func<InterviewDbContext, Task<T>> work, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InterviewDbContext>();
        if (!db.Database.IsRelational()) return await work(db);

        // The Npgsql provider retries transient failures itself, and it refuses a user transaction outside its strategy.
        // Each attempt starts with an empty tracker, so a retried attempt never re-adds rows the failed one tracked.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var result = await work(db);
            await transaction.CommitAsync(token);
            return result;
        }, ct);
    }

    private static bool IsSerializationFailure(Exception e)
    {
        for (Exception? current = e; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "40001" }) return true;
        }
        return false;
    }

    /// <summary>The refund a settlement carries, if any: a failed session returns its credit, a lost one too; the rest keep it.</summary>
    private static CreditReason? RefundReasonFor(SessionOutcome outcome) => outcome switch
    {
        SessionOutcome.Failed => CreditReason.Refund,
        SessionOutcome.Lost => CreditReason.RefundLostSession,
        _ => null,
    };

    private CreditEntry Entry(string account, int delta, CreditReason reason, string reference, DateTimeOffset? startedHour = null) => new()
    {
        Id = Guid.NewGuid(),
        AccountRef = account,
        Delta = delta,
        Reason = reason,
        Reference = reference,
        CreatedWeek = WeekOf(clock.GetUtcNow()),
        StartedHour = startedHour,
    };

    /// <summary>The start of the UTC hour: the only time of day a consume row keeps (W11, see <see cref="CreditEntry.StartedHour"/>).</summary>
    private static DateTimeOffset StartOfHour(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Monday of the ISO week (ADR-0027): the only time the ledger keeps.</summary>
    private static DateOnly WeekOf(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }
}

/// <summary>A consume with no settlement yet: the account it was taken from and the session it was taken for (W11).</summary>
public sealed record UnsettledConsume(string AccountRef, string SessionId);
