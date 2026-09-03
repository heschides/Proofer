using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <summary>
    /// Hand-authored. `dotnet ef migrations add` produced an empty diff here because the desktop
    /// model already carried these <c>Person</c> properties before this migration was generated,
    /// so the shared snapshot already reflected them — but no prior migration actually created
    /// the columns. This restores the step the tooling could not see it still owed. See
    /// DECISIONS.md ~2109 for the same hand-authored-migration precedent.
    /// </summary>
    /// <inheritdoc />
    public partial class AddPersonCreatedAtAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "People",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "People",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAtUtc",
                table: "People",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusChangedByUserId",
                table: "People",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusNote",
                table: "People",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StatusChangedByUserId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StatusNote",
                table: "People");
        }
    }
}
