using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;
using Xunit;

namespace Sati.Tests;

public sealed class PersistenceAssemblyBoundaryTests
{
    [Fact]
    public void ContextEntitiesAndEntireMigrationChainAreCrossPlatform()
    {
        var persistenceAssembly = typeof(SatiContext).Assembly;

        Assert.Equal("Sati.Persistence", persistenceAssembly.GetName().Name);
        Assert.Same(persistenceAssembly, typeof(Person).Assembly);
        Assert.Equal(
            ".NETCoreApp,Version=v10.0",
            persistenceAssembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);

        var referencedAssemblies = persistenceAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("PresentationFramework", referencedAssemblies);
        Assert.DoesNotContain("PresentationCore", referencedAssemblies);
        Assert.DoesNotContain("WindowsBase", referencedAssemblies);

        var migrationIds = persistenceAssembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(id => id is not null)
            .ToList();

        Assert.Equal(94, migrationIds.Count);
        Assert.Contains("20260812090000_TenantScopeSettingsAndProviders", migrationIds);
        Assert.Contains("20260830224423_AddUserPermissions", migrationIds);
        Assert.Contains("20260830231500_SeparateAgencyWideSupervision", migrationIds);
        Assert.Contains("20260901150802_AddUniqueFormPersonTypeDueDateIndex", migrationIds);
        Assert.Contains("20260901154714_AddDerivedFormCompliance", migrationIds);
        Assert.Contains("20260901232228_AddPersonCredibleClientId", migrationIds);
        Assert.Contains("20260902140636_AddCredibleProfileUpdateSetting", migrationIds);
        Assert.Contains("20260902142303_AddVocationalRehabilitationAssignments", migrationIds);
        Assert.Contains("20260903152847_AddFormAttestations", migrationIds);
        Assert.Contains("20260903173950_AddDocumentArtifacts", migrationIds);
        Assert.Contains("20260903175219_AddPersonCreatedAtAndStatus", migrationIds);
        Assert.Contains(migrationIds, id => id!.EndsWith("_AddLegalHolds", StringComparison.Ordinal));
        Assert.Contains(migrationIds, id => id!.EndsWith("_AddSafetyPlans", StringComparison.Ordinal));
    }
}
