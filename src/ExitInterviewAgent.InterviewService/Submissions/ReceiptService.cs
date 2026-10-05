using System.Diagnostics;
using ExitInterviewAgent.InterviewService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// Deletion by receipt code (ADR-0029). The code is a bearer secret with no account link. Semantics: a well-formed code
/// always gets the same answer whether or not a record was found ("if it exists, it is deleted now"), and every call takes
/// at least the configured floor, so neither the response nor the latency says whether a code is live. A malformed
/// code is a different, public answer: the checksum is computable by anyone and says nothing about stored codes.
/// </summary>
public sealed class ReceiptService(InterviewDbContext db, StoreGate gate, TimeProvider time, IOptions<SubmissionOptions> options)
{
    /// <returns>false when the code is malformed (a typo); true for every well-formed code, found or not.</returns>
    public async Task<bool> DeleteAsync(string? presentedCode, CancellationToken ct)
    {
        if (!ReceiptCodes.IsWellFormed(presentedCode))
        {
            return false;
        }

        var started = time.GetTimestamp();
        var hash = SecretTokens.Hash(presentedCode!);
        using (await gate.EnterAsync(ct))
        {
            var receipt = await db.Receipts.Where(r => r.CodeHash == hash).FirstOrDefaultAsync(ct);
            // The index finds the row by the hash of the presented code; the comparison below is still constant time.
            if (receipt is not null && SecretTokens.HashesEqual(receipt.CodeHash, hash))
            {
                var record = await db.Records.FindAsync([receipt.RecordId], ct);
                db.Receipts.Remove(receipt);
                if (record is not null)
                {
                    db.Records.Remove(record);
                }
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // A concurrent deletion of the same code won: the outcome is the same, and the answer must be too.
                }
                db.ChangeTracker.Clear();
            }
        }

        var floor = TimeSpan.FromMilliseconds(options.Value.Receipts.ResponseFloorMilliseconds);
        var remaining = floor - time.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, time, ct);
        }
        return true;
    }
}
