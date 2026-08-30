using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingExchangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingSubmissionEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ResponseType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ResponseCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSynthetic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSubmissionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingSubmissionEvents_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceClaimOutcomes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    BillingPeriodId = table.Column<int>(type: "int", nullable: true),
                    ClaimReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PayerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BilledAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllowedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PatientResponsibilityAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IsSynthetic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceClaimOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemittanceClaimOutcomes_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubmissionEvents_AgencyId_OccurredAtUtc",
                table: "BillingSubmissionEvents",
                columns: new[] { "AgencyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubmissionEvents_BillingPeriodId",
                table: "BillingSubmissionEvents",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceClaimOutcomes_AgencyId_ReceivedAtUtc",
                table: "RemittanceClaimOutcomes",
                columns: new[] { "AgencyId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceClaimOutcomes_BillingPeriodId",
                table: "RemittanceClaimOutcomes",
                column: "BillingPeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingSubmissionEvents");

            migrationBuilder.DropTable(
                name: "RemittanceClaimOutcomes");
        }
    }
}
