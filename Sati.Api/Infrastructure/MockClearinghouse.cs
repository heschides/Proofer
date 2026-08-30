using System.Globalization;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

/// <param name="FunctionalAcknowledgement">The 999. Always produced.</param>
/// <param name="ClaimAcknowledgement">The 277CA, or null when the file was rejected outright.</param>
/// <param name="RemittanceAdvice">The 835, or null when nothing reached payment.</param>
internal sealed record MockClearinghouseDocuments(
    string FunctionalAcknowledgement,
    string? ClaimAcknowledgement,
    string? RemittanceAdvice);

/// <summary>
/// Fabricates the responses a clearinghouse and payer would return for a submitted 837P.
/// </summary>
/// <remarks>
/// <para>
/// This is scaffolding, and it is deliberately the only part of the claim exchange that
/// is. Interpretation lives in <see cref="ClaimResponseReader"/> and ingestion in
/// <see cref="ClaimResponseIngestion"/>, both of which take documents and neither of
/// which knows or cares that this class exists. The day Office Ally replaces it, this
/// file is deleted and nothing else moves.
/// </para>
/// <para>
/// Two independent refusals keep it away from real claims. It parses only interchanges
/// whose ISA15 usage indicator is <c>T</c>, and it stamps <c>T</c> on everything it
/// emits, so nothing it produces can be ingested as production data even if it somehow
/// ran somewhere unintended. The route that calls it is separately gated to Demo.
/// </para>
/// </remarks>
internal static class MockClearinghouse
{
    private const string TestUsageIndicator = "T";

    public static MockClearinghouseDocuments Respond(
        string edi837,
        MockClearinghouseScenario scenario,
        DateTime respondedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(edi837);

        var segments = Parse(edi837);
        var isa = Find(segments, "ISA")
            ?? throw new InvalidOperationException("The submitted file has no ISA interchange header.");
        if (isa.Length <= 15 || isa[15].Trim() != TestUsageIndicator)
        {
            throw new InvalidOperationException(
                "The mock clearinghouse accepts test interchanges only. ISA15 must be 'T'.");
        }

        var st = Find(segments, "ST");
        if (st is not { Length: > 3 } || st[1].Trim() != "837")
            throw new InvalidOperationException("The submitted file is not an 837 transaction.");

        var claims = ReadClaims(segments);
        if (claims.Count == 0)
            throw new InvalidOperationException("The submitted 837 contains no claims.");

        var stamp = respondedAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = respondedAtUtc.ToString("HHmm", CultureInfo.InvariantCulture);

        // A rejected file never reaches the payer, so there is nothing further to return.
        // Producing a 277CA anyway would let the read models show a claim verdict that no
        // payer ever gave.
        if (scenario == MockClearinghouseScenario.SyntaxRejected)
        {
            return new MockClearinghouseDocuments(
                Wrap(stamp, time, Transaction("999", "0001",
                    "AK1*HC*1*005010X222A1~\n" +
                    $"AK2*837*{Control(st)}*005010X222A1~\n" +
                    "IK3*CLM*1**8~\n" +
                    "IK5*R*5~\n" +
                    "AK9*R*1*1*0~\n")),
                null,
                null);
        }

        var accepted999 = Wrap(stamp, time, Transaction("999", "0001",
            "AK1*HC*1*005010X222A1~\n" +
            $"AK2*837*{Control(st)}*005010X222A1~\n" +
            "IK5*A~\n" +
            "AK9*A*1*1*1~\n"));

        var acknowledgement = BuildAcknowledgement(claims, scenario, stamp, time);
        if (scenario is MockClearinghouseScenario.ClaimsRejected)
            return new MockClearinghouseDocuments(accepted999, acknowledgement, null);

        var remittance = BuildRemittance(claims, scenario, stamp, time);
        return new MockClearinghouseDocuments(accepted999, acknowledgement, remittance);
    }

    private static string BuildAcknowledgement(
        IReadOnlyList<MockClaim> claims,
        MockClearinghouseScenario scenario,
        string stamp,
        string time)
    {
        var body = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"BHT*0085*08*MOCK277*{stamp}*{time}*TH~\n");

        for (var index = 0; index < claims.Count; index++)
        {
            // PartiallyAccepted rejects every other claim so both halves are present
            // whatever the batch size, rather than depending on a particular count.
            var rejected = scenario switch
            {
                MockClearinghouseScenario.ClaimsRejected => true,
                MockClearinghouseScenario.PartiallyAccepted => index % 2 == 1,
                _ => false
            };
            var status = rejected ? "R3:187" : "A1:19:PR";
            body.Append(CultureInfo.InvariantCulture,
                $"STC*{status}*{stamp}*WQ*{Money(claims[index].Charge)}~\n");
            body.Append(CultureInfo.InvariantCulture, $"REF*1K*{claims[index].ClaimId}~\n");
        }

        return Wrap(stamp, time, Transaction("277", "0002", body.ToString()));
    }

    private static string BuildRemittance(
        IReadOnlyList<MockClaim> claims,
        MockClearinghouseScenario scenario,
        string stamp,
        string time)
    {
        var rows = new StringBuilder();
        decimal paidTotal = 0m;

        for (var index = 0; index < claims.Count; index++)
        {
            var claim = claims[index];

            // PartiallyAccepted already rejected the odd claims at acknowledgement, so
            // they are absent from the remittance rather than paid.
            if (scenario == MockClearinghouseScenario.PartiallyAccepted && index % 2 == 1)
                continue;

            var (statusCode, paid, groupCode, reasonCode) = scenario switch
            {
                MockClearinghouseScenario.Denied => ("4", 0m, "CO", "29"),
                MockClearinghouseScenario.Reversal => ("22", -claim.Charge, "CO", "45"),
                MockClearinghouseScenario.PartialPayment =>
                    ("1", decimal.Round(claim.Charge * 0.8m, 2, MidpointRounding.AwayFromZero), "CO", "45"),
                _ => ("1", claim.Charge, (string?)null, (string?)null)
            };

            paidTotal += paid;
            var adjustment = claim.Charge - paid;

            rows.Append(CultureInfo.InvariantCulture, $"LX*{index + 1}~\n");
            rows.Append(CultureInfo.InvariantCulture,
                $"CLP*{claim.ClaimId}*{statusCode}*{Money(claim.Charge)}*{Money(paid)}*0*MC*MOCK{index + 1:D6}*11*1~\n");
            if (groupCode is not null && adjustment != 0m)
            {
                rows.Append(CultureInfo.InvariantCulture,
                    $"CAS*{groupCode}*{reasonCode}*{Money(adjustment)}~\n");
            }
            rows.Append(CultureInfo.InvariantCulture,
                $"NM1*QC*1*{claim.LastName}*{claim.FirstName}****MI*{claim.MemberId}~\n");
            rows.Append(CultureInfo.InvariantCulture,
                $"SVC*{claim.Procedure}*{Money(claim.Charge)}*{Money(paid)}~\n");
            rows.Append(CultureInfo.InvariantCulture,
                $"DTM*472*{claim.ServiceDate:yyyyMMdd}~\n");
        }

        // A provider-level adjustment moves money without belonging to any claim, which is
        // the only honest way to make a deposit differ from the claim total while every
        // claim is correct. That is the state the reconciliation screen exists for.
        var providerAdjustment = scenario == MockClearinghouseScenario.ProviderLevelAdjustment ? 25.00m : 0m;
        var deposit = paidTotal - providerAdjustment;

        var body = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"BPR*I*{Money(deposit)}*C*ACH************{stamp}~\n")
            .Append(CultureInfo.InvariantCulture, $"TRN*1*MOCK-{stamp}-{time}*1999999984~\n")
            .Append("N1*PR*MOCK PAYER*XV*MCDME~\n")
            .Append("N1*PE*EXAMPLE AGENCY*XX*1999999984~\n")
            .Append(rows);

        if (providerAdjustment != 0m)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"PLB*1999999984*{stamp}*WO:MOCKREF*{Money(providerAdjustment)}~\n");
        }

        return Wrap(stamp, time, Transaction("835", "0003", body.ToString()));
    }

    /// <summary>
    /// Wraps a transaction set in an interchange stamped as a test. Everything this class
    /// emits carries ISA15 = T, so anything ingested from it identifies itself as
    /// synthetic from the document rather than from configuration.
    /// </summary>
    private static string Wrap(string stamp, string time, string transaction) =>
        $"ISA*00*          *00*          *ZZ*MOCKCLEARING   *ZZ*SATI           *" +
        $"{stamp[2..]}*{time}*^*00501*000000001*0*{TestUsageIndicator}*:~\n" +
        "GS*HP*MOCKCLEARING*SATI*" + stamp + "*" + time + "*1*X*005010X221A1~\n" +
        transaction +
        "GE*1*1~\n" +
        "IEA*1*000000001~\n";

    private static string Transaction(string type, string control, string body)
    {
        var transaction = $"ST*{type}*{control}~\n{body}";
        var segmentCount = transaction.Count(character => character == '~') + 1;
        return transaction + $"SE*{segmentCount}*{control}~\n";
    }

    private static List<MockClaim> ReadClaims(IReadOnlyList<string[]> segments)
    {
        var claims = new List<MockClaim>();
        string firstName = string.Empty, lastName = string.Empty, memberId = string.Empty;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];

            if (segment[0] == "NM1" && segment.Length > 9 && segment[1].Trim() == "IL")
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
                if (candidate[0] == "DTP" && candidate.Length > 3 && candidate[1].Trim() == "472")
                {
                    DateTime.TryParseExact(candidate[3], "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out serviceDate);
                }
            }

            var charge = decimal.TryParse(segment[2], NumberStyles.Number,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

            claims.Add(new MockClaim(
                segment[1].Trim(), charge, procedure, serviceDate, firstName, lastName, memberId));
        }

        return claims;
    }

    private static string Control(string[] st) => st.Length > 2 ? st[2].Trim() : "0001";

    private static List<string[]> Parse(string edi) => edi
        .Split('~', StringSplitOptions.RemoveEmptyEntries)
        .Select(segment => segment.Trim().Split('*'))
        .Where(segment => segment.Length > 0 && segment[0].Length > 0)
        .ToList();

    private static string[]? Find(IEnumerable<string[]> segments, string name) =>
        segments.FirstOrDefault(segment => segment[0] == name);

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record MockClaim(
        string ClaimId,
        decimal Charge,
        string Procedure,
        DateTime ServiceDate,
        string FirstName,
        string LastName,
        string MemberId);
}
