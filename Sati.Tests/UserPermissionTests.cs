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
    [InlineData("Director", UserPermissions.CaseManagement | UserPermissions.Supervision | UserPermissions.Administration)]
    [InlineData("Admin", UserPermissions.AllAgencyPermissions)]
    [InlineData("PlatformOperator", UserPermissions.None)]
    public void LegacyRolesHaveAnExplicitBackfillMapping(
        string role,
        UserPermissions expected) =>
        Assert.Equal(expected, UserPermissionRules.FromLegacyRole(role));

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
}
