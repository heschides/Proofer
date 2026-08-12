using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260810130000_AddScratchpadComments")]
public partial class AddScratchpadComments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ScratchpadComments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScratchpadId = table.Column<int>(type: "int", nullable: false),
                AuthorUserId = table.Column<int>(type: "int", nullable: false),
                AuthorDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ScratchpadComments", x => x.Id);
                table.ForeignKey(
                    name: "FK_ScratchpadComments_Scratchpad_ScratchpadId",
                    column: x => x.ScratchpadId,
                    principalTable: "Scratchpad",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ScratchpadComments_ScratchpadId_CreatedAtUtc",
            table: "ScratchpadComments",
            columns: new[] { "ScratchpadId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ScratchpadComments");
    }
}
