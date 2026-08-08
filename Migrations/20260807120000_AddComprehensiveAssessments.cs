using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260807120000_AddComprehensiveAssessments")]
public partial class AddComprehensiveAssessments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ComprehensiveAssessments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PersonId = table.Column<int>(type: "int", nullable: false),
                AuthorUserId = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Version = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                DocumentJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ComprehensiveAssessments", x => x.Id);
                table.ForeignKey("FK_ComprehensiveAssessments_People_PersonId", x => x.PersonId, "People", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ComprehensiveAssessments_Users_AuthorUserId", x => x.AuthorUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ComprehensiveAssessments_Users_ApprovedByUserId", x => x.ApprovedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ComprehensiveAssessments_AuthorUserId", "ComprehensiveAssessments", "AuthorUserId");
        migrationBuilder.CreateIndex("IX_ComprehensiveAssessments_ApprovedByUserId", "ComprehensiveAssessments", "ApprovedByUserId");
        migrationBuilder.CreateIndex("IX_ComprehensiveAssessments_PersonId_Version", "ComprehensiveAssessments", new[] { "PersonId", "Version" }, unique: true);
        migrationBuilder.Sql("UPDATE Settings SET CompAssessmentDaysBeforeAnniversary = 60 WHERE CompAssessmentDaysBeforeAnniversary = 120");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "ComprehensiveAssessments");
}
