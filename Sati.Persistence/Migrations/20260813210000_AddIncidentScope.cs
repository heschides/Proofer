using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260813210000_AddIncidentScope")]
public partial class AddIncidentScope : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_IncidentGroups_AgencyId_Source_Operation_ExceptionFingerprint",
            table: "IncidentGroups");

        migrationBuilder.AddColumn<string>(
            name: "Scope",
            table: "IncidentGroups",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Agency");

        migrationBuilder.CreateIndex(
            name: "IX_IncidentGroups_AgencyId_Scope_Source_Operation_ExceptionFingerprint",
            table: "IncidentGroups",
            columns: new[] { "AgencyId", "Scope", "Source", "Operation", "ExceptionFingerprint" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_IncidentGroups_AgencyId_Scope_Source_Operation_ExceptionFingerprint",
            table: "IncidentGroups");

        migrationBuilder.DropColumn(
            name: "Scope",
            table: "IncidentGroups");

        migrationBuilder.CreateIndex(
            name: "IX_IncidentGroups_AgencyId_Source_Operation_ExceptionFingerprint",
            table: "IncidentGroups",
            columns: new[] { "AgencyId", "Source", "Operation", "ExceptionFingerprint" },
            unique: true);
    }
}
