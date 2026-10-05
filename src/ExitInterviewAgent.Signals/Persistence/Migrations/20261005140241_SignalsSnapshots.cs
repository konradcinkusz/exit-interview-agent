using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExitInterviewAgent.Signals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignalsSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "signals");

            migrationBuilder.CreateTable(
                name: "EmployerSnapshots",
                schema: "signals",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployerRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    View = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployerSnapshots", x => new { x.SnapshotId, x.EmployerRef });
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                schema: "signals",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: false),
                    RulesVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MinimumGroupSize = table.Column<int>(type: "integer", nullable: false),
                    EmployerCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Seq);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_Fingerprint",
                schema: "signals",
                table: "Snapshots",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_Id",
                schema: "signals",
                table: "Snapshots",
                column: "Id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployerSnapshots",
                schema: "signals");

            migrationBuilder.DropTable(
                name: "Snapshots",
                schema: "signals");
        }
    }
}
