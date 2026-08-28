using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ProviderDirectoryRulesTests
{
    [Theory]
    [InlineData("CaseManager", true)]
    [InlineData("Supervisor", true)]
    [InlineData("Director", true)]
    [InlineData("Admin", true)]
    [InlineData("GlobalAdmin", false)]
    [InlineData(null, false)]
    public void CreateAndEditPermissionsAreExplicit(string? role, bool expected) =>
        Assert.Equal(expected, ProviderDirectoryRules.CanCreateOrEdit(role));

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("CaseManager", false)]
    [InlineData("Supervisor", false)]
    [InlineData("Director", false)]
    [InlineData(null, false)]
    public void DestructivePermissionsAreAdminOnly(string? role, bool expected) =>
        Assert.Equal(expected, ProviderDirectoryRules.CanDeleteOrMerge(role));

    [Fact]
    public void SameNameMatchingTrimsCollapsesWhitespaceAndIgnoresCase()
    {
        Assert.Equal("Maine Health Network", ProviderDirectoryRules.NormalizeName(
            "  Maine\t Health   Network  "));
        Assert.True(ProviderDirectoryRules.IsSameName(
            "MAINE HEALTH NETWORK", " Maine  Health Network "));
        Assert.False(ProviderDirectoryRules.IsSameName("Maine Health", "Maine Healthcare"));
        Assert.False(ProviderDirectoryRules.IsSameName(" ", " "));
    }

    [Fact]
    public void SameNameWarningExcludesTheEntryBeingEditedAndNeverBlocks()
    {
        ProviderAffiliationNode[] directory =
        [
            new(1, "MaineHealth", null, MedicalProviderKind.Network),
            new(2, "  mainehealth ", null, MedicalProviderKind.Network),
            new(3, "Other", null, MedicalProviderKind.Network)
        ];

        Assert.Equal(1, ProviderDirectoryRules.CountSameName("MaineHealth", 1, directory));
        Assert.Contains("An entry", ProviderDirectoryRules.SameNameWarning("MaineHealth", 1, directory));
        Assert.Equal(string.Empty, ProviderDirectoryRules.SameNameWarning("Other", 3, directory));
    }

    [Fact]
    public void SameNameWarningUsesPluralCountsAndExplainsTheTreeRisk()
    {
        ProviderAffiliationNode[] directory =
        [
            new(1, "MaineHealth", null, MedicalProviderKind.Network),
            new(2, "mainehealth", null, MedicalProviderKind.Network)
        ];

        var warning = ProviderDirectoryRules.SameNameWarning(" MAINEHEALTH ", 0, directory);

        Assert.Contains("2 entries", warning);
        Assert.Contains("splits the affiliation tree", warning);
    }

    [Fact]
    public void MergeRequiresTwoDifferentEntriesOfTheSameKind()
    {
        var network = new ProviderAffiliationNode(1, "One", null, MedicalProviderKind.Network);
        var otherNetwork = new ProviderAffiliationNode(2, "Two", null, MedicalProviderKind.Network);
        var practice = new ProviderAffiliationNode(3, "Practice", null, MedicalProviderKind.Practice);

        Assert.Contains("two different", ProviderDirectoryRules.ValidateMerge(network, network));
        Assert.Contains("same kind", ProviderDirectoryRules.ValidateMerge(network, practice));
        Assert.Null(ProviderDirectoryRules.ValidateMerge(network, otherNetwork));
    }

    [Fact]
    public void ConsumerLinkConflictMessageCountsWithoutNamingConsumers()
    {
        var singular = ProviderDirectoryRules.MergeConsumerLinkConflictMessage(1);
        var plural = ProviderDirectoryRules.MergeConsumerLinkConflictMessage(2);

        Assert.Contains("1 consumer has", singular);
        Assert.Contains("2 consumers have", plural);
        Assert.Contains("End or correct", plural);
    }

    [Fact]
    public void MergeSummaryNamesTheEntriesAndCountsEveryMovedRelationship()
    {
        var summary = ProviderDirectoryRules.MergeSummary("Keep", "Remove", 1, 2, 3);

        Assert.Contains("\"Remove\" was merged into \"Keep\"", summary);
        Assert.Contains("1 affiliated entry", summary);
        Assert.Contains("2 consumer links", summary);
        Assert.Contains("3 contacts", summary);
        Assert.Contains("Documents", summary);
    }

    [Fact]
    public void NamedContactValidationIsSharedAndFieldSpecific()
    {
        var errors = ProviderDirectoryRules.ValidateContact(new SaveProviderContactRequest(
            " ",
            new string('r', ProviderDirectoryRules.ContactRoleMaxLength + 1),
            new string('1', ProviderDirectoryRules.ContactPhoneMaxLength + 1),
            new string('2', ProviderDirectoryRules.ContactExtensionMaxLength + 1),
            "not-an-email",
            false,
            -1));

        Assert.Equal(
            new[] { "email", "extension", "name", "phone", "role", "sortOrder" },
            errors.Keys.OrderBy(key => key));
    }

    [Fact]
    public void NamedContactValidationAcceptsTrimmedOptionalDetails()
    {
        var errors = ProviderDirectoryRules.ValidateContact(new SaveProviderContactRequest(
            " Referral coordinator ",
            " Referrals ",
            " 207-555-0100 ",
            " 42 ",
            " referrals@example.test ",
            true,
            3));

        Assert.Empty(errors);
    }
}
