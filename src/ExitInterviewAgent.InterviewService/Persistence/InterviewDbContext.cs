using Microsoft.EntityFrameworkCore;

namespace ExitInterviewAgent.InterviewService.Persistence;

/// <summary>
/// The interview-service's database (P3: owned by this service, opened by nothing else). Table layout and what each
/// table can and cannot link: docs/architecture/submission-flow.md. Keys of the ledger, receipts and tickets are random
/// (never sequential) so insertion order is not a join key; <c>SchemaInvariantTests</c> pins the column sets.
/// </summary>
public sealed class InterviewDbContext(DbContextOptions<InterviewDbContext> options) : DbContext(options)
{
    public DbSet<RecordRow> Records => Set<RecordRow>();
    public DbSet<LedgerEntry> SubmissionLedger => Set<LedgerEntry>();
    public DbSet<ReceiptRow> Receipts => Set<ReceiptRow>();
    public DbSet<TicketRow> SubmissionTickets => Set<TicketRow>();
    public DbSet<CreditEntry> CreditEntries => Set<CreditEntry>();
    public DbSet<PaymentEventRow> PaymentEvents => Set<PaymentEventRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CreditEntry>(e =>
        {
            e.ToTable("CreditEntries");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.AccountRef).HasMaxLength(256).IsRequired();
            e.Property(c => c.Reason).HasConversion<string>().HasMaxLength(16);
            e.Property(c => c.Reference).HasMaxLength(128);
            e.HasIndex(c => c.AccountRef);
            // One purchase per payment event, one consume and one refund per session: the database decides a race.
            e.HasIndex(c => new { c.Reason, c.Reference }).IsUnique().HasDatabaseName(CreditReferenceIndex);
        });

        model.Entity<PaymentEventRow>(e =>
        {
            e.ToTable("PaymentEvents");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.ProviderEventId).HasMaxLength(128).IsRequired();
            e.Property(p => p.Kind).HasMaxLength(32).IsRequired();
            e.Property(p => p.AccountRef).HasMaxLength(256).IsRequired();
            e.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            // Idempotency by provider event id (web-app-plan §4): a replay cannot add a second purchase.
            e.HasIndex(p => p.ProviderEventId).IsUnique().HasDatabaseName(PaymentEventIndex);
        });

        model.Entity<RecordRow>(e =>
        {
            e.ToTable("Records");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasMaxLength(32).ValueGeneratedNever();
            e.Property(r => r.EmployerRef).HasMaxLength(64);
            e.Property(r => r.Json).IsRequired();
            e.Property(r => r.Verification).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(r => r.CreatedWeek);
            e.HasIndex(r => r.EmployerRef);
        });

        model.Entity<LedgerEntry>(e =>
        {
            e.ToTable("SubmissionLedger");
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedNever();
            e.Property(l => l.KeyId).HasMaxLength(32);
            e.Property(l => l.Tag).HasMaxLength(64);
            // The one-per-employer-per-account rule: the database, not the application, decides a race.
            e.HasIndex(l => l.Tag).IsUnique().HasDatabaseName(LedgerTagIndex);
            e.HasIndex(l => l.CreatedWeek);
        });

        model.Entity<ReceiptRow>(e =>
        {
            e.ToTable("Receipts");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.CodeHash).HasMaxLength(64);
            e.Property(r => r.RecordId).HasMaxLength(32);
            e.HasIndex(r => r.CodeHash).IsUnique();
            e.HasIndex(r => r.RecordId);
            e.HasOne<RecordRow>().WithMany().HasForeignKey(r => r.RecordId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<TicketRow>(e =>
        {
            e.ToTable("SubmissionTickets");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.Property(t => t.Sub).HasMaxLength(256);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.ExpiresAt);
            e.HasIndex(t => t.Sub);
        });
    }

    public const string LedgerTagIndex = "IX_SubmissionLedger_Tag";
    public const string CreditReferenceIndex = "IX_CreditEntries_Reason_Reference";
    public const string PaymentEventIndex = "IX_PaymentEvents_ProviderEventId";
}
