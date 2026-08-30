using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenedDateAndAnniversaryOffsets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompAssessmentDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PcpDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrivacyPracticesDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReclassificationDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseAgencyDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseDhhsDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseMedicalDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SafetyPlanDaysBeforeAnniversary",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedDate",
                table: "Forms",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompAssessmentDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PcpDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PrivacyPracticesDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReclassificationDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseAgencyDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseDhhsDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReleaseMedicalDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SafetyPlanDaysBeforeAnniversary",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OpenedDate",
                table: "Forms");
        }
    }
}
