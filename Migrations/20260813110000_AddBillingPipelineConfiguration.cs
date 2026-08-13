using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260813110000_AddBillingPipelineConfiguration")]
public partial class AddBillingPipelineConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("BillingProcedureCode", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("BillingModifier", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<decimal>("BillingUnitRate", "Agencies", "decimal(18,2)", nullable: true);
        migrationBuilder.AddColumn<string>("EdiSubmitterId", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("EdiPayerName", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("EdiPayerId", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("EdiContactName", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("EdiContactPhone", "Agencies", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("ProcedureModifier", "ClaimLines", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<string>("ClaimSnapshotJson", "ClaimLines", "nvarchar(max)", nullable: true);
        migrationBuilder.AddColumn<decimal>(
            "ChargeAmount", "ClaimLines", "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>("BillingStreet", "People", "nvarchar(55)", maxLength: 55, nullable: true);
        migrationBuilder.AddColumn<string>("BillingCity", "People", "nvarchar(30)", maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<string>("BillingState", "People", "nvarchar(2)", maxLength: 2, nullable: true);
        migrationBuilder.AddColumn<string>("BillingZip", "People", "nvarchar(15)", maxLength: 15, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("BillingProcedureCode", "Agencies");
        migrationBuilder.DropColumn("BillingModifier", "Agencies");
        migrationBuilder.DropColumn("BillingUnitRate", "Agencies");
        migrationBuilder.DropColumn("EdiSubmitterId", "Agencies");
        migrationBuilder.DropColumn("EdiPayerName", "Agencies");
        migrationBuilder.DropColumn("EdiPayerId", "Agencies");
        migrationBuilder.DropColumn("EdiContactName", "Agencies");
        migrationBuilder.DropColumn("EdiContactPhone", "Agencies");
        migrationBuilder.DropColumn("ProcedureModifier", "ClaimLines");
        migrationBuilder.DropColumn("ClaimSnapshotJson", "ClaimLines");
        migrationBuilder.DropColumn("ChargeAmount", "ClaimLines");
        migrationBuilder.DropColumn("BillingStreet", "People");
        migrationBuilder.DropColumn("BillingCity", "People");
        migrationBuilder.DropColumn("BillingState", "People");
        migrationBuilder.DropColumn("BillingZip", "People");
    }
}
