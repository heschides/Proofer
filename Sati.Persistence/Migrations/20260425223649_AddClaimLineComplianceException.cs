using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimLineComplianceException : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ComplianceOverride",
                table: "Notes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverrideApprovedAt",
                table: "Notes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideApprovedById",
                table: "Notes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "Notes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComplianceExceptionReason",
                table: "ClaimLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsComplianceException",
                table: "ClaimLines",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplianceOverride",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "OverrideApprovedAt",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "OverrideApprovedById",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "ComplianceExceptionReason",
                table: "ClaimLines");

            migrationBuilder.DropColumn(
                name: "IsComplianceException",
                table: "ClaimLines");
        }
    }
}
