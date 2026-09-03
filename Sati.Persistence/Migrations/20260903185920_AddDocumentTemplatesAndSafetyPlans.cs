using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTemplatesAndSafetyPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(nullable: true),
                    Kind = table.Column<string>(maxLength: 40, nullable: false),
                    Version = table.Column<int>(nullable: false),
                    Body = table.Column<string>(maxLength: 100000, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(nullable: false),
                    PublishedByUserId = table.Column<int>(nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(nullable: true)
                }, constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                    table.ForeignKey("FK_DocumentTemplates_Agencies_AgencyId", x => x.AgencyId, "Agencies", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_DocumentTemplates_Users_PublishedByUserId", x => x.PublishedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex("IX_DocumentTemplates_AgencyKindVersion", "DocumentTemplates", new[] { "AgencyId", "Kind", "Version" }, unique: true);
            migrationBuilder.CreateIndex("IX_DocumentTemplates_PublishedByUserId", "DocumentTemplates", "PublishedByUserId");
            // Freeze this published content in the migration; later template changes
            // must not alter what a fresh database receives for version 1.
            const string privacyPracticesBody = """
# Notice of Privacy Practices

PROVISIONAL SATI DEFAULT - AGENCY PRIVACY AND LEGAL REVIEW REQUIRED

Prepared for cycle beginning: {{cycle.start}}

This notice describes general ways {{agency.name}} may use and share information about {{consumer.full_name}}, and how the individual or authorized representative may exercise privacy rights. It is a generic starting point and must be replaced or approved by the agency before production use.

## Our responsibilities

- Protect the privacy and security of health and service information.
- Follow the privacy practices described in the agency's current approved notice.
- Notify affected people when required after a breach of unsecured information.
- Provide the current notice when privacy practices materially change.

## How information may be used or shared

Information may be used or shared as permitted or required by applicable law for treatment and service coordination, payment, health-care operations, public-health and safety duties, oversight, legal proceedings, and other specifically authorized purposes. Uses or disclosures requiring written authorization will not occur without that authorization, and an authorization may be revoked as allowed by law.

## Individual privacy rights

- Ask to inspect or obtain a copy of records, subject to lawful limits.
- Ask for a correction or amendment.
- Ask for confidential communications or certain restrictions.
- Ask for an accounting of qualifying disclosures.
- Receive a paper copy of the agency's approved notice.
- Make a privacy complaint without retaliation.

## Questions or complaints

Contact {{agency.name}} at {{agency.address}} or {{agency.phone}} to ask questions, exercise a privacy right, or make a complaint. The agency's approved notice must identify any additional external complaint process that applies.

## Receipt

Receiving this notice does not authorize a release of information. Receipt or a documented good-faith effort to provide the notice is recorded separately by authorized staff.

Prepared for: {{consumer.full_name}}
Date of birth: {{consumer.birth_date}}
Case manager: {{case_manager.name}}, {{case_manager.role}}
Coverage cycle: {{cycle.start}} through {{cycle.end}}
""";
            migrationBuilder.InsertData("DocumentTemplates", new[] { "Id", "AgencyId", "Kind", "Version", "Body", "PublishedAtUtc", "PublishedByUserId", "RetiredAtUtc" },
                new object[] { 1, null!, "PrivacyPractices", 1, privacyPracticesBody, new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), null!, null! });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DocumentTemplates");
        }
    }
}
