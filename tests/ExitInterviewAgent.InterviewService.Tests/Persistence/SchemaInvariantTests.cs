using ExitInterviewAgent.InterviewService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ExitInterviewAgent.InterviewService.Tests.Persistence;

/// <summary>The one place the permitted columns are written down; a new column must be added here, in review, on purpose.</summary>
public static class SchemaGolden
{
    public static readonly IReadOnlyDictionary<string, string[]> Columns = new Dictionary<string, string[]>
    {
        ["Records"] = ["Id", "EmployerRef", "Json", "Verification", "CreatedWeek"],
        ["SubmissionLedger"] = ["Id", "KeyId", "Tag", "CreatedWeek"],
        ["Receipts"] = ["Id", "CodeHash", "RecordId"],
        ["SubmissionTickets"] = ["Id", "TokenHash", "Sub", "ExpiresAt"],
        ["CreditEntries"] = ["Id", "AccountRef", "Delta", "Reason", "Reference", "CreatedWeek", "StartedHour"],
        ["PaymentEvents"] = ["Id", "ProviderEventId", "Kind", "AccountRef", "Quantity", "AmountMinorUnits", "Currency", "ReceivedWeek"],
        ["SessionSettlements"] = ["Id", "SessionId", "AccountRef", "Outcome", "SettledWeek"],
    };

    /// <summary>The only columns that hold an account: the ticket's subject (short-lived), the credit and payment rows (W3), and the settlement row (W11).</summary>
    public static readonly string[] AccountHolders = ["SubmissionTickets.Sub", "CreditEntries.AccountRef", "PaymentEvents.AccountRef", "SessionSettlements.AccountRef"];

    /// <summary>Columns that hold a value derived from an account subject, or the subject itself.</summary>
    public static readonly string[] SubjectDerived = ["SubmissionLedger.Tag", "SubmissionLedger.KeyId", "SubmissionTickets.Sub", "CreditEntries.AccountRef", "PaymentEvents.AccountRef", "SessionSettlements.AccountRef"];

    /// <summary>
    /// The one timestamp in the credit ledger (W11): the hour a consume started, set on consume rows only. It is an hour, not a
    /// day or a week, because the startup sweep needs to know a session is older than its idle window; the hour is the coarsest
    /// bucket that keeps that test to within an hour. Its slack is recorded in ADR-0077.
    /// </summary>
    public static readonly string[] HourBuckets = ["CreditEntries.StartedHour"];

    /// <summary>Columns that identify, or can be joined to, a stored record.</summary>
    public static readonly string[] RecordIdentifying =
        ["Records.Id", "Records.EmployerRef", "Records.Json", "Records.Verification", "Records.CreatedWeek", "Receipts.RecordId", "Receipts.CodeHash", "Receipts.Id"];
}

/// <summary>
/// The privacy invariants of the table layout, checked against the EF model (and, in the PostgreSQL tests, against the
/// migrated database): no account column anywhere except the short-lived ticket row; the ledger holds no record
/// reference; keys are random, never sequential; stored timestamps are coarse buckets (docs/architecture/submission-flow.md).
/// </summary>
public sealed class SchemaInvariantTests
{
    private static IModel Model()
    {
        using var db = new InterviewDbContext(new DbContextOptionsBuilder<InterviewDbContext>().UseInMemoryDatabase("schema-" + Guid.NewGuid()).Options);
        return db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
    }

    private static IEnumerable<(string Table, IProperty Property)> Columns() =>
        Model().GetEntityTypes().SelectMany(e => e.GetProperties().Select(p => (e.GetTableName()!, p)));

    [Fact]
    public void The_tables_have_exactly_the_documented_columns()
    {
        var actual = Model().GetEntityTypes().ToDictionary(e => e.GetTableName()!, e => e.GetProperties().Select(p => p.GetColumnName()).Order().ToArray());

        Assert.Equal(SchemaGolden.Columns.Keys.Order(), actual.Keys.Order());
        foreach (var (table, columns) in SchemaGolden.Columns)
        {
            Assert.Equal(columns.Order(), actual[table]);
        }
    }

    [Fact]
    public void Only_the_named_tables_hold_an_account_column()
    {
        // Credits and payments belong to an account by definition (W3); the record, receipt and ledger tables stay account-free.
        Assert.Equal(["CreditEntries.AccountRef", "PaymentEvents.AccountRef", "SessionSettlements.AccountRef", "SubmissionTickets.Sub"], SchemaGolden.AccountHolders.Order().ToArray());
    }

    [Fact]
    public void No_column_names_an_account_except_the_ticket_row()
    {
        var banned = new[] { "sub", "subject", "user", "account", "email", "principal", "owner", "client", "ip" };

        foreach (var (table, property) in Columns())
        {
            var name = property.GetColumnName().ToLowerInvariant();
            var hit = banned.Any(b => name == b || name.StartsWith(b, StringComparison.Ordinal) && name.Length > b.Length && char.IsUpper(property.GetColumnName()[b.Length]));
            if (hit)
            {
                Assert.True(SchemaGolden.AccountHolders.Contains($"{table}.{property.GetColumnName()}"), $"{table}.{property.GetColumnName()} looks like an account column");
            }
        }
    }

    [Fact]
    public void No_table_holds_both_something_derived_from_an_account_and_something_that_identifies_a_record()
    {
        foreach (var (table, columns) in SchemaGolden.Columns)
        {
            var qualified = columns.Select(c => $"{table}.{c}").ToArray();
            var subject = qualified.Intersect(SchemaGolden.SubjectDerived).Any();
            var record = qualified.Intersect(SchemaGolden.RecordIdentifying).Any();
            Assert.False(subject && record, $"{table} holds both an account-derived value and a record-identifying one");
        }
    }

    [Fact]
    public void Every_column_is_classified_so_a_new_one_cannot_slip_past_the_rule_above()
    {
        var known = SchemaGolden.SubjectDerived.Concat(SchemaGolden.RecordIdentifying)
            .Concat(["SubmissionLedger.Id", "SubmissionLedger.CreatedWeek", "SubmissionTickets.Id", "SubmissionTickets.TokenHash", "SubmissionTickets.ExpiresAt"])
            .Concat(["CreditEntries.Id", "CreditEntries.Delta", "CreditEntries.Reason", "CreditEntries.Reference", "CreditEntries.CreatedWeek", "CreditEntries.StartedHour"])
            .Concat(["SessionSettlements.Id", "SessionSettlements.SessionId", "SessionSettlements.Outcome", "SessionSettlements.SettledWeek"])
            .Concat(["PaymentEvents.Id", "PaymentEvents.ProviderEventId", "PaymentEvents.Kind", "PaymentEvents.Quantity", "PaymentEvents.AmountMinorUnits", "PaymentEvents.Currency", "PaymentEvents.ReceivedWeek"]);

        var all = SchemaGolden.Columns.SelectMany(t => t.Value.Select(c => $"{t.Key}.{c}"));

        Assert.Empty(all.Except(known));
    }

    [Fact]
    public void The_ledger_has_no_record_receipt_or_employer_column_and_no_relationship_in_either_direction()
    {
        var ledger = Model().FindEntityType(typeof(LedgerEntry))!;

        Assert.Empty(ledger.GetForeignKeys());
        Assert.Empty(ledger.GetReferencingForeignKeys());
        Assert.Empty(ledger.GetNavigations());
        foreach (var p in ledger.GetProperties())
        {
            var name = p.GetColumnName().ToLowerInvariant();
            Assert.DoesNotContain("record", name);
            Assert.DoesNotContain("interview", name);
            Assert.DoesNotContain("receipt", name);
            Assert.DoesNotContain("employer", name);
        }
    }

    [Fact]
    public void Receipts_and_records_carry_no_account_derived_value_and_tickets_carry_no_record_or_employer()
    {
        var receipts = SchemaGolden.Columns["Receipts"].Concat(SchemaGolden.Columns["Records"]).Select(c => c.ToLowerInvariant());
        Assert.DoesNotContain(receipts, c => c is "sub" or "tag" or "keyid" or "ticket" or "tokenhash");

        var tickets = SchemaGolden.Columns["SubmissionTickets"].Select(c => c.ToLowerInvariant());
        Assert.DoesNotContain(tickets, c => c.Contains("record") || c.Contains("employer") || c.Contains("interview") || c.Contains("receipt"));
    }

    [Fact]
    public void Keys_are_random_never_database_generated_sequences_or_counters()
    {
        var model = Model();
        Assert.Empty(model.GetSequences());
        foreach (var entity in model.GetEntityTypes())
        {
            var key = Assert.Single(entity.FindPrimaryKey()!.Properties);
            Assert.Equal(ValueGenerated.Never, key.ValueGenerated);
            Assert.True(key.ClrType == typeof(Guid) || key.ClrType == typeof(string), $"{entity.GetTableName()} key is {key.ClrType.Name}: a counter is a join key");
        }
    }

    [Fact]
    public void Stored_timestamps_are_week_buckets_except_the_ticket_expiry()
    {
        foreach (var (table, property) in Columns())
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            {
                Assert.Contains($"{table}.{property.GetColumnName()}", new[] { "SubmissionTickets.ExpiresAt" }.Concat(SchemaGolden.HourBuckets));
            }
        }
        Assert.Equal(typeof(DateOnly), Model().FindEntityType(typeof(RecordRow))!.FindProperty(nameof(RecordRow.CreatedWeek))!.ClrType);
        Assert.Equal(typeof(DateOnly), Model().FindEntityType(typeof(LedgerEntry))!.FindProperty(nameof(LedgerEntry.CreatedWeek))!.ClrType);
    }

    [Fact]
    public void The_ledger_tag_is_unique_so_the_database_decides_a_race()
    {
        var index = Model().FindEntityType(typeof(LedgerEntry))!.GetIndexes().Single(i => i.Properties.Single().Name == nameof(LedgerEntry.Tag));

        Assert.True(index.IsUnique);
        Assert.Equal(InterviewDbContext.LedgerTagIndex, index.GetDatabaseName());
    }
}
