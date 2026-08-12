using Sati.Api.Security;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sati.Tests;

public sealed class StabilizationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void CaseManagerMayWriteOnlyOwnedWorkflowStatuses(int status)
    {
        Assert.True(NoteWorkflow.IsCaseManagerWritableStatus(status));
    }

    [Theory]
    [InlineData(6)] // Approved
    [InlineData(7)] // Returned
    [InlineData(8)] // Abandoned
    public void CaseManagerCannotAssertServerOwnedStatuses(int status)
    {
        Assert.False(NoteWorkflow.IsCaseManagerWritableStatus(status));
    }

    [Fact]
    public void LoggedAndApprovedNotesAreLocked()
    {
        Assert.False(NoteWorkflow.CanCaseManagerEdit(2));
        Assert.False(NoteWorkflow.CanCaseManagerEdit(6));
        Assert.True(NoteWorkflow.CanCaseManagerEdit(7));
    }

    [Fact]
    public void OnlyUnsubmittedNotesCanBeDeleted()
    {
        Assert.True(NoteWorkflow.CanCaseManagerDelete(1));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(2));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(6));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(7));
    }

    [Fact]
    public void ApiPasswordHasherProducesVerifiableSaltedCredentials()
    {
        var service = new PasswordVerifier();
        var first = service.Hash("correct horse battery staple");
        var second = service.Hash("correct horse battery staple");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(service.Verify("correct horse battery staple", first.Hash, first.Salt));
        Assert.False(service.Verify("wrong password", first.Hash, first.Salt));
    }

    [Fact]
    public void DayAfterThanksgivingIsExcludedWhenConfigured()
    {
        var settings = new Settings { ExcludeDayAfterThanksgiving = true };
        Assert.True(WorkdayHelper.IsAlwaysExcludedWorkday(new DateTime(2026, 11, 27), settings));
    }

    [Fact]
    public void PersonCreationGeneratesOneFormPerType()
    {
        var settings = new Settings();
        var person = Person.CreatePerson(
            1, "Ada", "Lovelace", string.Empty,
            new DateTime(1990, 1, 1), new DateTime(2026, 1, 1),
            WaiverType.Section21, settings);

        Assert.Equal(Enum.GetValues<FormType>().Length, person.Forms.Count);
    }

    [Fact]
    public void IncentiveCalculationAppliesThresholdAndPerUnitRate()
    {
        var incentive = new Incentive
        {
            BaseIncentive = 100m,
            PerUnitIncentive = 2m,
            UnitsPerDay = 10,
            DaysScheduled = 10
        };

        Assert.Equal(0m, incentive.Calculate(99m));
        Assert.Equal(100m, incentive.Calculate(100m));
        Assert.Equal(110m, incentive.Calculate(105m));
    }

    [Fact]
    public void EfModelMatchesLatestMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiModelValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
