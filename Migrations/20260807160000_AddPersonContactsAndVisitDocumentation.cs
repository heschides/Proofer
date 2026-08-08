using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260807160000_AddPersonContactsAndVisitDocumentation")]
public partial class AddPersonContactsAndVisitDocumentation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "VisitDocumentationJson",
            table: "Notes",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "PersonContacts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PersonId = table.Column<int>(type: "int", nullable: false),
                FirstName = table.Column<string>(type: "nvarchar(75)", maxLength: 75, nullable: false),
                LastName = table.Column<string>(type: "nvarchar(75)", maxLength: 75, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Relationship = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Organization = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                IsEmergencyContact = table.Column<bool>(type: "bit", nullable: false),
                HasActiveRelease = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonContacts", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonContacts_People_PersonId",
                    column: x => x.PersonId,
                    principalTable: "People",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PersonContacts_PersonId_IsActive",
            table: "PersonContacts",
            columns: new[] { "PersonId", "IsActive" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PersonContacts");
        migrationBuilder.DropColumn(name: "VisitDocumentationJson", table: "Notes");
    }
}
