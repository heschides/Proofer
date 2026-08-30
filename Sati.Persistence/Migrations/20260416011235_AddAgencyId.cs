using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agencies", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Agencies",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
            { 1, "Internal" },
            { 2, "Sandbox Mode" }
                });

            migrationBuilder.AddColumn<int>(
     name: "AgencyId",
     table: "Users",
     type: "int",
     nullable: false,
     defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AgencyId",
                table: "People",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AgencyId",
                table: "Notes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AgencyId",
                table: "Users",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_People_AgencyId",
                table: "People",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_AgencyId",
                table: "Notes",
                column: "AgencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Agencies_AgencyId",
                table: "Notes",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Agencies_AgencyId",
                table: "People",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Agencies_AgencyId",
                table: "Users",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
