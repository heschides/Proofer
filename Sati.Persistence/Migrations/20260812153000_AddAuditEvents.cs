using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812153000_AddAuditEvents")]
public partial class AddAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AgencyId = table.Column<int>(type: "int", nullable: false),
                ActorUserId = table.Column<int>(type: "int", nullable: false),
                Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ResourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ResourceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AuditEvents", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_AgencyId_OccurredAtUtc",
            table: "AuditEvents",
            columns: new[] { "AgencyId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_EventId",
            table: "AuditEvents",
            column: "EventId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AuditEvents");
}
