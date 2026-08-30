using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// How an inbound 999, 277CA, or 835 becomes a record status.
///
/// This is the half of the claim exchange that survives the mock clearinghouse. A
/// simulator and Office Ally both produce X12; only the transport differs, so these are
/// written against documents rather than against whatever produced them.
/// </summary>
public sealed class ClaimResponseReaderTests
{
    private static string Interchange(string usageIndicator, string body) =>
        $"ISA*00*          *00*          *ZZ*SUBMITTER      *ZZ*RECEIVER       *260830*1200*^*00501*000000123*0*{usageIndicator}*:~\n"
        + body;

    private static string Ack999(string ak9) => Interchange("T",
        $"ST*999*0001~\nAK1*HC*1*005010X222A1~\nAK2*837*0001*005010X222A1~\n{ak9}\nSE*5*0001~\n");

    private static string Ack277(params string[] statuses) => Interchange("T",
        "ST*277*0002~\nBHT*0085*08*REF*20260830*1200*TH~\n"
        + string.Join("\n", statuses.Select(status => $"STC*{status}*20260830*WQ*100.00~"))
        + "\nSE*4*0002~\n");

    // ISA15 is the X12 usage indicator. It is what decides whether records written from a
    // document are marked synthetic, so both readings are worth pinning.
    [Theory]
    [InlineData("T", true)]
    [InlineData("P", false)]
    public void TheUsageIndicatorDecidesWhetherTheInterchangeIsATest(string indicator, bool expected)
    {
        var envelope = ClaimResponseReader.ReadEnvelope(
            Interchange(indicator, "ST*835*0003~\nSE*2*0003~\n"));

        Assert.Equal(expected, envelope.IsTestInterchange);
        Assert.Equal(ClaimResponseKind.RemittanceAdvice, envelope.Kind);
        Assert.Equal("000000123", envelope.ControlNumber);
    }

    [Fact]
    public void AnUnrecognisedTransactionIsNotGuessedAt()
    {
        var envelope = ClaimResponseReader.ReadEnvelope(
            Interchange("T", "ST*270*0004~\nSE*2*0004~\n"));

        Assert.Equal(ClaimResponseKind.Unrecognised, envelope.Kind);
    }

    [Theory]
    [InlineData("AK9*A*1*1*1~", BillingSubmissionStage.FunctionalAccepted)]
    [InlineData("AK9*E*1*1*0~", BillingSubmissionStage.FunctionalAccepted)]
    [InlineData("AK9*R*1*1*0~", BillingSubmissionStage.FunctionalRejected)]
    public void A999IsReadAsSyntaxAcceptedOrRejected(string ak9, BillingSubmissionStage expected)
    {
        var result = ClaimResponseReader.ReadAcknowledgement(Ack999(ak9));

        Assert.Equal(expected, result.Stage);
    }

    /// <summary>
    /// A file can pass syntax and still have every claim rejected, so the two stages stay
    /// distinct. Collapsing them would lose the difference between "the clearinghouse
    /// could read it" and "the payer will pay it".
    /// </summary>
    [Fact]
    public void EveryClaimAcceptedIsClaimAccepted()
    {
        var result = ClaimResponseReader.ReadAcknowledgement(Ack277("A1:19:PR", "A1:19:PR"));

        Assert.Equal(BillingSubmissionStage.ClaimAccepted, result.Stage);
    }

    [Fact]
    public void EveryClaimRejectedIsClaimRejected()
    {
        var result = ClaimResponseReader.ReadAcknowledgement(Ack277("R3:187", "R3:187"));

        Assert.Equal(BillingSubmissionStage.ClaimRejected, result.Stage);
    }

    [Fact]
    public void AMixedAcknowledgementIsPartialAndSaysHowMany()
    {
        var result = ClaimResponseReader.ReadAcknowledgement(Ack277("A1:19:PR", "R3:187", "A1:19:PR"));

        Assert.Equal(BillingSubmissionStage.PartiallyAccepted, result.Stage);
        Assert.Contains("2 of 3", result.Explanation, StringComparison.Ordinal);
    }

    /// <summary>An acknowledgement with no status must not read as acceptance.</summary>
    [Fact]
    public void AnAcknowledgementWithNoClaimStatusIsNotTreatedAsAccepted()
    {
        var result = ClaimResponseReader.ReadAcknowledgement(Interchange("T",
            "ST*277*0002~\nBHT*0085*08*REF*20260830*1200*TH~\nSE*3*0002~\n"));

        Assert.Equal(BillingSubmissionStage.ClaimRejected, result.Stage);
    }

    [Fact]
    public void An835IsReadIntoPerClaimOutcomesAndDepositTotals()
    {
        var remittance = ClaimResponseReader.ReadRemittance(Interchange("T",
            """
            ST*835*0003~
            BPR*I*430.00*C*ACH************20260830~
            TRN*1*PAYMENT-9001*1999999984~
            N1*PR*SYNTHETIC PAYER*XV*MCDME~
            CLP*CLAIM-1*1*200.00*200.00*0*MC*SYN000001*11*1~
            CLP*CLAIM-2*1*200.00*150.00*10*MC*SYN000002*11*1~
            CAS*CO*45*50.00~
            CLP*CLAIM-3*4*200.00*0.00*0*MC*SYN000003*11*1~
            CAS*CO*29*200.00~
            CLP*CLAIM-4*22*100.00*-100.00*0*MC*SYN000004*11*1~
            PLB*1999999984*20261231*WO:REF9*20.00~
            SE*12*0003~
            """));

        Assert.Equal(4, remittance.Claims.Count);
        Assert.Equal("PAYMENT-9001", remittance.PaymentReference);
        Assert.Equal("SYNTHETIC PAYER", remittance.PayerName);
        Assert.Equal(new DateTime(2026, 8, 30), remittance.PaymentDate);
        Assert.Equal(430.00m, remittance.RemittancePaymentAmount);
        Assert.Equal(20.00m, remittance.ProviderLevelAdjustment);

        Assert.Equal(RemittanceClaimStatus.Paid, remittance.Claims[0].Status);
        Assert.Equal(RemittanceClaimStatus.PartiallyPaid, remittance.Claims[1].Status);
        Assert.Equal(RemittanceClaimStatus.Denied, remittance.Claims[2].Status);
        Assert.Equal(RemittanceClaimStatus.Reversed, remittance.Claims[3].Status);

        Assert.Equal(50.00m, remittance.Claims[1].AdjustmentAmount);
        Assert.Equal("CO", remittance.Claims[1].GroupCode);
        Assert.Equal("45", remittance.Claims[1].ReasonCode);
        Assert.Equal(10m, remittance.Claims[1].PatientResponsibilityAmount);
    }

    /// <summary>
    /// A claim the payer processed but paid nothing on is not the same as one it denied.
    /// Calling both denied would put work in the wrong queue and overstate what the payer
    /// actually said.
    /// </summary>
    [Fact]
    public void ProcessedWithNoPaymentNeedsReviewRatherThanReadingAsDenied()
    {
        var remittance = ClaimResponseReader.ReadRemittance(Interchange("T",
            """
            ST*835*0003~
            BPR*I*0.00*C*NON************20260830~
            CLP*CLAIM-9*1*150.00*0.00*0*MC*SYN000009*11*1~
            SE*4*0003~
            """));

        var claim = Assert.Single(remittance.Claims);
        Assert.Equal(RemittanceClaimStatus.NeedsReview, claim.Status);
        Assert.NotEqual(RemittanceClaimStatus.Denied, claim.Status);
    }

    /// <summary>
    /// The claim total and the deposit are read separately, because a provider-level
    /// adjustment makes them differ with every claim correct. Conflating them is what
    /// makes a deposit look unreconcilable.
    /// </summary>
    [Fact]
    public void TheClaimTotalAndTheDepositAreReadSeparately()
    {
        var remittance = ClaimResponseReader.ReadRemittance(Interchange("T",
            """
            ST*835*0003~
            BPR*I*180.00*C*ACH************20260830~
            CLP*CLAIM-1*1*200.00*200.00*0*MC*SYN000001*11*1~
            PLB*1999999984*20261231*WO:REF9*20.00~
            SE*5*0003~
            """));

        Assert.Equal(200.00m, remittance.ClaimPaymentTotal);
        Assert.Equal(20.00m, remittance.ProviderLevelAdjustment);
        Assert.Equal(180.00m, remittance.RemittancePaymentAmount);
    }
}
