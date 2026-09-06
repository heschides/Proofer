using Sati.Models;
using Sati.Models.Billing;
using Sati.Contracts.V1;

namespace Sati.Helpers
{
    /// <summary>
    /// Translates a BillingPeriod and its ClaimLines into a valid X12 837P
    /// flat file string per Office Ally's companion guide (005010X222A1).
    ///
    /// Pure static translation — no DB access, no DI. The caller is responsible
    /// for loading the BillingPeriod with all required navigation properties:
    ///   BillingPeriod.Lines (each with Note, Note.Person, Note.Person.Agency)
    ///
    /// Segment terminator: ~
    /// Element separator: *
    /// Sub-element separator: :
    /// One segment per line for human readability during development.
    /// OA accepts both single-line and multi-line formats.
    /// </summary>
    public static class EdiGenerator
    {
        private const string SegTerm = "~";
        private const string ElemSep = "*";
        private const string SubSep = ":";
        private const string OaReceiverId = "330897513";
        private const string OaReceiverName = "OFFICE ALLY";
        private const string VersionCode = "005010X222A1";
        private const string TaxonomyCode = "251B00000X"; // Case Management

        public static string Generate(BillingPeriod period, bool isTest, DateTime generatedAt, string controlNumber)
        {
            var dateStr = generatedAt.ToString("yyyyMMdd");
            var timeStr = generatedAt.ToString("HHmm");
            var icn = controlNumber; // interchange control number
            var gcn = "1"; // group control number — one group per file

            var rows = ReadAndValidateRows(period);

            // Group the immutable claim snapshots by consumer. No mutable Person or Agency value
            // is read while rendering a financial record.
            var byPerson = rows
                .GroupBy(row => row.Snapshot.PersonId)
                .ToList();
            var envelope = rows[0].Snapshot;
            var submitterId = envelope.SubmitterId;

            var sb = new System.Text.StringBuilder();

            // ─── ISA — Interchange Control Header ───────────────────────────────
            // Fixed-width fields. ISA is exactly 106 characters including the
            // segment terminator. Field widths are mandated by the X12 standard.
            sb.AppendLine(Seg("ISA",
                "00",                           // ISA01 Auth qualifier
                "          ",                   // ISA02 Auth info (10 spaces)
                "00",                           // ISA03 Security qualifier
                "          ",                   // ISA04 Security info (10 spaces)
                "ZZ",                           // ISA05 Sender qualifier
                submitterId.PadRight(15),       // ISA06 Sender ID (15 chars)
                "ZZ",                           // ISA07 Receiver qualifier
                OaReceiverId.PadRight(15),      // ISA08 Receiver ID (15 chars)
                dateStr[2..],                   // ISA09 Date (YYMMDD)
                timeStr,                        // ISA10 Time (HHMM)
                "^",                            // ISA11 Repetition separator
                "00501",                        // ISA12 Version
                icn.PadLeft(9, '0')[..9],       // ISA13 Interchange control number (9 digits)
                "0",                            // ISA14 Acknowledgment requested
                isTest ? "T" : "P",             // ISA15 Usage indicator
                SubSep                          // ISA16 Component separator
            ));

            // ─── GS — Functional Group Header ───────────────────────────────────
            sb.AppendLine(Seg("GS",
                "HC",                           // GS01 Functional ID (HC = Health Care Claim)
                submitterId,                    // GS02 Sender code
                OaReceiverId,                   // GS03 Receiver code
                dateStr,                        // GS04 Date (YYYYMMDD)
                timeStr,                        // GS05 Time (HHMM)
                gcn,                            // GS06 Group control number
                "X",                            // GS07 Responsible agency
                VersionCode                     // GS08 Version
            ));

            // ─── ST — Transaction Set Header ────────────────────────────────────
            sb.AppendLine(Seg("ST",
                "837",                          // ST01 Transaction set ID
                "0001",                         // ST02 Transaction set control number
                VersionCode                     // ST03 Implementation convention
            ));

            // ─── BHT — Beginning of Hierarchical Transaction ─────────────────────
            sb.AppendLine(Seg("BHT",
                "0019",                         // BHT01 Hierarchical structure code
                "00",                           // BHT02 Transaction set purpose (00 = original)
                icn,                            // BHT03 Reference ID
                dateStr,                        // BHT04 Date
                timeStr,                        // BHT05 Time
                "CH"                            // BHT06 Transaction type (CH = chargeable)
            ));

            // ─── Loop 1000A — Submitter ───────────────────────────────────────────
            sb.AppendLine(Seg("NM1",
                "41",                           // NM101 Submitter
                "2",                            // NM102 Non-person entity
                envelope.BillingProviderName,  // NM103 Org name
                "", "", "", "",                 // NM104-107 not used
                "46",                           // NM108 ID qualifier
                submitterId                     // NM109 Submitter ID
            ));

            sb.AppendLine(Seg("PER",
                "IC",                           // PER01 Contact function (IC = information contact)
                envelope.SubmitterContactName, // PER02 Name
                "TE",                           // PER03 Comm qualifier (telephone)
                envelope.SubmitterContactPhone // PER04 submitter contact number
            ));

            // ─── Loop 1000B — Receiver ───────────────────────────────────────────
            sb.AppendLine(Seg("NM1",
                "40",                           // NM101 Receiver
                "2",                            // NM102 Non-person entity
                OaReceiverName,                 // NM103
                "", "", "", "",                 // NM104-107 not used
                "46",                           // NM108
                OaReceiverId                    // NM109
            ));

            // ─── Loop 2000A — Billing Provider HL ────────────────────────────────
            var hlBilling = 1;
            sb.AppendLine(Seg("HL",
                hlBilling.ToString(),           // HL01 Hierarchical ID
                "",                             // HL02 Parent (none — top level)
                "20",                           // HL03 Level code (20 = information source)
                "1"                             // HL04 Child code (1 = has children)
            ));

            sb.AppendLine(Seg("PRV",
                "BI",                           // PRV01 Provider code (BI = billing)
                "PXC",                          // PRV02 Reference ID qualifier
                TaxonomyCode                    // PRV03 Taxonomy code
            ));

            // ─── Loop 2010AA — Billing Provider Name/Address ─────────────────────
            sb.AppendLine(Seg("NM1",
                "85",                           // NM101 Billing provider
                "2",                            // NM102 Non-person entity
                envelope.BillingProviderName,  // NM103
                "", "", "", "",                 // NM104-107 not used
                "XX",                           // NM108 NPI qualifier
                envelope.BillingProviderNpi    // NM109 NPI
            ));

            sb.AppendLine(Seg("N3", envelope.BillingProviderStreet));

            sb.AppendLine(Seg("N4",
                envelope.BillingProviderCity,   // N401
                envelope.BillingProviderState,  // N402
                envelope.BillingProviderZip     // N403
            ));

            sb.AppendLine(Seg("REF",
                "EI",                           // REF01 Tax ID qualifier
                envelope.BillingProviderTaxId  // REF02 Tax ID
            ));

            // ─── Loop 2000B — One per consumer ───────────────────────────────────
            var hlCounter = hlBilling + 1;

            foreach (var personGroup in byPerson)
            {
                var subscriber = personGroup.First().Snapshot;
                var hlSubscriber = hlCounter++;

                sb.AppendLine(Seg("HL",
                    hlSubscriber.ToString(),    // HL01
                    hlBilling.ToString(),       // HL02 Parent = billing provider
                    "22",                       // HL03 Level code (22 = subscriber)
                    "0"                         // HL04 No children
                ));

                sb.AppendLine(Seg("SBR",
                    "P",                        // SBR01 Payer responsibility (P = primary)
                    "18",                       // SBR02 Individual relationship (18 = self)
                    "", "", "", "", "", "",      // SBR03-09 not used
                    "MC"                        // SBR09 Claim filing indicator (MC = Medicaid)
                ));

                // ─── Loop 2010BA — Subscriber ────────────────────────────────────
                sb.AppendLine(Seg("NM1",
                    "IL",                       // NM101 Insured/subscriber
                    "1",                        // NM102 Person
                    subscriber.SubscriberLastName,  // NM103
                    subscriber.SubscriberFirstName, // NM104
                    "", "",                     // NM105-106 not used
                    "",                         // NM107 not used
                    "MI",                       // NM108 Member ID qualifier
                    subscriber.SubscriberMemberId // NM109 MaineCare ID
                ));

                sb.AppendLine(Seg("N3", subscriber.SubscriberStreet));
                sb.AppendLine(Seg("N4", subscriber.SubscriberCity,
                    subscriber.SubscriberState, subscriber.SubscriberZip));

                sb.AppendLine(Seg("DMG",
                    "D8",                                           // DMG01 Date format
                    subscriber.SubscriberBirthDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                    subscriber.SubscriberGenderCode
                ));

                // ─── Loop 2010BB — Payer ─────────────────────────────────────────
                sb.AppendLine(Seg("NM1",
                    "PR",                       // NM101 Payer
                    "2",                        // NM102 Non-person entity
                    subscriber.PayerName,       // NM103
                    "", "", "", "",             // NM104-107 not used
                    "PI",                       // NM108 Payer ID qualifier
                    subscriber.PayerId          // NM109 payer ID
                ));

                // ─── Loop 2300 — One claim per note ──────────────────────────────
                var claimCounter = 1;
                foreach (var row in personGroup)
                {
                    var line = row.Line;
                    var claimId = $"{period.Id}-{line.NoteId}";

                    var units = BillingRules.FormatDecimal(line.Units ?? 0m);
                    var charge = BillingRules.FormatDecimal(line.ChargeAmount);
                    var procedure = $"HC{SubSep}{line.ProcedureCode}" +
                        (string.IsNullOrWhiteSpace(line.ProcedureModifier)
                            ? string.Empty
                            : $"{SubSep}{line.ProcedureModifier}");
                    sb.AppendLine(Seg("CLM",
                        claimId,                            // CLM01 Claim submitter ID
                        charge,                             // CLM02 Total charge
                        "", "",                             // CLM03-04 not used
                        $"{line.PlaceOfService:D2}{SubSep}{SubSep}1", // CLM05 place:qualifier:participation
                        "Y",                                // CLM06 Provider signature on file
                        "A",                                // CLM07 Assignment (A = assigned)
                        "Y",                                // CLM08 Benefits assigned
                        "I"                                 // CLM09 Release of info (I = informed consent)
                    ));

                    sb.AppendLine(Seg("DTP",
                        "472",                                              // DTP01 Date qualifier (service)
                        "D8",                                               // DTP02 Format
                        line.DateOfService.ToString("yyyyMMdd")             // DTP03 Date
                    ));

                    if (!string.IsNullOrWhiteSpace(line.DiagnosisCode))
                    {
                        sb.AppendLine(Seg("HI",
                            $"ABK{SubSep}{line.DiagnosisCode}"  // HI01 Principal diagnosis (ABK = ICD-10)
                        ));
                    }

                    // ─── Loop 2400 — Service line ─────────────────────────────
                    sb.AppendLine(Seg("LX", claimCounter.ToString()));

                    sb.AppendLine(Seg("SV1",
                        procedure,                              // SV101 Procedure and modifier
                        charge,                                 // SV102 Charge
                        "UN",                                    // SV103 Unit basis (UN = unit)
                        units,                                  // SV104 Units
                        line.PlaceOfService.ToString("D2"),     // SV105 Place of service
                        "",                                      // SV106 not used
                        ""                                       // SV107 not used
                    ));

                    sb.AppendLine(Seg("DTP",
                        "472",                                              // DTP01
                        "D8",                                               // DTP02
                        line.DateOfService.ToString("yyyyMMdd")             // DTP03
                    ));

                    claimCounter++;
                }
            }

            // ─── SE — Transaction Set Trailer ────────────────────────────────────
            // SE01 is the segment count — every segment from ST to SE inclusive.
            // SE01 counts ST through SE inclusive; ISA and GS are outside the transaction set.
            var segmentCount = sb.ToString().Count(character => character == '~') - 1;

            sb.AppendLine(Seg("SE",
                segmentCount.ToString(),        // SE01 Segment count
                "0001"                          // SE02 Transaction set control number (matches ST02)
            ));

            // ─── GE — Functional Group Trailer ───────────────────────────────────
            sb.AppendLine(Seg("GE",
                "1",                            // GE01 Number of transaction sets
                gcn                             // GE02 Group control number (matches GS06)
            ));

            // ─── IEA — Interchange Control Trailer ───────────────────────────────
            sb.AppendLine(Seg("IEA",
                "1",                            // IEA01 Number of functional groups
                icn.PadLeft(9, '0')[..9]        // IEA02 Interchange control number (matches ISA13)
            ));

            return sb.ToString();
        }

        public static void ValidatePeriod(BillingPeriod period) =>
            _ = ReadAndValidateRows(period);

        private static List<EdiSnapshotRow> ReadAndValidateRows(BillingPeriod period)
        {
            var rows = period.Lines
                .Select(line => new EdiSnapshotRow(
                    line,
                    ProfessionalClaimSnapshotCodec.Deserialize(line.ClaimSnapshotJson)))
                .ToList();
            if (rows.Count == 0)
                throw new InvalidOperationException("The billing period has no claim lines.");

            Validate(rows[0].Snapshot, rows);
            return rows;
        }

        // Builds a segment string: SEGID*elem1*elem2*...*elemN~
        private static string Seg(string id, params string[] elements)
        {
            return id + ElemSep + string.Join(ElemSep, elements) + SegTerm;
        }

        private static void Validate(
            ProfessionalClaimSnapshot envelope,
            IEnumerable<EdiSnapshotRow> rows)
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
                string.IsNullOrWhiteSpace(envelope.SubmitterContactPhone) ||
                envelope.SubmitterContactPhone.Length is < 10 or > 15 ||
                envelope.SubmitterContactPhone.Any(character => !char.IsDigit(character)))
                throw new InvalidOperationException("The agency billing and EDI configuration is incomplete or invalid.");

            foreach (var row in rows)
            {
                var line = row.Line;
                var snapshot = row.Snapshot;
                if (snapshot.AgencyId != envelope.AgencyId ||
                    snapshot.SubmitterId != envelope.SubmitterId ||
                    snapshot.PayerId != envelope.PayerId ||
                    snapshot.BillingProviderNpi != envelope.BillingProviderNpi)
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
                    line.PlaceOfService is < 1 or > 99 ||
                    line.Units is null or <= 0 || line.ChargeAmount <= 0)
                    throw new InvalidOperationException($"Claim line {line.Id} is incomplete or invalid for EDI generation.");
            }
        }

        private sealed record EdiSnapshotRow(
            ClaimLine Line,
            ProfessionalClaimSnapshot Snapshot);
    }
}
