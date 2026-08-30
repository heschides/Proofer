using System.Globalization;
using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Exercises Sati's outbound claim with a deterministic, in-memory clearinghouse/payer double.
/// This is not an X12 validator, payer certification, or network transport test.
/// </summary>
public sealed class SyntheticClaimExchangeTests
{
    [Fact]
    public void Test837ReceivesAcknowledgementsAndABalancedSynthetic835()
    {
        var generatedAt = new DateTime(2026, 8, 29, 9, 30, 0, DateTimeKind.Local);
        var claim = CreateSubmittedPeriod();
        var edi837 = EdiGenerator.Generate(claim, isTest: true, generatedAt, "123456789");

        var response = SyntheticClaimExchange.ProcessTestClaim(edi837, generatedAt.AddMinutes(2));

        Assert.Contains("ST*999*", response.FunctionalAcknowledgement);
        Assert.Contains("AK2*837*0001*005010X222A1~", response.FunctionalAcknowledgement);
        Assert.Contains("IK5*A~", response.FunctionalAcknowledgement);
        Assert.Contains("AK9*A*1*1*1~", response.FunctionalAcknowledgement);

        Assert.Contains("ST*277*", response.ClaimAcknowledgement);
        Assert.Contains("STC*A1:19:PR*", response.ClaimAcknowledgement);
        Assert.Contains("REF*1K*77-99~", response.ClaimAcknowledgement);

        Assert.Contains("ST*835*", response.RemittanceAdvice);
        Assert.Contains("CLP*77-99*1*33.25*26.60*0*MC*SYN000001*11*1~", response.RemittanceAdvice);
        Assert.Contains("CAS*CO*45*6.65~", response.RemittanceAdvice);
        Assert.Contains("SVC*HC:G9012:HI*33.25*26.60~", response.RemittanceAdvice);
        Assert.Equal(33.25m, response.TotalSubmittedCharge);
        Assert.Equal(26.60m, response.TotalPayment);
        Assert.Equal(response.TotalSubmittedCharge,
            response.TotalPayment + response.TotalContractualAdjustment);
    }

    [Fact]
    public void SimulatorRefusesAProduction837()
    {
        var generatedAt = new DateTime(2026, 8, 29, 9, 30, 0, DateTimeKind.Local);
        var edi837 = EdiGenerator.Generate(
            CreateSubmittedPeriod(), isTest: false, generatedAt, "123456789");

        var error = Assert.Throws<InvalidOperationException>(() =>
            SyntheticClaimExchange.ProcessTestClaim(edi837, generatedAt.AddMinutes(2)));

        Assert.Equal("The synthetic claim exchange accepts test 837 files only.", error.Message);
    }

    private static BillingPeriod CreateSubmittedPeriod()
    {
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "SYNTHETIC PAYER", "MCDME");
        return new BillingPeriod
        {
            Id = 77,
            UserId = 12,
            Month = 8,
            Year = 2026,
            Status = BillingStatus.Submitted,
            Lines =
            [
                new ClaimLine
                {
                    Id = 88,
                    NoteId = 99,
                    DateOfService = new DateTime(2026, 8, 12),
                    ProcedureCode = "G9012",
                    ProcedureModifier = "HI",
                    Units = 1.33m,
                    ChargeAmount = 33.25m,
                    ClientMaineCareId = "987654321",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11,
                    ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(snapshot)
                }
            ]
        };
    }
}

internal static class SyntheticClaimExchange
{
    private const decimal PaidPercentage = 0.80m;

    public static SyntheticClaimExchangeResult ProcessTestClaim(string edi837, DateTime respondedAt)
    {
        var segments = ParseSegments(edi837);
        var isa = Required(segments, "ISA");
        if (isa.Length <= 15 || isa[15] != "T")
            throw new InvalidOperationException("The synthetic claim exchange accepts test 837 files only.");

        var st = Required(segments, "ST");
        if (st.Length <= 3 || st[1] != "837" || st[3] != "005010X222A1")
            throw new InvalidOperationException("The file is not Sati's supported 837P transaction version.");

        var claims = ReadClaims(segments);
        if (claims.Count == 0)
            throw new InvalidOperationException("The test 837 contains no professional claims.");

        var submitted = claims.Sum(claim => claim.Charge);
        var paid = claims.Sum(claim => claim.Payment);
        var adjustment = claims.Sum(claim => claim.Adjustment);
        var date = respondedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = respondedAt.ToString("HHmm", CultureInfo.InvariantCulture);

        var acknowledgement999 = Transaction("999", "0001",
            $"AK1*HC*1*005010X222A1~\n" +
            $"AK2*837*{st[2]}*005010X222A1~\n" +
            "IK5*A~\n" +
            "AK9*A*1*1*1~\n");

        var claimRows = string.Join("\n", claims.Select(claim =>
            $"REF*1K*{claim.ClaimId}~"));
        var acknowledgement277 = Transaction("277", "0002",
            $"BHT*0085*08*SYNTHETIC277*{date}*{time}*TH~\n" +
            $"STC*A1:19:PR*{date}*WQ*{Money(submitted)}~\n" +
            claimRows + "\n");

        var remittanceRows = string.Join("\n", claims.Select((claim, index) =>
            $"LX*{index + 1}~\n" +
            $"CLP*{claim.ClaimId}*1*{Money(claim.Charge)}*{Money(claim.Payment)}*0*MC*SYN{index + 1:D6}*11*1~\n" +
            $"CAS*CO*45*{Money(claim.Adjustment)}~\n" +
            $"NM1*QC*1*{claim.SubscriberLastName}*{claim.SubscriberFirstName}****MI*{claim.MemberId}~\n" +
            $"SVC*{claim.Procedure}*{Money(claim.Charge)}*{Money(claim.Payment)}~\n" +
            $"DTM*472*{claim.ServiceDate:yyyyMMdd}~"));
        var remittance835 = Transaction("835", "0003",
            $"BPR*I*{Money(paid)}*C*NON************{date}~\n" +
            $"TRN*1*SYNTHETIC835*1999999984~\n" +
            "N1*PR*SYNTHETIC PAYER*XV*MCDME~\n" +
            "N1*PE*EXAMPLE AGENCY*XX*1999999984~\n" +
            remittanceRows + "\n");

        return new SyntheticClaimExchangeResult(
            acknowledgement999,
            acknowledgement277,
            remittance835,
            submitted,
            paid,
            adjustment);
    }

    private static List<SyntheticClaim> ReadClaims(IReadOnlyList<string[]> segments)
    {
        var claims = new List<SyntheticClaim>();
        string firstName = string.Empty;
        string lastName = string.Empty;
        string memberId = string.Empty;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment[0] == "NM1" && segment.Length > 9 && segment[1] == "IL")
            {
                lastName = segment[3];
                firstName = segment[4];
                memberId = segment[9];
                continue;
            }
            if (segment[0] != "CLM" || segment.Length <= 2)
                continue;

            var procedure = string.Empty;
            var serviceDate = default(DateTime);
            for (var detail = index + 1; detail < segments.Count && segments[detail][0] != "CLM"; detail++)
            {
                var candidate = segments[detail];
                if (candidate[0] == "SV1" && candidate.Length > 1)
                    procedure = candidate[1];
                if (candidate[0] == "DTP" && candidate.Length > 3 && candidate[1] == "472")
                {
                    serviceDate = DateTime.ParseExact(
                        candidate[3], "yyyyMMdd", CultureInfo.InvariantCulture);
                }
            }

            var charge = decimal.Parse(segment[2], CultureInfo.InvariantCulture);
            var payment = decimal.Round(charge * PaidPercentage, 2, MidpointRounding.AwayFromZero);
            claims.Add(new SyntheticClaim(
                segment[1], charge, payment, charge - payment, procedure,
                serviceDate, firstName, lastName, memberId));
        }
        return claims;
    }

    private static List<string[]> ParseSegments(string edi) => edi
        .Split('~', StringSplitOptions.RemoveEmptyEntries)
        .Select(segment => segment.Trim().Split('*'))
        .ToList();

    private static string[] Required(IEnumerable<string[]> segments, string name) =>
        segments.FirstOrDefault(segment => segment[0] == name)
        ?? throw new InvalidOperationException($"The test 837 is missing its {name} segment.");

    private static string Transaction(string type, string control, string body)
    {
        var transaction = $"ST*{type}*{control}~\n{body}";
        var segmentCount = transaction.Count(character => character == '~') + 1;
        return transaction + $"SE*{segmentCount}*{control}~\n";
    }

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record SyntheticClaim(
        string ClaimId,
        decimal Charge,
        decimal Payment,
        decimal Adjustment,
        string Procedure,
        DateTime ServiceDate,
        string SubscriberFirstName,
        string SubscriberLastName,
        string MemberId);
}

internal sealed record SyntheticClaimExchangeResult(
    string FunctionalAcknowledgement,
    string ClaimAcknowledgement,
    string RemittanceAdvice,
    decimal TotalSubmittedCharge,
    decimal TotalPayment,
    decimal TotalContractualAdjustment);
