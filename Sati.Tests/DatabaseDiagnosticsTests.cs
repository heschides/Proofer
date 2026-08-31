using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// What the Settings database panel says about the schema in front of it.
///
/// Written against the 2026-08-30 session, where three machines held 78, 73, and 72
/// migration history rows and nobody knew until one refused to start. A colleague was three
/// releases behind and the first signal was a failure dialog. Every question asked that
/// evening — which environment, which database, how many updates, which are pending — is
/// one this panel answers without anyone running a script.
/// </summary>
public sealed class DatabaseDiagnosticsTests
{
    private static DatabaseDiagnostics Compare(string[] applied, string[] expected) =>
        DatabaseDiagnostics.Compare(
            "PRODUCTION", "Local database", "SatiProduction", "Production", applied, expected);

    private static string[] Ids(params int[] numbers) =>
        [.. numbers.Select(number => $"2026083000{number:D4}_Migration{number}")];

    [Fact]
    public void MatchingHistoryReadsAsUpToDate()
    {
        var diagnostics = Compare(Ids(1, 2, 3), Ids(1, 2, 3));

        Assert.Equal(SchemaCurrency.Current, diagnostics.Currency);
        Assert.False(diagnostics.NeedsAttention);
        Assert.Contains("Up to date", diagnostics.Headline, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ordinary case, and the one nobody needs to act on: the desktop applies these at
    /// its next launch. It still says so, because "3 of 10 applied" is the number that would
    /// have shown a colleague was three releases behind before his app refused to start.
    /// </summary>
    [Fact]
    public void PendingMigrationsReadAsBehindAndSayHowMany()
    {
        var diagnostics = Compare(Ids(1, 2), Ids(1, 2, 3, 4));

        Assert.Equal(SchemaCurrency.Behind, diagnostics.Currency);
        Assert.False(diagnostics.NeedsAttention);
        Assert.Equal(2, diagnostics.Pending.Count);
        Assert.Contains("2 of 4", diagnostics.Headline, StringComparison.Ordinal);
        Assert.Contains("next time Sati starts", diagnostics.Headline, StringComparison.Ordinal);
    }

    /// <summary>
    /// A database written by a newer release. Running an older build against it is how
    /// records get written by code that does not know the current shape, so this is
    /// surfaced rather than left for someone to notice.
    /// </summary>
    [Fact]
    public void ADatabaseAheadOfThisBuildNeedsAttention()
    {
        var diagnostics = Compare(Ids(1, 2, 3), Ids(1, 2));

        Assert.Equal(SchemaCurrency.Ahead, diagnostics.Currency);
        Assert.True(diagnostics.NeedsAttention);
        Assert.Single(diagnostics.Unrecognised);
    }

    /// <summary>
    /// Pending one way and unrecognised the other. This is the shape the SatiDemo history
    /// was actually in, and it is the one that most needs a person.
    /// </summary>
    [Fact]
    public void DivergedInBothDirectionsAsksForHelp()
    {
        var diagnostics = Compare(Ids(1, 2, 9), Ids(1, 2, 3));

        Assert.Equal(SchemaCurrency.Diverged, diagnostics.Currency);
        Assert.True(diagnostics.NeedsAttention);
        Assert.Contains("Send this screen to Josh", diagnostics.Headline, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing readable must not look like health. An empty comparison reporting "up to
    /// date" would be the panel's worst possible failure.
    /// </summary>
    [Fact]
    public void AnUnreadableHistoryIsUnknownRatherThanCurrent()
    {
        var diagnostics = Compare([], []);

        Assert.Equal(SchemaCurrency.Unknown, diagnostics.Currency);
        Assert.True(diagnostics.NeedsAttention);
        Assert.DoesNotContain("Up to date", diagnostics.Headline, StringComparison.Ordinal);
    }

    /// <summary>
    /// The id carries its own timestamp, so the newest id answers "when was this last
    /// migrated" without a separate date field that could disagree with it.
    /// </summary>
    [Fact]
    public void TheNewestAppliedIdIsReportedAndCarriesItsOwnTimestamp()
    {
        var diagnostics = Compare(Ids(1, 5, 3), Ids(1, 3, 5));

        Assert.Equal("20260830000005_Migration5", diagnostics.NewestApplied);
    }

    /// <summary>
    /// The history table is read with a BOM on at least one machine, and an id that differs
    /// only by an invisible character would be reported as both pending and unrecognised.
    /// </summary>
    [Fact]
    public void AByteOrderMarkDoesNotMakeAnIdLookLikeTwoDifferentOnes()
    {
        var diagnostics = Compare(["﻿20260210004007_InitialCreate"], ["20260210004007_InitialCreate"]);

        Assert.Equal(SchemaCurrency.Current, diagnostics.Currency);
        Assert.Empty(diagnostics.Pending);
        Assert.Empty(diagnostics.Unrecognised);
    }

    [Fact]
    public void CountsAndListsCannotDisagree()
    {
        var diagnostics = Compare(Ids(1, 2), Ids(1, 2, 3, 4, 5));

        Assert.Equal(2, diagnostics.AppliedCount);
        Assert.Equal(5, diagnostics.ExpectedCount);
        Assert.Equal(diagnostics.ExpectedCount - diagnostics.AppliedCount, diagnostics.Pending.Count);
    }

    /// <summary>
    /// The support block has to answer every question asked over the phone that evening,
    /// so a screenshot replaces a round trip.
    /// </summary>
    [Fact]
    public void TheSupportSummaryCarriesEnvironmentDatabaseIdentityAndCounts()
    {
        var diagnostics = DatabaseDiagnostics.Compare(
            "DEMO", "Hosted API", "SatiDemo", "Demo",
            Ids(1, 2), Ids(1, 2, 3),
            apiReleaseVersion: "1.2.33", apiContractRevision: "7C6F00E77F6E");

        var summary = diagnostics.SupportSummary;

        Assert.Contains("DEMO", summary, StringComparison.Ordinal);
        Assert.Contains("Hosted API", summary, StringComparison.Ordinal);
        Assert.Contains("SatiDemo", summary, StringComparison.Ordinal);
        Assert.Contains("2 of 3 applied", summary, StringComparison.Ordinal);
        Assert.Contains("1.2.33", summary, StringComparison.Ordinal);
        Assert.Contains("7C6F00E77F6E", summary, StringComparison.Ordinal);
    }

    /// <summary>The API lines are absent for a local database rather than shown empty.</summary>
    [Fact]
    public void ALocalDatabaseSummaryDoesNotClaimAnApiRelease()
    {
        var summary = Compare(Ids(1), Ids(1)).SupportSummary;

        Assert.DoesNotContain("API release", summary, StringComparison.Ordinal);
    }
}
