using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExitInterviewAgent.InterviewService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EmployerRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    Verification = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedWeek = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedWeek = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionLedger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sub = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_Records_RecordId",
                        column: x => x.RecordId,
                        principalTable: "Records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CodeHash",
                table: "Receipts",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_RecordId",
                table: "Receipts",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Records_CreatedWeek",
                table: "Records",
                column: "CreatedWeek");

            migrationBuilder.CreateIndex(
                name: "IX_Records_EmployerRef",
                table: "Records",
                column: "EmployerRef");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionLedger_CreatedWeek",
                table: "SubmissionLedger",
                column: "CreatedWeek");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionLedger_Tag",
                table: "SubmissionLedger",
                column: "Tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionTickets_ExpiresAt",
                table: "SubmissionTickets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionTickets_Sub",
                table: "SubmissionTickets",
                column: "Sub");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionTickets_TokenHash",
                table: "SubmissionTickets",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "SubmissionLedger");

            migrationBuilder.DropTable(
                name: "SubmissionTickets");

            migrationBuilder.DropTable(
                name: "Records");
        }
    }
}
