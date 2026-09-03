using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The pure rules behind rule-3 deletion: the 20-day window and the billing-integrity gate.
/// Per HANDOFF_CLIENT_DELETION_POLICY.md, the interesting cases are the permissive ones — a
/// record with notes, an assessment, or synthetic billing artifacts must remain deletable, since
/// that is exactly the content a record created to try something out will carry.
/// </summary>
public sealed class ConsumerDeletionRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ADayNineteenOldConsumerIsWithinTheWindow()
    {
        var createdAtUtc = Now.AddDays(-19);

        Assert.True(ConsumerDeletionRules.IsWithinDeletionWindow(createdAtUtc, Now));
    }

    [Fact]
    public void ADayTwentyOldConsumerIsOutsideTheWindow()
    {
        var createdAtUtc = Now.AddDays(-20);

        Assert.False(ConsumerDeletionRules.IsWithinDeletionWindow(createdAtUtc, Now));
    }

    [Fact]
    public void ADayTwentyOneOldConsumerIsOutsideTheWindow()
    {
        var createdAtUtc = Now.AddDays(-21);

        Assert.False(ConsumerDeletionRules.IsWithinDeletionWindow(createdAtUtc, Now));
    }

    // The sentinel A2 backfills pre-existing rows to — permanently outside the window, never a
    // guessed real creation date.
    [Fact]
    public void ARowPredatingTheColumnIsPermanentlyOutsideTheWindow()
    {
        Assert.False(ConsumerDeletionRules.IsWithinDeletionWindow(default, Now));
        Assert.False(ConsumerDeletionRules.IsWithinDeletionWindow(default, Now.AddYears(50)));
    }

    // ---- Billing-integrity gate: the permissive cases, which are the point of the window ----

    [Fact]
    public void NoBillingArtifactsAtAllDoesNotBlock()
    {
        Assert.False(ConsumerDeletionRules.HasTransmittedBilling(BillingIntegrityFacts.None));
    }

    [Fact]
    public void SyntheticOrGeneratedOnlyBillingDoesNotBlock()
    {
        // Generated (not Transmitted) BillingSubmissionEvents and synthetic outcomes/periods
        // are exactly what a record created to try something out will carry.
        var facts = new BillingIntegrityFacts(
            HasTransmittedBillingSubmissionEvent: false,
            HasNonSyntheticRemittanceClaimOutcome: false,
            HasSubmittedOrNonDraftBillingPeriod: false);

        Assert.False(ConsumerDeletionRules.HasTransmittedBilling(facts));
    }

    // ---- Billing-integrity gate: each condition independently blocks ----

    [Fact]
    public void ATransmittedBillingSubmissionEventBlocks()
    {
        var facts = BillingIntegrityFacts.None with { HasTransmittedBillingSubmissionEvent = true };

        Assert.True(ConsumerDeletionRules.HasTransmittedBilling(facts));
    }

    [Fact]
    public void ANonSyntheticRemittanceClaimOutcomeBlocks()
    {
        var facts = BillingIntegrityFacts.None with { HasNonSyntheticRemittanceClaimOutcome = true };

        Assert.True(ConsumerDeletionRules.HasTransmittedBilling(facts));
    }

    [Fact]
    public void ASubmittedOrNonDraftBillingPeriodBlocks()
    {
        var facts = BillingIntegrityFacts.None with { HasSubmittedOrNonDraftBillingPeriod = true };

        Assert.True(ConsumerDeletionRules.HasTransmittedBilling(facts));
    }

    [Fact]
    public void AttestationMustMatchExactlyAndDiffersFromTheTestDataAttestation()
    {
        Assert.True(ConsumerDeletionRules.HasValidConsumerAttestation(
            ConsumerDeletionRules.ConsumerAttestation));
        Assert.False(ConsumerDeletionRules.HasValidConsumerAttestation(
            TestDataDeletionRules.ConsumerAttestation));
        Assert.False(ConsumerDeletionRules.HasValidConsumerAttestation(null));
        Assert.False(ConsumerDeletionRules.HasValidConsumerAttestation(""));
    }
}
