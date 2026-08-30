using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812153300_AddPersonLifecycleHistory")]
public partial class AddPersonLifecycleHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Revision",
            table: "People",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.CreateTable(
            name: "PersonVersions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PersonId = table.Column<int>(type: "int", nullable: false),
                AgencyId = table.Column<int>(type: "int", nullable: false),
                ActorUserId = table.Column<int>(type: "int", nullable: false),
                ActorDisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Version = table.Column<int>(type: "int", nullable: false),
                ChangeKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SnapshotGzip = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                ChangesGzip = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonVersions_People_PersonId",
                    column: x => x.PersonId,
                    principalTable: "People",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PersonVersions_AgencyId_ChangedAtUtc",
            table: "PersonVersions",
            columns: new[] { "AgencyId", "ChangedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonVersions_PersonId_Version",
            table: "PersonVersions",
            columns: new[] { "PersonId", "Version" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PersonVersions");
        migrationBuilder.DropColumn(name: "Revision", table: "People");
    }
}
