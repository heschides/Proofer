using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812090000_TenantScopeSettingsAndProviders")]
public partial class TenantScopeSettingsAndProviders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AgencyId",
            table: "Settings",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AgencyId",
            table: "Providers",
            type: "int",
            nullable: true);

        // Production and Demo are separate databases. Assign each legacy global
        // directory/settings row to the agency actually represented by users in
        // that database, falling back to the first seeded agency on an empty DB.
        migrationBuilder.Sql("""
            DECLARE @AgencyId int = COALESCE(
                (SELECT TOP (1) AgencyId FROM Users GROUP BY AgencyId ORDER BY COUNT_BIG(*) DESC, AgencyId),
                (SELECT TOP (1) Id FROM Agencies ORDER BY Id));

            UPDATE Settings SET AgencyId = @AgencyId WHERE AgencyId IS NULL;
            UPDATE Providers SET AgencyId = @AgencyId WHERE AgencyId IS NULL;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "AgencyId",
            table: "Settings",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "AgencyId",
            table: "Providers",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Settings_AgencyId",
            table: "Settings",
            column: "AgencyId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Providers_AgencyId_Name",
            table: "Providers",
            columns: new[] { "AgencyId", "Name" });

        migrationBuilder.AddForeignKey(
            name: "FK_Settings_Agencies_AgencyId",
            table: "Settings",
            column: "AgencyId",
            principalTable: "Agencies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Providers_Agencies_AgencyId",
            table: "Providers",
            column: "AgencyId",
            principalTable: "Agencies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Settings_Agencies_AgencyId", table: "Settings");
        migrationBuilder.DropForeignKey(name: "FK_Providers_Agencies_AgencyId", table: "Providers");
        migrationBuilder.DropIndex(name: "IX_Settings_AgencyId", table: "Settings");
        migrationBuilder.DropIndex(name: "IX_Providers_AgencyId_Name", table: "Providers");
        migrationBuilder.DropColumn(name: "AgencyId", table: "Settings");
        migrationBuilder.DropColumn(name: "AgencyId", table: "Providers");
    }
}
