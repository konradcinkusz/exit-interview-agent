using Microsoft.EntityFrameworkCore;

namespace ExitInterviewAgent.Signals.Persistence;

/// <summary>
/// The module's own context, in its own schema (<c>signals</c>) with its own migration history (ADR-0052). It holds published
/// aggregates only: no record, no quote, no interview id, no band of any single record. A table that carried those would be a
/// second record store with weaker controls.
/// </summary>
public sealed class SignalsDbContext(DbContextOptions<SignalsDbContext> options) : DbContext(options)
{
    public const string Schema = "signals";

    public DbSet<SnapshotRow> Snapshots => Set<SnapshotRow>();
    public DbSet<EmployerSnapshotRow> EmployerSnapshots => Set<EmployerSnapshotRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);

        model.Entity<SnapshotRow>(e =>
        {
            e.ToTable("Snapshots");
            e.HasKey(s => s.Seq);
            e.Property(s => s.Seq).ValueGeneratedNever();
            e.Property(s => s.Fingerprint).HasMaxLength(96);
            e.Property(s => s.RulesVersion).HasMaxLength(16);
            e.HasIndex(s => s.Id).IsUnique();
            // The one-publication-per-period rule: the database decides a race between two instances.
            e.HasIndex(s => s.Fingerprint).IsUnique();
        });

        model.Entity<EmployerSnapshotRow>(e =>
        {
            e.ToTable("EmployerSnapshots");
            e.HasKey(r => new { r.SnapshotId, r.EmployerRef });
            e.Property(r => r.EmployerRef).HasMaxLength(64);
            e.Property(r => r.View).IsRequired();
        });
    }
}
