using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812235500_AddEdiIdempotency")]
public partial class AddEdiIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EdiGenerations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AgencyId = table.Column<int>(type: "int", nullable: false),
                ActorUserId = table.Column<int>(type: "int", nullable: false),
                BillingPeriodId = table.Column<int>(type: "int", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                IsTest = table.Column<bool>(type: "bit", nullable: false),
                FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EdiGenerations", x => x.Id);
                table.ForeignKey(
                    name: "FK_EdiGenerations_BillingPeriods_BillingPeriodId",
                    column: x => x.BillingPeriodId,
                    principalTable: "BillingPeriods",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EdiGenerations_BillingPeriodId",
            table: "EdiGenerations",
            column: "BillingPeriodId");

        migrationBuilder.CreateIndex(
            name: "IX_EdiGenerations_AgencyId_ActorUserId_IdempotencyKey",
            table: "EdiGenerations",
            columns: new[] { "AgencyId", "ActorUserId", "IdempotencyKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "EdiGenerations");
}
