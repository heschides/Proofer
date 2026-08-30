using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddRemittanceDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemittanceDeposits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PayerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimPaymentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProviderLevelAdjustmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ProviderLevelAdjustmentSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RemittancePaymentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EftDepositAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsSynthetic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceDeposits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceDeposits_AgencyId_ReceivedAtUtc",
                table: "RemittanceDeposits",
                columns: new[] { "AgencyId", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemittanceDeposits");
        }
    }
}
