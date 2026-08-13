using Sati.Models;
using Sati.Models.Billing;

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
        private const string PayerId = "MCDME";
        private const string PayerName = "MEDICAID MAINE";
        private const string ProcedureCode = "T1016";
        private const string VersionCode = "005010X222A1";
        private const string TaxonomyCode = "251B00000X"; // Case Management

        public static string Generate(BillingPeriod period, string submitterId, bool isTest, DateTime generatedAt, string controlNumber)
        {
            var dateStr = generatedAt.ToString("yyyyMMdd");
            var timeStr = generatedAt.ToString("HHmm");
            var icn = controlNumber; // interchange control number
            var gcn = "1"; // group control number — one group per file

            // Group claim lines by consumer so each consumer gets one 2000B loop
            // containing all their claims for the period.
            var byPerson = period.Lines
                .GroupBy(l => l.Note.PersonId)
                .ToList();

            var agency = period.Lines
                .Select(l => l.Note.Person.Agency)
                .FirstOrDefault(a => a != null)
                ?? throw new InvalidOperationException("No agency found on claim lines.");

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
                isTest ? "T" : "P"              // ISA15 Usage indicator
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
                agency.Name,                    // NM103 Org name
                "", "", "", "",                 // NM104-107 not used
                "46",                           // NM108 ID qualifier
                submitterId                     // NM109 Submitter ID
            ));

            sb.AppendLine(Seg("PER",
                "IC",                           // PER01 Contact function (IC = information contact)
                agency.Name,                    // PER02 Name
                "TE",                           // PER03 Comm qualifier (telephone)
                "3609757000"                    // PER04 OA support number as placeholder
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
                agency.Name,                    // NM103
                "", "", "", "",                 // NM104-107 not used
                "XX",                           // NM108 NPI qualifier
                agency.Npi!                     // NM109 NPI
            ));

            sb.AppendLine(Seg("N3", agency.Street!));

            sb.AppendLine(Seg("N4",
                agency.City!,                   // N401
                agency.State!,                  // N402
                agency.Zip!                     // N403
            ));

            sb.AppendLine(Seg("REF",
                "EI",                           // REF01 Tax ID qualifier
                agency.TaxId!                   // REF02 Tax ID
            ));

            // ─── Loop 2000B — One per consumer ───────────────────────────────────
            var hlCounter = hlBilling + 1;

            foreach (var personGroup in byPerson)
            {
                var person = personGroup.First().Note.Person;
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
                    person.LastName!,           // NM103
                    person.FirstName!,          // NM104
                    "", "",                     // NM105-106 not used
                    "",                         // NM107 not used
                    "MI",                       // NM108 Member ID qualifier
                    person.MaineCareId!         // NM109 MaineCare ID
                ));

                sb.AppendLine(Seg("DMG",
                    "D8",                                           // DMG01 Date format
                    person.BirthDate.ToString("yyyyMMdd"),          // DMG02 DOB
                    person.Gender switch                            // DMG03 Gender
                    {
                        Gender.Male => "M",
                        Gender.Female => "F",
                        _ => "U"
                    }
                ));

                // ─── Loop 2010BB — Payer ─────────────────────────────────────────
                sb.AppendLine(Seg("NM1",
                    "PR",                       // NM101 Payer
                    "2",                        // NM102 Non-person entity
                    PayerName,                  // NM103
                    "", "", "", "",             // NM104-107 not used
                    "PI",                       // NM108 Payer ID qualifier
                    PayerId                     // NM109 OA payer ID
                ));

                // ─── Loop 2300 — One claim per note ──────────────────────────────
                var claimCounter = 1;
                foreach (var line in personGroup)
                {
                    var claimId = $"{period.Id}-{line.NoteId}";

                    sb.AppendLine(Seg("CLM",
                        claimId,                            // CLM01 Claim submitter ID
                        line.Units?.ToString("F2") ?? "0",  // CLM02 Total charge
                        "", "",                             // CLM03-04 not used
                        $"{line.PlaceOfService}{SubSep}{SubSep}1", // CLM05 place:qualifier:participation
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
                        $"HC{SubSep}{ProcedureCode}",           // SV101 Procedure (HC = HCPCS)
                        line.Units?.ToString("F2") ?? "0",       // SV102 Charge
                        "UN",                                    // SV103 Unit basis (UN = unit)
                        line.Units?.ToString("F2") ?? "0",       // SV104 Units
                        line.PlaceOfService.ToString(),          // SV105 Place of service
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
            var segmentCount = sb.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Length + 1; // +1 for SE itself

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

        // Builds a segment string: SEGID*elem1*elem2*...*elemN~
        private static string Seg(string id, params string[] elements)
        {
            return id + ElemSep + string.Join(ElemSep, elements) + SegTerm;
        }
    }
}