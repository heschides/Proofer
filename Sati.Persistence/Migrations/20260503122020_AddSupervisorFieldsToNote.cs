using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddSupervisorFieldsToNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExemptDates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExemptDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExemptDates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExemptDates_UserId",
                table: "ExemptDates",
                column: "UserId");

            migrationBuilder.DropColumn(
                name: "ExcludedDatesJson",
                table: "Incentives");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExemptDates");

            migrationBuilder.AddColumn<string>(
                name: "ExcludedDatesJson",
                table: "Incentives",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }
    }
}
