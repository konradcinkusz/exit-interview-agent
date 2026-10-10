using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExitInterviewAgent.InterviewService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditsAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedWeek = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccountRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReceivedWeek = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_AccountRef",
                table: "CreditEntries",
                column: "AccountRef");

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_Reason_Reference",
                table: "CreditEntries",
                columns: new[] { "Reason", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ProviderEventId",
                table: "PaymentEvents",
                column: "ProviderEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditEntries");

            migrationBuilder.DropTable(
                name: "PaymentEvents");
        }
    }
}
