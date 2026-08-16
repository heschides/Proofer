using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddAtRequestAttestation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttestationStatement",
                table: "ATRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedAtUtc",
                table: "ATRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByName",
                table: "ATRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByRole",
                table: "ATRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignedByUserId",
                table: "ATRequests",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttestationStatement",
                table: "ATRequests");

            migrationBuilder.DropColumn(
                name: "SignedAtUtc",
                table: "ATRequests");

            migrationBuilder.DropColumn(
                name: "SignedByName",
                table: "ATRequests");

            migrationBuilder.DropColumn(
                name: "SignedByRole",
                table: "ATRequests");

            migrationBuilder.DropColumn(
                name: "SignedByUserId",
                table: "ATRequests");
        }
    }
}
