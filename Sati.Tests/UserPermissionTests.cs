using Sati.Contracts.V1;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using Xunit;

namespace Sati.Tests;

public sealed class UserPermissionTests
{
    [Fact]
    public void SharedContractsOwnThePerUserPermissionSet()
    {
        var permissionsType = typeof(Sati.Contracts.V1.BillingRules).Assembly
            .GetType("Sati.Contracts.V1.UserPermissions");
        var rulesType = typeof(Sati.Contracts.V1.BillingRules).Assembly
            .GetType("Sati.Contracts.V1.UserPermissionRules");

        Assert.NotNull(permissionsType);
        Assert.NotNull(rulesType);
        Assert.True(permissionsType!.IsEnum);
        Assert.NotNull(rulesType!.GetMethod("HasBillingPermissions"));
    }

    [Fact]
    public void PermissionsAreIndependentCapabilities()
    {
        Assert.True(UserPermissionRules.HasBillingPermissions(UserPermissions.Billing));
        Assert.False(UserPermissionRules.HasAdminPermissions(UserPermissions.Billing));
        Assert.True(UserPermissionRules.HasAdminPermissions(UserPermissions.Administration));
        Assert.False(UserPermissionRules.HasBillingPermissions(UserPermissions.Administration));
    }

    [Theory]
    [InlineData("CaseManager", UserPermissions.CaseManagement)]
    [InlineData("Supervisor", UserPermissions.CaseManagement | UserPermissions.Supervision)]
    [InlineData("Director", UserPermissions.CaseManagement | UserPermissions.Supervision | UserPermissions.AgencyWideSupervision)]
    [InlineData("Admin", UserPermissions.AllAgencyPermissions)]
    [InlineData("PlatformOperator", UserPermissions.None)]
    public void LegacyRolesHaveAnExplicitBackfillMapping(
        string role,
        UserPermissions expected) =>
        Assert.Equal(expected, UserPermissionRules.FromLegacyRole(role));

    // The escalation the AddUserPermissions backfill introduced: Director was denied every
    // administration gate under the old role string, so mapping it to Administration handed
    // every existing Director the audit export, settings writes, destructive test-data
    // deletion, and provider merge on upgrade.
    [Fact]
    public void TheLegacyDirectorLabelDoesNotBackfillIntoAdministration()
    {
        var director = UserPermissionRules.FromLegacyRole("Director");

        Assert.False(UserPermissionRules.HasAdminPermissions(director));
        Assert.False(UserPermissionRules.HasBillingPermissions(director));
        Assert.True(UserPermissionRules.HasAgencyWideSupervisionPermissions(director));
        Assert.True(UserPermissionRules.HasSupervisorPermissions(director));
    }

    // Agency-wide reach and administration are separate capabilities, but administration
    // implies the reach: an administrator who can export the whole agency's audit trail is
    // not meaningfully restrained from reading its notes.
    [Fact]
    public void AdministrationImpliesAgencyWideSupervisionButNotTheReverse()
    {
        Assert.True(UserPermissionRules.HasAgencyWideSupervisionPermissions(
            UserPermissions.Administration));
        Assert.False(UserPermissionRules.HasAdminPermissions(
            UserPermissions.AgencyWideSupervision));
    }

    [Theory]
    [InlineData("CaseManager")]
    [InlineData("Supervisor")]
    [InlineData("Director")]
    [InlineData("Admin")]
    public void TheCompatibilityLabelRoundTripsThroughThePermissionSet(string role) =>
        Assert.Equal(role, UserPermissionRules.LegacyLabel(UserPermissionRules.FromLegacyRole(role)));

    [Fact]
    public void UnknownPermissionBitsAreUnsupportedAndDenyByDefault()
    {
        var unknown = UserPermissions.Billing | (UserPermissions)(1 << 20);
        Assert.False(UserPermissionRules.IsSupported(unknown));
        Assert.False(UserPermissionRules.HasBillingPermissions(unknown));
    }

    [Fact]
    public void MigrationBackfillsEveryLegacyAgencyRoleAndDeniesUnknownLabels()
    {
        var migration = new Sati.Migrations.AddUserPermissions();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Sati.Migrations.AddUserPermissions)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("WHEN 'CaseManager' THEN 1", sql);
        Assert.Contains("WHEN 'Supervisor' THEN 3", sql);
        Assert.Contains("WHEN 'Director' THEN 7", sql);
        Assert.Contains("WHEN 'Admin' THEN 15", sql);
        Assert.Contains("ELSE 0", sql);
    }

    // AddUserPermissions is left as written, because editing a migration body already
    // recorded in __EFMigrationsHistory is skipped on upgraded databases and applied on
    // fresh ones — which is exactly how the two diverge. The correction is a second
    // migration, and the pair must land on the values FromLegacyRole now returns.
    [Fact]
    public void TheCorrectiveMigrationLandsOnTheSameValuesTheRulesReturn()
    {
        var migration = new Sati.Migrations.SeparateAgencyWideSupervision();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Sati.Migrations.SeparateAgencyWideSupervision)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains(
            $"SET [Permissions] = {(int)UserPermissionRules.FromLegacyRole("Director")}", sql);
        Assert.Contains(
            $"SET [Permissions] = {(int)UserPermissionRules.FromLegacyRole("Admin")}", sql);

        // Scoped to the exact values AddUserPermissions wrote, so a deliberate edit made
        // between the two migrations is preserved rather than clobbered.
        Assert.Contains("[Role] = 'Director' AND [Permissions] = 7", sql);
        Assert.Contains("[Role] = 'Admin' AND [Permissions] = 15", sql);
    }
}
