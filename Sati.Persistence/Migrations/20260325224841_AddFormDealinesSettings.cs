using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDealinesSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompAssessmentDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CompAssessmentOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PcpDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PcpOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrivacyPracticesDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrivacyPracticesOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReclassificationDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReclassificationOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseAgencyDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseAgencyOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseDhhsDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseDhhsOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseMedicalDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseMedicalOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SafetyPlanDaysAfterDue",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SafetyPlanOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompAssessmentDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CompAssessmentOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PcpDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PcpOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PrivacyPracticesDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PrivacyPracticesOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReclassificationDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReclassificationOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseAgencyDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseAgencyOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseDhhsDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseDhhsOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseMedicalDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseMedicalOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReviewDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReviewOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SafetyPlanDaysAfterDue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SafetyPlanOpenDaysBefore",
                table: "Settings");
        }
    }
}
