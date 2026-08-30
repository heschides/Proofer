using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The mock clearinghouse, exercised through the reader that will still be here when
/// Office Ally replaces it.
///
/// These deliberately assert on what <see cref="ClaimResponseReader"/> makes of the
/// documents rather than on the documents themselves. A test that asserted the exact
/// segment text would pass while producing X12 that nothing could interpret, which is the
/// failure worth catching: the two halves only matter if they agree.
/// </summary>
public sealed class MockClearinghouseTests
{
    private static readonly DateTime GeneratedAt = new(2026, 8, 30, 9, 30, 0, DateTimeKind.Utc);

    private static string TestClaim(int claimCount = 2, decimal charge = 200m)
    {
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "MOCK PAYER", "MCDME");

        var period = new ServerBillingPeriod { Id = 77, UserId = 12, Month = 8, Year = 2026, Status = 1 };
        for (var index = 0; index < claimCount; index++)
        {
            period.Lines.Add(new ServerClaimLine
            {
                Id = 500 + index,
                NoteId = 900 + index,
                BillingPeriodId = 77,
                DateOfService = new DateTime(2026, 8, 12),
                ProcedureCode = "G9012",
                Units = 1,
                ChargeAmount = charge,
                ClientMaineCareId = "987654321",
                RenderingProviderNpi = "1999999984",
                DiagnosisCode = "F89",
                PlaceOfService = 11,
                ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(snapshot)
            });
        }

        return ServerEdiGenerator.Generate(period, isTest: true, GeneratedAt, "123456789");
    }

    /// <summary>
    /// The first independent guard. The mock refuses a production interchange outright, so
    /// even if its Demo-only route were somehow reachable it could not fabricate a
    /// response to a real claim file.
    /// </summary>
    [Fact]
    public void TheMockRefusesAProductionInterchange()
    {
        var period = new ServerBillingPeriod { Id = 77, UserId = 12, Month = 8, Year = 2026, Status = 1 };
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "MOCK PAYER", "MCDME");
        period.Lines.Add(new ServerClaimLine
        {
            Id = 500, NoteId = 900, BillingPeriodId = 77,
            DateOfService = new DateTime(2026, 8, 12), ProcedureCode = "G9012",
            Units = 1, ChargeAmount = 200m, ClientMaineCareId = "987654321",
            RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11,
            ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(snapshot)
        });
        var production837 = ServerEdiGenerator.Generate(period, isTest: false, GeneratedAt, "123456789");

        var error = Assert.Throws<InvalidOperationException>(() =>
            MockClearinghouse.Respond(production837, MockClearinghouseScenario.Accepted, GeneratedAt));

        Assert.Contains("test interchanges only", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second independent guard. Everything the mock emits is stamped as a test
    /// interchange, so ingesting it records synthetic rows from the document's own
    /// declaration rather than from configuration.
    /// </summary>
    [Theory]
    [InlineData(MockClearinghouseScenario.Accepted)]
    [InlineData(MockClearinghouseScenario.SyntaxRejected)]
    [InlineData(MockClearinghouseScenario.ClaimsRejected)]
    [InlineData(MockClearinghouseScenario.PartiallyAccepted)]
    [InlineData(MockClearinghouseScenario.PartialPayment)]
    [InlineData(MockClearinghouseScenario.Denied)]
    [InlineData(MockClearinghouseScenario.ProviderLevelAdjustment)]
    [InlineData(MockClearinghouseScenario.Reversal)]
    public void EveryDocumentTheMockEmitsIsATestInterchange(MockClearinghouseScenario scenario)
    {
        var documents = MockClearinghouse.Respond(TestClaim(), scenario, GeneratedAt);

        foreach (var document in new[]
                 {
                     documents.FunctionalAcknowledgement,
                     documents.ClaimAcknowledgement,
                     documents.RemittanceAdvice
                 }.Where(value => value is not null))
        {
            Assert.True(ClaimResponseReader.ReadEnvelope(document!).IsTestInterchange);
        }
    }

    [Fact]
    public void AcceptedRunsTheWholeWayToPaidInFull()
    {
        var documents = MockClearinghouse.Respond(TestClaim(), MockClearinghouseScenario.Accepted, GeneratedAt);

        Assert.Equal(BillingSubmissionStage.FunctionalAccepted,
            ClaimResponseReader.ReadAcknowledgement(documents.FunctionalAcknowledgement).Stage);
        Assert.Equal(BillingSubmissionStage.ClaimAccepted,
            ClaimResponseReader.ReadAcknowledgement(documents.ClaimAcknowledgement!).Stage);

        var remittance = ClaimResponseReader.ReadRemittance(documents.RemittanceAdvice!);
        Assert.Equal(2, remittance.Claims.Count);
        Assert.All(remittance.Claims, claim => Assert.Equal(RemittanceClaimStatus.Paid, claim.Status));
        Assert.Equal(400m, remittance.ClaimPaymentTotal);
        Assert.Equal(400m, remittance.RemittancePaymentAmount);
    }

    /// <summary>
    /// A rejected file never reaches the payer, so there is nothing after the 999.
    /// Producing a 277CA anyway would let the read models show a claim verdict no payer
    /// ever gave.
    /// </summary>
    [Fact]
    public void ASyntaxRejectionProducesNothingAfterThe999()
    {
        var documents = MockClearinghouse.Respond(TestClaim(), MockClearinghouseScenario.SyntaxRejected, GeneratedAt);

        Assert.Equal(BillingSubmissionStage.FunctionalRejected,
            ClaimResponseReader.ReadAcknowledgement(documents.FunctionalAcknowledgement).Stage);
        Assert.Null(documents.ClaimAcknowledgement);
        Assert.Null(documents.RemittanceAdvice);
    }

    [Fact]
    public void ClaimsRejectedStopsBeforePaymentBecauseNothingWasAccepted()
    {
        var documents = MockClearinghouse.Respond(TestClaim(), MockClearinghouseScenario.ClaimsRejected, GeneratedAt);

        Assert.Equal(BillingSubmissionStage.FunctionalAccepted,
            ClaimResponseReader.ReadAcknowledgement(documents.FunctionalAcknowledgement).Stage);
        Assert.Equal(BillingSubmissionStage.ClaimRejected,
            ClaimResponseReader.ReadAcknowledgement(documents.ClaimAcknowledgement!).Stage);
        Assert.Null(documents.RemittanceAdvice);
    }

    /// <summary>
    /// The claims the payer rejected must not then appear as paid. A batch where the
    /// acknowledgement and the remittance disagree about which claims exist is exactly
    /// the state the submission home cannot render honestly.
    /// </summary>
    [Fact]
    public void PartiallyAcceptedPaysOnlyTheClaimsThePayerAccepted()
    {
        var documents = MockClearinghouse.Respond(
            TestClaim(claimCount: 4), MockClearinghouseScenario.PartiallyAccepted, GeneratedAt);

        Assert.Equal(BillingSubmissionStage.PartiallyAccepted,
            ClaimResponseReader.ReadAcknowledgement(documents.ClaimAcknowledgement!).Stage);

        var remittance = ClaimResponseReader.ReadRemittance(documents.RemittanceAdvice!);
        Assert.Equal(2, remittance.Claims.Count);
        Assert.All(remittance.Claims, claim => Assert.Equal(RemittanceClaimStatus.Paid, claim.Status));
    }

    [Fact]
    public void PartialPaymentCarriesAContractualAdjustmentThatExplainsTheShortfall()
    {
        var documents = MockClearinghouse.Respond(TestClaim(), MockClearinghouseScenario.PartialPayment, GeneratedAt);
        var remittance = ClaimResponseReader.ReadRemittance(documents.RemittanceAdvice!);

        Assert.All(remittance.Claims, claim =>
        {
            Assert.Equal(RemittanceClaimStatus.PartiallyPaid, claim.Status);
            Assert.Equal("CO", claim.GroupCode);
            Assert.Equal("45", claim.ReasonCode);
            Assert.Equal(claim.BilledAmount - claim.PaidAmount, claim.AdjustmentAmount);
        });
    }

    [Fact]
    public void DeniedIsReadAsDeniedRatherThanAsPaidNothing()
    {
        var documents = MockClearinghouse.Respond(TestClaim(), MockClearinghouseScenario.Denied, GeneratedAt);
        var remittance = ClaimResponseReader.ReadRemittance(documents.RemittanceAdvice!);

        Assert.All(remittance.Claims, claim =>
        {
            Assert.Equal(RemittanceClaimStatus.Denied, claim.Status);
            Assert.Equal("29", claim.ReasonCode);
        });
    }

    [Fact]
    public void AReversalIsReadAsAReversalAndNotAsAnUnderpayment()
    {
        var documents = MockClearinghouse.Respond(TestClaim(), MockClearinghouseScenario.Reversal, GeneratedAt);
        var remittance = ClaimResponseReader.ReadRemittance(documents.RemittanceAdvice!);

        Assert.All(remittance.Claims, claim => Assert.Equal(RemittanceClaimStatus.Reversed, claim.Status));
    }

    /// <summary>
    /// The state the deposit reconciliation screen exists for: every claim correct, and
    /// the deposit still short of the claim total because money moved at provider level.
    /// </summary>
    [Fact]
    public void AProviderLevelAdjustmentMakesTheDepositDifferWithEveryClaimCorrect()
    {
        var documents = MockClearinghouse.Respond(
            TestClaim(), MockClearinghouseScenario.ProviderLevelAdjustment, GeneratedAt);
        var remittance = ClaimResponseReader.ReadRemittance(documents.RemittanceAdvice!);

        Assert.All(remittance.Claims, claim => Assert.Equal(RemittanceClaimStatus.Paid, claim.Status));
        Assert.Equal(400m, remittance.ClaimPaymentTotal);
        Assert.Equal(25m, remittance.ProviderLevelAdjustment);
        Assert.Equal(375m, remittance.RemittancePaymentAmount);
        Assert.NotEqual(remittance.ClaimPaymentTotal, remittance.RemittancePaymentAmount);
    }
}
