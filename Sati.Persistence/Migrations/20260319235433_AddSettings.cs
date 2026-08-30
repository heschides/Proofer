using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AbandonedAfterDays = table.Column<int>(type: "int", nullable: false),
                    ProductivityThreshold = table.Column<int>(type: "int", nullable: false),
                    BaseIncentive = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PerUnitIncentive = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VisitTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContactTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentationTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
