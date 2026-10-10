using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExitInterviewAgent.InterviewService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "CreditEntries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedHour",
                table: "CreditEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SettledWeek = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSettlements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionSettlements_SessionId",
                table: "SessionSettlements",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionSettlements");

            migrationBuilder.DropColumn(
                name: "StartedHour",
                table: "CreditEntries");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "CreditEntries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
