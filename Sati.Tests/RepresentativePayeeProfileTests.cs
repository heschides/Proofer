using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Contracts.V1;
using Sati.Data.Cloud;
using Xunit;

namespace Sati.Tests;

public sealed class RepresentativePayeeProfileTests
{
    [Fact]
    public void PayeeRequiresMonthlyIncomeAndExplicitRecurringNeeds()
    {
        var missing = RepresentativePayeeRules.Validate(true, null, null);
        var valid = RepresentativePayeeRules.Validate(
            true,
            943.50m,
            "Rent on the first and a weekly personal-needs check.");

        Assert.Contains("repPayeeMonthlyIncome", missing.Keys);
        Assert.Contains("repPayeeRegularCheckRequestNeeds", missing.Keys);
        Assert.Empty(valid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("943.501")]
    public void MonthlyIncomeRejectsNonPositiveOrFractionalCentAmounts(string value)
    {
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

        var errors = RepresentativePayeeRules.Validate(true, amount, "None");

        Assert.Contains("repPayeeMonthlyIncome", errors.Keys);
    }

    [Fact]
    public void NonPayeeProfileCannotRetainFinancialDetails()
    {
        var errors = RepresentativePayeeRules.Validate(
            false,
            943.50m,
            "This stale value must not survive a No selection.");

        Assert.Contains("representativePayeeDetails", errors.Keys);
    }

    [Fact]
    public void CloudSaveContractCarriesTheCompletePayeeProfile()
    {
        var person = Person.Rehydrate(41, 7);
        person.FirstName = "Profile";
        person.LastName = "Test";
        person.Bio = "Current biography.";
        person.BirthDate = new DateTime(1990, 1, 1);
        person.CaseManagerIsRepPayee = true;
        person.RepPayeeMonthlyIncome = 943.50m;
        person.RepPayeeRegularCheckRequestNeeds = "Rent and weekly spending money.";
        person.Revision = 3;

        var request = CloudContractMapper.ToSavePersonRequest(person);

        Assert.True(request.CaseManagerIsRepPayee);
        Assert.Equal(943.50m, request.RepPayeeMonthlyIncome);
        Assert.Equal("Rent and weekly spending money.", request.RepPayeeRegularCheckRequestNeeds);
        Assert.Equal(3, request.ExpectedRevision);
    }

    [Fact]
    public void MigrationAddsBoundedFinancialProfileColumns()
    {
        var migration = new Migrations.AddRepresentativePayeeProfile();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Migrations.AddRepresentativePayeeProfile)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Contains(columns, column =>
            column.Name == "CaseManagerIsRepPayee" && column.ClrType == typeof(bool));
        Assert.Contains(columns, column =>
            column.Name == "RepPayeeMonthlyIncome" && column.ColumnType == "decimal(18,2)");
        Assert.Contains(columns, column =>
            column.Name == "RepPayeeRegularCheckRequestNeeds" && column.MaxLength == 2_000);
    }

    [Fact]
    public void ProfileUsesAccessibleYesNoAndConditionalDetailControls()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "ClientsView.xaml"));

        Assert.Contains("Case manager is representative payee, yes", xaml, StringComparison.Ordinal);
        Assert.Contains("Case manager is representative payee, no", xaml, StringComparison.Ordinal);
        Assert.Contains("RepPayeeMonthlyIncomeText", xaml, StringComparison.Ordinal);
        Assert.Contains("RepPayeeRegularCheckRequestNeeds", xaml, StringComparison.Ordinal);
        Assert.Contains("It does not request or authorize a check", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"2000\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LongLivedDatabaseRunnerIsGuardedTransactionalAndBacksUpLocalRecords()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Apply-RepresentativePayeeProfileMigration.ps1"));

        Assert.Contains("20260822210734_AddRepresentativePayeeProfile", script, StringComparison.Ordinal);
        Assert.Contains("SatiDatabaseIdentity", script, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.People', N'CaseManagerIsRepPayee')", script, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION", script, StringComparison.Ordinal);
        Assert.Contains("BACKUP DATABASE", script, StringComparison.Ordinal);
        Assert.Contains("-and $personCount -gt 0", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
