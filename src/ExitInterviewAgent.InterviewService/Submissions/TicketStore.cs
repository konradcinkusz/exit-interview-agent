using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>A ticket as found by a read-only look: its row id and the account it was minted for. Held in memory only.</summary>
public sealed record TicketPeek(Guid Id, string Sub);

/// <summary>
/// CLI submission tickets (brief section 4, ADR-0030): random 256 bits, single use, not bound to an employer, short lived.
/// The server keeps the SHA-256 of the ticket and the account subject until the ticket is redeemed; redemption deletes the
/// row. This is the only place an account subject is stored.
/// </summary>
public sealed class TicketStore(InterviewDbContext db, DatabaseMode mode, TimeProvider time, IOptions<SubmissionOptions> options)
{
    private TicketSettings Settings => options.Value.Tickets;

    /// <summary>Mints a ticket, or returns null when the account already holds the maximum number of live tickets.</summary>
    public async Task<(string Ticket, DateTimeOffset ExpiresAt)?> MintAsync(string sub, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var live = await db.SubmissionTickets.CountAsync(t => t.Sub == sub && t.ExpiresAt > now, ct);
        if (live >= Settings.MaxOutstandingPerAccount)
        {
            return null;
        }
        var ticket = SecretTokens.NewTicket();
        var expiresAt = RoundUp(now.AddMinutes(Settings.TtlMinutes), TimeSpan.FromMinutes(Math.Max(1, Settings.ExpiryGranularityMinutes)));
        db.SubmissionTickets.Add(new TicketRow { Id = Guid.NewGuid(), TokenHash = SecretTokens.Hash(ticket), Sub = sub, ExpiresAt = expiresAt });
        await db.SaveChangesAsync(ct);
        return (ticket, expiresAt);
    }

    /// <summary>Read-only: does this presented ticket name a live row? Does not consume it.</summary>
    public async Task<TicketPeek?> PeekAsync(string presented, CancellationToken ct)
    {
        if (!SecretTokens.IsWellFormedTicket(presented))
        {
            return null;
        }
        var hash = SecretTokens.Hash(presented);
        var now = time.GetUtcNow();
        var row = await db.SubmissionTickets.AsNoTracking().Where(t => t.TokenHash == hash && t.ExpiresAt > now)
            .Select(t => new { t.Id, t.TokenHash, t.Sub }).SingleOrDefaultAsync(ct);
        return row is not null && SecretTokens.HashesEqual(row.TokenHash, hash) ? new TicketPeek(row.Id, row.Sub) : null;
    }

    /// <summary>
    /// Consumes the ticket: returns its account subject exactly once. Under a race one caller gets the subject and every
    /// other gets null, because the delete is a single atomic statement whose affected-row count decides the winner.
    /// Call inside the submission's transaction so a rejected submission leaves the ticket usable.
    /// </summary>
    public async Task<string?> RedeemAsync(TicketPeek peek, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (mode.IsRelational)
        {
            var deleted = await db.SubmissionTickets.Where(t => t.Id == peek.Id && t.ExpiresAt > now).ExecuteDeleteAsync(ct);
            return deleted == 1 ? peek.Sub : null;
        }
        var row = await db.SubmissionTickets.FindAsync([peek.Id], ct);
        if (row is null || row.ExpiresAt <= now)
        {
            return null;
        }
        db.SubmissionTickets.Remove(row);
        await db.SaveChangesAsync(ct);
        return row.Sub;
    }

    internal static DateTimeOffset RoundUp(DateTimeOffset value, TimeSpan step)
    {
        var ticks = (value.UtcTicks + step.Ticks - 1) / step.Ticks * step.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
