using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812153200_RequireOneClaimLinePerNote")]
public partial class RequireOneClaimLinePerNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM ClaimLines GROUP BY NoteId HAVING COUNT_BIG(*) > 1)
                THROW 51000, 'Cannot enforce one claim line per note because duplicate claim lines already exist.', 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_ClaimLines_NoteId",
            table: "ClaimLines",
            column: "NoteId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(
            name: "IX_ClaimLines_NoteId",
            table: "ClaimLines");
}
