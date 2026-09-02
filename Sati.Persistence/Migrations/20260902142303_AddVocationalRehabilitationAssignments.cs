using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddVocationalRehabilitationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VrAssistantTitle",
                table: "Settings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "VSA");

            migrationBuilder.AddColumn<string>(
                name: "VrAssistantName",
                table: "People",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VrCounselorName",
                table: "People",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VrAssistantTitle",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "VrAssistantName",
                table: "People");

            migrationBuilder.DropColumn(
                name: "VrCounselorName",
                table: "People");
        }
    }
}
