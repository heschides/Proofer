using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812233000_AddSettingsRevision")]
public partial class AddSettingsRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>(
            name: "Revision",
            table: "Settings",
            type: "int",
            nullable: false,
            defaultValue: 1);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "Revision",
            table: "Settings");
}
