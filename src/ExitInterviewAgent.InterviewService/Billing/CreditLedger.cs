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
/// <item>a session takes one credit once and a failed session gives it back once (unique on reason and session id);</item>
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
            db.CreditEntries.Add(Entry(account, -1, CreditReason.Consume, sessionId));
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    /// <summary>
    /// Gives a session's credit back, once, and only when that session took one. A second call, or a call for a session that never
    /// took a credit, writes nothing.
    /// </summary>
    public async Task RefundAsync(string account, string sessionId, CancellationToken ct)
    {
        await WriteAsync(async db =>
        {
            if (!await db.CreditEntries.AnyAsync(c => c.Reason == CreditReason.Consume && c.Reference == sessionId, ct)) return false;
            if (await db.CreditEntries.AnyAsync(c => c.Reason == CreditReason.Refund && c.Reference == sessionId, ct)) return false;
            db.CreditEntries.Add(Entry(account, +1, CreditReason.Refund, sessionId));
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
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

    private CreditEntry Entry(string account, int delta, CreditReason reason, string reference) => new()
    {
        Id = Guid.NewGuid(),
        AccountRef = account,
        Delta = delta,
        Reason = reason,
        Reference = reference,
        CreatedWeek = WeekOf(clock.GetUtcNow()),
    };

    /// <summary>Monday of the ISO week (ADR-0027): the only time the ledger keeps.</summary>
    private static DateOnly WeekOf(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }
}
