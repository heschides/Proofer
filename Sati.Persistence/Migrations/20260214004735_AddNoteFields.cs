using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_People_PersonId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "Notes");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "Notes",
                newName: "Narrative");

            migrationBuilder.AlterColumn<int>(
                name: "PersonId",
                table: "Notes",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EventDate",
                table: "Notes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_People_PersonId",
                table: "Notes",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_People_PersonId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "Notes");

            migrationBuilder.RenameColumn(
                name: "Narrative",
                table: "Notes",
                newName: "Content");

            migrationBuilder.AlterColumn<int>(
                name: "PersonId",
                table: "Notes",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "Notes",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_People_PersonId",
                table: "Notes",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id");
        }
    }
}
