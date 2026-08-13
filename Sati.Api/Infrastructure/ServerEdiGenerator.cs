using System.Globalization;
using System.Text;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

internal static class ServerEdiGenerator
{
    private const string OaReceiverId = "330897513";
    private const string VersionCode = "005010X222A1";
    private const string SubSep = ":";

    public static string Generate(
        ServerBillingPeriod period,
        IReadOnlyList<ServerNote> notes,
        IReadOnlyList<ServerPerson> people,
        ServerAgency agency,
        string submitterId,
        bool isTest,
        DateTime generatedAt,
        string controlNumber)
    {
        var notesById = notes.ToDictionary(note => note.Id);
        var peopleById = people.ToDictionary(person => person.Id);
        var rows = period.Lines.Select(line =>
        {
            if (!notesById.TryGetValue(line.NoteId, out var note) ||
                !peopleById.TryGetValue(note.PersonId, out var person))
                throw new InvalidOperationException("A billing line is missing its note or client.");
            return new EdiRow(line, note, person);
        }).ToList();

        ValidateAgency(agency);
        var now = generatedAt;
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var time = now.ToString("HHmm", CultureInfo.InvariantCulture);
        var interchange = controlNumber;
        var control = controlNumber;
        var builder = new StringBuilder();

        builder.AppendLine(Segment("ISA", "00", "          ", "00", "          ", "ZZ",
            submitterId.PadRight(15), "ZZ", OaReceiverId.PadRight(15), date[2..], time,
            "^", "00501", control, "0", isTest ? "T" : "P"));
        builder.AppendLine(Segment("GS", "HC", submitterId, OaReceiverId, date, time, "1", "X", VersionCode));
        builder.AppendLine(Segment("ST", "837", "0001", VersionCode));
        builder.AppendLine(Segment("BHT", "0019", "00", interchange, date, time, "CH"));
        builder.AppendLine(Segment("NM1", "41", "2", agency.Name, "", "", "", "", "46", submitterId));
        builder.AppendLine(Segment("PER", "IC", agency.Name, "TE", "3609757000"));
        builder.AppendLine(Segment("NM1", "40", "2", "OFFICE ALLY", "", "", "", "", "46", OaReceiverId));
        builder.AppendLine(Segment("HL", "1", "", "20", "1"));
        builder.AppendLine(Segment("PRV", "BI", "PXC", "251B00000X"));
        builder.AppendLine(Segment("NM1", "85", "2", agency.Name, "", "", "", "", "XX", agency.Npi!));
        builder.AppendLine(Segment("N3", agency.Street!));
        builder.AppendLine(Segment("N4", agency.City!, agency.State!, agency.Zip!));
        builder.AppendLine(Segment("REF", "EI", agency.TaxId!));

        var hierarchicalId = 2;
        foreach (var group in rows.GroupBy(row => row.Person.Id))
        {
            var person = group.First().Person;
            builder.AppendLine(Segment("HL", hierarchicalId++.ToString(CultureInfo.InvariantCulture), "1", "22", "0"));
            builder.AppendLine(Segment("SBR", "P", "18", "", "", "", "", "", "", "MC"));
            builder.AppendLine(Segment("NM1", "IL", "1", person.LastName!, person.FirstName!, "", "", "", "MI", person.MaineCareId!));
            builder.AppendLine(Segment("DMG", "D8", person.BirthDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                person.Gender == 1 ? "M" : person.Gender == 2 ? "F" : "U"));
            builder.AppendLine(Segment("NM1", "PR", "2", "MEDICAID MAINE", "", "", "", "", "PI", "MCDME"));

            var lineNumber = 1;
            foreach (var row in group)
            {
                var line = row.Line;
                var units = line.Units?.ToString("F2", CultureInfo.InvariantCulture) ?? "0";
                builder.AppendLine(Segment("CLM", $"{period.Id}-{line.NoteId}", units, "", "",
                    $"{line.PlaceOfService}{SubSep}{SubSep}1", "Y", "A", "Y", "I"));
                builder.AppendLine(Segment("DTP", "472", "D8", line.DateOfService.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                if (!string.IsNullOrWhiteSpace(line.DiagnosisCode))
                    builder.AppendLine(Segment("HI", $"ABK{SubSep}{line.DiagnosisCode}"));
                builder.AppendLine(Segment("LX", lineNumber++.ToString(CultureInfo.InvariantCulture)));
                builder.AppendLine(Segment("SV1", $"HC{SubSep}T1016", units, "UN", units,
                    line.PlaceOfService.ToString(CultureInfo.InvariantCulture), "", ""));
                builder.AppendLine(Segment("DTP", "472", "D8", line.DateOfService.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
            }
        }

        var segmentCount = builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length + 1;
        builder.AppendLine(Segment("SE", segmentCount.ToString(CultureInfo.InvariantCulture), "0001"));
        builder.AppendLine(Segment("GE", "1", "1"));
        builder.AppendLine(Segment("IEA", "1", control));
        return builder.ToString();
    }

    private static void ValidateAgency(ServerAgency agency)
    {
        if (string.IsNullOrWhiteSpace(agency.Name) || string.IsNullOrWhiteSpace(agency.Npi) ||
            string.IsNullOrWhiteSpace(agency.TaxId) || string.IsNullOrWhiteSpace(agency.Street) ||
            string.IsNullOrWhiteSpace(agency.City) || string.IsNullOrWhiteSpace(agency.State) ||
            string.IsNullOrWhiteSpace(agency.Zip))
            throw new InvalidOperationException("The agency record is incomplete for EDI generation.");
    }

    private static string Segment(string id, params string[] elements) =>
        id + "*" + string.Join("*", elements) + "~";

    private sealed record EdiRow(ServerClaimLine Line, ServerNote Note, ServerPerson Person);
}
