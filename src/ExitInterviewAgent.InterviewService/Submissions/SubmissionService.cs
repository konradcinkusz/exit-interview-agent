using System.Diagnostics.Metrics;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.Privacy;
using ExitInterviewAgent.Records;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// The one submission implementation: the web endpoint, the MCP tool (T8) and the ticketed CLI endpoint all call it.
/// Input is the raw record bytes and the account subject (or a ticket that resolves to one); output is a receipt code,
/// shown once, or a stable rejection. Pipeline, in this order: size, schema validation, server-side PII re-scan,
/// AI disclosure, employer verification (degrades), then ONE transaction that checks the ledger and writes the ledger
/// entry, the record and the receipt (and, for a ticket, deletes the ticket row). Nothing here logs content, an account
/// subject, a ticket or a receipt code (T-15); a rejection is counted by code only.
/// </summary>
public sealed class SubmissionService(
    InterviewDbContext db,
    DatabaseMode mode,
    StoreGate gate,
    RecordValidator validator,
    IPiiScanner pii,
    IEmploymentVerifier verifier,
    LedgerKeySet ledgerKeys,
    TicketStore tickets,
    TimeProvider time,
    IOptions<SubmissionOptions> options,
    SubmissionMetrics metrics)
{
    /// <summary>Submit as an authenticated account (web token, or an MCP token: <c>sub</c> is the same user id on both).</summary>
    public async Task<SubmissionOutcome> SubmitAsync(ReadOnlyMemory<byte> recordJson, string sub, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        return metrics.Count(await RunAsync(recordJson, sub, null, ct));
    }

    /// <summary>Submit with a CLI ticket (anonymous call): the ticket resolves to the account subject, and its row is deleted in the same transaction.</summary>
    public async Task<SubmissionOutcome> SubmitWithTicketAsync(ReadOnlyMemory<byte> recordJson, string? presentedTicket, CancellationToken ct)
    {
        var peek = presentedTicket is null ? null : await tickets.PeekAsync(presentedTicket, ct);
        return metrics.Count(peek is null
            ? SubmissionOutcome.Reject(SubmissionCodes.TicketInvalid)
            : await RunAsync(recordJson, peek.Sub, peek, ct));
    }

    private async Task<SubmissionOutcome> RunAsync(ReadOnlyMemory<byte> body, string sub, TicketPeek? ticket, CancellationToken ct)
    {
        if (body.Length > RecordLimits.Default.MaxPayloadBytes)
        {
            return SubmissionOutcome.Reject(SubmissionCodes.PayloadTooLarge);
        }

        var validation = validator.Validate(body.Span);
        if (!validation.IsValid)
        {
            return SubmissionOutcome.Reject(new Rejection(
                validation.Errors.FirstOrDefault()?.Code ?? "SCHEMA_VIOLATION",
                [.. validation.Errors.Select(e => new FieldError(e.Code, e.Path))], []));
        }
        var record = validation.Record!;

        var scan = await pii.ScanAsync(record, ct);
        if (scan.Failed)
        {
            return SubmissionOutcome.Reject(SubmissionCodes.PiiCheckFailed);
        }
        if (scan.Kinds.Count > 0)
        {
            return SubmissionOutcome.Reject(new Rejection(SubmissionCodes.PiiDetected, [], [.. scan.Kinds.Select(k => k.ToString())]));
        }

        if (!record.Interview.AiDisclosed)
        {
            return SubmissionOutcome.Reject(new Rejection(SubmissionCodes.AiNotDisclosed, [new FieldError(SubmissionCodes.AiNotDisclosed, "/interview/aiDisclosed")], []));
        }

        var level = await VerifyAsync(sub, record.EmployerRef, ct);
        if (level == VerificationLevel.Unverified && options.Value.Verification.RejectUnverified)
        {
            return SubmissionOutcome.Reject(SubmissionCodes.EmploymentNotVerified);
        }

        return await PersistAsync(record, sub, ticket, level, ct);
    }

    /// <summary>A verifier that is down, slow or broken must not break submission (P8): the record is stored as <c>Unchecked</c>.</summary>
    private async Task<VerificationLevel> VerifyAsync(string sub, string employerRef, CancellationToken ct)
    {
        try
        {
            var check = await verifier.VerifyAsync(sub, employerRef, ct)
                .WaitAsync(TimeSpan.FromMilliseconds(options.Value.Verification.TimeoutMilliseconds), ct);
            return check == EmploymentCheck.Verified ? VerificationLevel.Verified : VerificationLevel.Unverified;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return VerificationLevel.Unchecked;
        }
    }

    private async Task<SubmissionOutcome> PersistAsync(InterviewRecord record, string sub, TicketPeek? ticket, VerificationLevel level, CancellationToken ct)
    {
        var json = RecordSerializer.SerializeCanonicalString(record);
        var week = TimeBuckets.WeekStart(time.GetUtcNow());
        var receiptCode = ReceiptCodes.NewCode();

        using var _ = await gate.EnterAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = mode.IsRelational ? await db.Database.BeginTransactionAsync(ct) : null;

            var subject = sub;
            if (ticket is not null)
            {
                subject = await tickets.RedeemAsync(ticket, ct);
                if (subject is null)
                {
                    return SubmissionOutcome.Reject(SubmissionCodes.TicketInvalid);
                }
            }

            foreach (var tag in ledgerKeys.AllTags(subject, record.EmployerRef))
            {
                if (await db.SubmissionLedger.AnyAsync(l => l.Tag == tag, ct))
                {
                    return SubmissionOutcome.Reject(SubmissionCodes.AlreadySubmitted); // rolls back: a ticket stays usable
                }
            }

            db.SubmissionLedger.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                KeyId = ledgerKeys.ActiveKeyId,
                Tag = ledgerKeys.ActiveTag(subject, record.EmployerRef),
                CreatedWeek = week,
            });
            db.Records.Add(new RecordRow
            {
                Id = record.InterviewId.Value,
                EmployerRef = record.EmployerRef,
                Json = json,
                Verification = level,
                CreatedWeek = week,
            });
            db.Receipts.Add(new ReceiptRow { Id = Guid.NewGuid(), CodeHash = SecretTokens.Hash(receiptCode), RecordId = record.InterviewId.Value });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsLedgerConflict(ex))
            {
                return SubmissionOutcome.Reject(SubmissionCodes.AlreadySubmitted);
            }
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
            return SubmissionOutcome.Accept(receiptCode);
        });
    }

    private static bool IsLedgerConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: InterviewDbContext.LedgerTagIndex };
}

/// <summary>
/// Outcome counter. Labels come from a fixed vocabulary (accepted, or a rejection code): no employer, account, ticket or
/// receipt is ever a label, and the canary test asserts it.
/// </summary>
public sealed class SubmissionMetrics
{
    public const string MeterName = "ExitInterviewAgent.Submissions";
    private readonly Counter<long> _outcomes;

    public SubmissionMetrics(IMeterFactory meters) =>
        _outcomes = meters.Create(MeterName).CreateCounter<long>("submissions.outcomes");

    public SubmissionOutcome Count(SubmissionOutcome outcome)
    {
        _outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome.Accepted ? "accepted" : outcome.Rejection!.Code));
        return outcome;
    }
}
