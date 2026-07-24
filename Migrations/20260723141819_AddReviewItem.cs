using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    CycleAnchor = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Quarter = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LoggedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SlotIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewItems_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_PersonId_CycleAnchor_Quarter_Category_SlotIndex",
                table: "ReviewItems",
                columns: new[] { "PersonId", "CycleAnchor", "Quarter", "Category", "SlotIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewItems");
        }
    }
}
