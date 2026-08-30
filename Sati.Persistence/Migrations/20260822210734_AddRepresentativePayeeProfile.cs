using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddRepresentativePayeeProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CaseManagerIsRepPayee",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RepPayeeMonthlyIncome",
                table: "People",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepPayeeRegularCheckRequestNeeds",
                table: "People",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CaseManagerIsRepPayee",
                table: "People");

            migrationBuilder.DropColumn(
                name: "RepPayeeMonthlyIncome",
                table: "People");

            migrationBuilder.DropColumn(
                name: "RepPayeeRegularCheckRequestNeeds",
                table: "People");
        }
    }
}
