using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddIncentive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExcludeChristmas",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFriday",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeIndependenceDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeIndigenousPeoplesDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeJuneteenth",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeLaborDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeMLKDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeMemorialDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeMonday",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeNewYearsDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludePresidentsDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeThanksgiving",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeThursday",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeTuesday",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeVeteransDay",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeWednesday",
                table: "Settings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Incentives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    DaysScheduled = table.Column<int>(type: "int", nullable: false),
                    BaseIncentive = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PerUnitIncentive = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incentives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incentives_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incentives_UserId_Month_Year",
                table: "Incentives",
                columns: new[] { "UserId", "Month", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Incentives");

            migrationBuilder.DropColumn(
                name: "ExcludeChristmas",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeFriday",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeIndependenceDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeIndigenousPeoplesDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeJuneteenth",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeLaborDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeMLKDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeMemorialDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeMonday",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeNewYearsDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludePresidentsDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeThanksgiving",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeThursday",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeTuesday",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeVeteransDay",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ExcludeWednesday",
                table: "Settings");
        }
    }
}
