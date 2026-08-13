using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260813162000_AddIncidentHealthPipeline")]
public partial class AddIncidentHealthPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IncidentGroups",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AgencyId = table.Column<int>(type: "int", nullable: false),
                Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Operation = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                FirstRelease = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                LastRelease = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                ExceptionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                FirstSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastSeenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastReference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                LastActorRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IncidentGroups", x => x.Id);
                table.ForeignKey(
                    name: "FK_IncidentGroups_Agencies_AgencyId",
                    column: x => x.AgencyId,
                    principalTable: "Agencies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IncidentGroups_AgencyId_LastSeenUtc",
            table: "IncidentGroups",
            columns: new[] { "AgencyId", "LastSeenUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_IncidentGroups_AgencyId_Source_Operation_ExceptionFingerprint",
            table: "IncidentGroups",
            columns: new[] { "AgencyId", "Source", "Operation", "ExceptionFingerprint" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "IncidentGroups");
}
