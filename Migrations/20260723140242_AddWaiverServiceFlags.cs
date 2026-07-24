using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddWaiverServiceFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DayProgramCount",
                table: "People",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "HasCommunitySupport1To1",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasCommunitySupportDayProgram",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasCommunitySupportSelfDirected",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasEmploymentSpecialist",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasHomeSupport",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSelfDirectedHomeSupport",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasSharedLiving",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasWorkSupports",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmployed",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DayProgramCount",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasCommunitySupport1To1",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasCommunitySupportDayProgram",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasCommunitySupportSelfDirected",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasEmploymentSpecialist",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasHomeSupport",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasSelfDirectedHomeSupport",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasSharedLiving",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HasWorkSupports",
                table: "People");

            migrationBuilder.DropColumn(
                name: "IsEmployed",
                table: "People");
        }
    }
}
