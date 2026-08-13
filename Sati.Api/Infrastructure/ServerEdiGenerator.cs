using System.Globalization;
using System.Text;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal static class ServerEdiGenerator
{
    private const string OaReceiverId = "330897513";
    private const string VersionCode = "005010X222A1";
    private const string SubSep = ":";

    public static string Generate(
        ServerBillingPeriod period,
        bool isTest,
        DateTime generatedAt,
        string controlNumber)
    {
        var rows = period.Lines.Select(line => new EdiRow(
            line,
            ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson))).ToList();
        if (rows.Count == 0)
            throw new InvalidOperationException("The billing period has no claim lines.");

        var envelope = rows[0].Snapshot;
        Validate(envelope, rows);
        var submitterId = envelope.SubmitterId;
        var date = generatedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = generatedAt.ToString("HHmm", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();

        builder.AppendLine(Segment("ISA", "00", "          ", "00", "          ", "ZZ",
            submitterId.PadRight(15), "ZZ", OaReceiverId.PadRight(15), date[2..], time,
            "^", "00501", controlNumber, "0", isTest ? "T" : "P", SubSep));
        builder.AppendLine(Segment("GS", "HC", submitterId, OaReceiverId, date, time, "1", "X", VersionCode));
        builder.AppendLine(Segment("ST", "837", "0001", VersionCode));
        builder.AppendLine(Segment("BHT", "0019", "00", controlNumber, date, time, "CH"));
        builder.AppendLine(Segment("NM1", "41", "2", envelope.BillingProviderName, "", "", "", "", "46", submitterId));
        builder.AppendLine(Segment("PER", "IC", envelope.SubmitterContactName, "TE", envelope.SubmitterContactPhone));
        builder.AppendLine(Segment("NM1", "40", "2", "OFFICE ALLY", "", "", "", "", "46", OaReceiverId));
        builder.AppendLine(Segment("HL", "1", "", "20", "1"));
        builder.AppendLine(Segment("PRV", "BI", "PXC", "251B00000X"));
        builder.AppendLine(Segment("NM1", "85", "2", envelope.BillingProviderName, "", "", "", "", "XX", envelope.BillingProviderNpi));
        builder.AppendLine(Segment("N3", envelope.BillingProviderStreet));
        builder.AppendLine(Segment("N4", envelope.BillingProviderCity, envelope.BillingProviderState, envelope.BillingProviderZip));
        builder.AppendLine(Segment("REF", "EI", envelope.BillingProviderTaxId));

        var hierarchicalId = 2;
        foreach (var group in rows.GroupBy(row => row.Snapshot.PersonId))
        {
            var subscriber = group.First().Snapshot;
            builder.AppendLine(Segment("HL", hierarchicalId++.ToString(CultureInfo.InvariantCulture), "1", "22", "0"));
            builder.AppendLine(Segment("SBR", "P", "18", "", "", "", "", "", "", "MC"));
            builder.AppendLine(Segment("NM1", "IL", "1", subscriber.SubscriberLastName,
                subscriber.SubscriberFirstName, "", "", "", "MI", subscriber.SubscriberMemberId));
            builder.AppendLine(Segment("N3", subscriber.SubscriberStreet));
            builder.AppendLine(Segment("N4", subscriber.SubscriberCity, subscriber.SubscriberState, subscriber.SubscriberZip));
            builder.AppendLine(Segment("DMG", "D8",
                subscriber.SubscriberBirthDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                subscriber.SubscriberGenderCode));
            builder.AppendLine(Segment("NM1", "PR", "2", subscriber.PayerName, "", "", "", "", "PI", subscriber.PayerId));

            var lineNumber = 1;
            foreach (var row in group)
            {
                var line = row.Line;
                var units = BillingRules.FormatDecimal(line.Units ?? 0m);
                var charge = BillingRules.FormatDecimal(line.ChargeAmount);
                var procedure = $"HC{SubSep}{line.ProcedureCode}" +
                    (string.IsNullOrWhiteSpace(line.ProcedureModifier) ? string.Empty : $"{SubSep}{line.ProcedureModifier}");
                builder.AppendLine(Segment("CLM", $"{period.Id}-{line.NoteId}", charge, "", "",
                    $"{line.PlaceOfService:D2}{SubSep}{SubSep}1", "Y", "A", "Y", "I"));
                builder.AppendLine(Segment("DTP", "472", "D8", line.DateOfService.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                builder.AppendLine(Segment("HI", $"ABK{SubSep}{line.DiagnosisCode}"));
                builder.AppendLine(Segment("LX", lineNumber++.ToString(CultureInfo.InvariantCulture)));
                builder.AppendLine(Segment("SV1", procedure, charge, "UN", units,
                    line.PlaceOfService.ToString("D2", CultureInfo.InvariantCulture), "", ""));
                builder.AppendLine(Segment("DTP", "472", "D8", line.DateOfService.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
            }
        }

        // SE01 counts ST through SE inclusive; ISA and GS are outside the transaction set.
        var segmentCount = builder.ToString().Count(character => character == '~') - 1;
        builder.AppendLine(Segment("SE", segmentCount.ToString(CultureInfo.InvariantCulture), "0001"));
        builder.AppendLine(Segment("GE", "1", "1"));
        builder.AppendLine(Segment("IEA", "1", controlNumber));
        return builder.ToString();
    }

    private static void Validate(ProfessionalClaimSnapshot envelope, IReadOnlyList<EdiRow> rows)
    {
        if (!BillingRules.IsSafeX12Element(envelope.BillingProviderName, 60) ||
            !BillingRules.IsValidNpi(envelope.BillingProviderNpi) ||
            !BillingRules.IsSafeX12Element(envelope.BillingProviderTaxId, 50) ||
            !BillingRules.IsSafeX12Element(envelope.BillingProviderStreet, 55) ||
            !BillingRules.IsSafeX12Element(envelope.BillingProviderCity, 30) ||
            !BillingRules.IsSafeX12Element(envelope.BillingProviderState, 2) ||
            !BillingRules.IsSafeX12Element(envelope.BillingProviderZip, 15) ||
            !BillingRules.IsSafeX12Element(envelope.SubmitterId, 15) ||
            !BillingRules.IsSafeX12Element(envelope.PayerName, 60) ||
            !BillingRules.IsSafeX12Element(envelope.PayerId, 80) ||
            !BillingRules.IsSafeX12Element(envelope.SubmitterContactName, 60) ||
            envelope.SubmitterContactPhone.Length is < 10 or > 15 ||
            envelope.SubmitterContactPhone.Any(character => !char.IsDigit(character)))
            throw new InvalidOperationException("The claim provider snapshot is incomplete or invalid.");

        foreach (var row in rows)
        {
            var line = row.Line;
            var snapshot = row.Snapshot;
            if (snapshot.AgencyId != envelope.AgencyId || snapshot.SubmitterId != envelope.SubmitterId ||
                snapshot.PayerId != envelope.PayerId || snapshot.BillingProviderNpi != envelope.BillingProviderNpi)
                throw new InvalidOperationException("The billing period mixes incompatible provider or payer snapshots.");
            if (!BillingRules.IsSafeX12Element(snapshot.SubscriberFirstName, 35) ||
                !BillingRules.IsSafeX12Element(snapshot.SubscriberLastName, 60) ||
                !BillingRules.IsSafeX12Element(snapshot.SubscriberMemberId, 80) ||
                !BillingRules.IsSafeX12Element(snapshot.SubscriberStreet, 55) ||
                !BillingRules.IsSafeX12Element(snapshot.SubscriberCity, 30) ||
                !BillingRules.IsSafeX12Element(snapshot.SubscriberState, 2) ||
                !BillingRules.IsSafeX12Element(snapshot.SubscriberZip, 15) ||
                snapshot.SubscriberGenderCode is not ("M" or "F" or "U"))
                throw new InvalidOperationException($"Claim line {line.Id} has an invalid subscriber snapshot.");
            if (!BillingRules.IsValidProcedureCode(line.ProcedureCode) ||
                !BillingRules.IsValidModifier(line.ProcedureModifier) ||
                !BillingRules.IsValidDiagnosisCode(line.DiagnosisCode) ||
                line.PlaceOfService is < 1 or > 99 || line.Units is null or <= 0 || line.ChargeAmount <= 0)
                throw new InvalidOperationException($"Claim line {line.Id} is incomplete or invalid for EDI generation.");
        }
    }

    private static string Segment(string id, params string[] elements) =>
        id + "*" + string.Join("*", elements) + "~";

    private sealed record EdiRow(ServerClaimLine Line, ProfessionalClaimSnapshot Snapshot);
}
