using System.Globalization;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>
/// Identity facts derived from the authorized session and stored profile. None of
/// these values are accepted from the desktop request on the cloud path.
/// </summary>
public sealed record AgencyReleaseSubject(
    int PersonId,
    string? ConsumerName,
    DateTime? BirthDate,
    string? GuardianName,
    string? AgencyName,
    string? AgencyAddress,
    string? AgencyPhone,
    string? CaseManagerName,
    string? CaseManagerRole);

/// <summary>
/// Creates Sati's agency-owned release. Unlike the official DHHS forms, this is a
/// document Sati may compose and brand; it does not imitate or alter a state form.
/// </summary>
public sealed class AgencyReleasePdfGenerator
{
    private static readonly Color Navy = Color.FromRgb(23, 50, 77);
    private static readonly Color Teal = Color.FromRgb(47, 125, 122);
    private static readonly Color PaleTeal = Color.FromRgb(238, 246, 245);
    private static readonly Color PaleNavy = Color.FromRgb(239, 243, 247);
    private static readonly Color PaleWarning = Color.FromRgb(255, 247, 230);
    private static readonly Color Warning = Color.FromRgb(154, 92, 0);
    private static readonly Color PaleDanger = Color.FromRgb(253, 239, 239);
    private static readonly Color Danger = Color.FromRgb(151, 44, 44);
    private static readonly Color Border = Color.FromRgb(205, 214, 220);
    private static readonly Color MidGray = Color.FromRgb(88, 101, 113);

    public byte[] Generate(
        AgencyReleaseSubject subject,
        AgencyReleaseRequest request,
        DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        AgencyReleaseRules.EnsureValid(request);

        var document = CreateDocument(subject, request, generatedAtUtc);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Info.CreationDate = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc);
        renderer.PdfDocument.Info.ModificationDate = renderer.PdfDocument.Info.CreationDate;
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static Document CreateDocument(
        AgencyReleaseSubject subject,
        AgencyReleaseRequest request,
        DateTime generatedAtUtc)
    {
        var document = new Document();
        document.Info.Title = $"Agency release - {Safe(subject.ConsumerName)}";
        document.Info.Subject = "Authorization to release or obtain information";
        document.Info.Author = "Sati";

        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException("MigraDoc did not provide its Normal style.");
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;
        normal.Font.Color = Navy;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(0.65);
        section.PageSetup.BottomMargin = Unit.FromInch(0.65);
        section.PageSetup.LeftMargin = Unit.FromInch(0.7);
        section.PageSetup.RightMargin = Unit.FromInch(0.7);

        AddHeaderAndFooter(section, subject);
        AddTitle(section, subject, request, generatedAtUtc);
        AddIdentity(section, subject);
        AddRecipient(section, request);
        AddAuthorization(section, request);
        AddInformation(section, request);
        AddSensitivePermissions(section, request);
        AddLegalTerms(section);
        AddSignatures(section, subject, request, generatedAtUtc);

        return document;
    }

    private static void AddHeaderAndFooter(Section section, AgencyReleaseSubject subject)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.Format.Font.Name = "Arial";
        header.Format.Font.Size = 8;
        header.Format.Font.Color = MidGray;
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.8);
        header.Format.Borders.Bottom.Color = Teal;
        header.Format.SpaceAfter = Unit.FromPoint(5);
        header.AddFormattedText("SATI", TextFormat.Bold);
        header.AddText($"  |  {Safe(subject.AgencyName)}  |  Agency release");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = MidGray;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        footer.Format.Borders.Top.Color = Border;
        footer.Format.SpaceBefore = Unit.FromPoint(5);
        footer.AddText("CONFIDENTIAL - AUTHORIZATION TO RELEASE INFORMATION  |  Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void AddTitle(
        Section section,
        AgencyReleaseSubject subject,
        AgencyReleaseRequest request,
        DateTime generatedAtUtc)
    {
        var eyebrow = section.AddParagraph(request.IsRevocation
            ? "REVOCATION OF AUTHORIZATION"
            : "AUTHORIZATION TO RELEASE OR OBTAIN INFORMATION");
        eyebrow.Format.Font.Bold = true;
        eyebrow.Format.Font.Size = 9;
        eyebrow.Format.Font.Color = Teal;
        eyebrow.Format.SpaceBefore = Unit.FromPoint(8);

        var title = section.AddParagraph("Release of Information");
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 23;
        title.Format.Font.Color = Navy;
        title.Format.SpaceAfter = Unit.FromPoint(2);

        var subtitle = section.AddParagraph(
            $"{Safe(subject.AgencyName)}  |  {Safe(subject.ConsumerName)}  |  Person #{subject.PersonId}");
        subtitle.Format.Font.Size = 10;
        subtitle.Format.Font.Color = MidGray;
        subtitle.Format.SpaceAfter = Unit.FromPoint(8);

        var granted = request.AuthorizationGranted == true;
        var callout = section.AddParagraph();
        callout.Format.Shading.Color = granted ? PaleTeal : PaleDanger;
        callout.Format.Borders.Left.Width = Unit.FromPoint(3);
        callout.Format.Borders.Left.Color = granted ? Teal : Danger;
        callout.Format.LeftIndent = Unit.FromPoint(9);
        callout.Format.RightIndent = Unit.FromPoint(9);
        callout.Format.SpaceBefore = Unit.FromPoint(3);
        callout.Format.SpaceAfter = Unit.FromPoint(10);
        callout.AddFormattedText(granted ? "Authorization granted. " : "Authorization not granted. ", TextFormat.Bold);
        callout.AddText(granted
            ? "The selections below record the consumer's stated direction. Required signatures remain separate."
            : "This document records that the consumer or legally responsible person did not authorize disclosure.");

        var generated = section.AddParagraph(
            $"Prepared {generatedAtUtc:yyyy-MM-dd HH:mm} UTC by {Safe(subject.CaseManagerName)} ({Safe(subject.CaseManagerRole)})." );
        generated.Format.Font.Size = 8;
        generated.Format.Font.Color = MidGray;
        generated.Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static void AddIdentity(Section section, AgencyReleaseSubject subject)
    {
        AddSectionHeading(section, "CONSUMER AND AGENCY");
        var table = CreateKeyValueTable(section);
        AddKeyValueRow(table, "Consumer", subject.ConsumerName, "Date of birth", FormatDate(subject.BirthDate));
        AddKeyValueRow(table, "Guardian", subject.GuardianName, "Case manager", subject.CaseManagerName);
        AddKeyValueRow(table, "Agency", subject.AgencyName, "Agency phone", subject.AgencyPhone);
        AddKeyValueRow(table, "Agency address", subject.AgencyAddress, "Person ID", subject.PersonId.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddRecipient(Section section, AgencyReleaseRequest request)
    {
        AddSectionHeading(section, "RECIPIENT / CONTACT");
        var table = CreateKeyValueTable(section);
        AddKeyValueRow(table, "Contact type", request.ContactType, "Contact name", request.ContactName);
        AddKeyValueRow(table, "Relationship", request.Relationship, "Telephone", request.ContactPhone);
        AddKeyValueRow(table, "Address", request.ContactAddress, "City / state", JoinNonBlank(request.ContactCity, request.ContactState));
        AddKeyValueRow(table, "Fax", request.ContactFax, "Email", request.ContactEmail);
    }

    private static void AddAuthorization(Section section, AgencyReleaseRequest request)
    {
        AddSectionHeading(section, "AUTHORIZATION WINDOW");
        var table = CreateKeyValueTable(section);
        AddKeyValueRow(
            table,
            "Disclosure scope",
            Enum.Parse<AgencyReleaseScope>(request.Scope!).ToString() == nameof(AgencyReleaseScope.OneTime)
                ? "One-time disclosure"
                : "Multiple disclosures",
            "Authorization",
            YesNo(request.AuthorizationGranted));
        AddKeyValueRow(table, "Start date", FormatDate(request.StartDate), "Expiration date", FormatDate(request.ExpirationDate));
        AddKeyValueRow(
            table,
            "Release without review",
            YesNo(request.ReleaseWithoutReview),
            "Revocation",
            request.IsRevocation ? $"Yes - effective {FormatDate(request.RevokedOn)}" : "No");
    }

    private static void AddInformation(Section section, AgencyReleaseRequest request)
    {
        AddSectionHeading(section, "INFORMATION TO DISCLOSE AND / OR OBTAIN");
        var selected = new HashSet<string>(request.InformationCategories ?? [], StringComparer.Ordinal);
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Border;
        table.AddColumn(Unit.FromInch(3.55));
        table.AddColumn(Unit.FromInch(3.55));

        var categories = AgencyReleaseInformation.All
            .Where(value => value != AgencyReleaseInformation.Other)
            .ToList();
        for (var index = 0; index < categories.Count; index += 2)
        {
            var row = table.AddRow();
            AddCheckboxCell(row.Cells[0], selected.Contains(categories[index]), AgencyReleaseInformation.DisplayName(categories[index]));
            if (index + 1 < categories.Count)
                AddCheckboxCell(row.Cells[1], selected.Contains(categories[index + 1]), AgencyReleaseInformation.DisplayName(categories[index + 1]));
        }

        var other = table.AddRow();
        other.Cells[0].MergeRight = 1;
        AddCheckboxCell(
            other.Cells[0],
            selected.Contains(AgencyReleaseInformation.Other),
            selected.Contains(AgencyReleaseInformation.Other)
                ? $"Other: {Safe(request.OtherInformation)}"
                : "Other");
    }

    private static void AddSensitivePermissions(Section section, AgencyReleaseRequest request)
    {
        AddSectionHeading(section, "SPECIAL PERMISSIONS");
        AddChoiceBand(
            section,
            "Drug / alcohol treatment or diagnosis",
            request.IncludeDrugAlcohol,
            "Federal substance-use confidentiality rules may restrict redisclosure without specific written consent.");
        AddChoiceBand(
            section,
            "Mental / behavioral health treatment",
            request.IncludeMentalHealth,
            "This permission is recorded separately from the general record categories above.");
        AddChoiceBand(
            section,
            "HIV / AIDS status, testing, or diagnosis",
            request.IncludeHivAids,
            "This permission is recorded separately because special confidentiality protections may apply.");
    }

    private static void AddLegalTerms(Section section)
    {
        // Keep the rights language with the signatures it governs. Without this
        // deliberate break a typical release strands only signature lines on page
        // two, while breaking earlier leaves half of page one unused.
        section.AddPageBreak();
        AddSectionHeading(section, "IMPORTANT RIGHTS AND DISCLOSURE TERMS");

        var part2 = section.AddParagraph();
        part2.Format.Shading.Color = PaleWarning;
        part2.Format.Borders.Left.Width = Unit.FromPoint(3);
        part2.Format.Borders.Left.Color = Warning;
        part2.Format.LeftIndent = Unit.FromPoint(9);
        part2.Format.RightIndent = Unit.FromPoint(9);
        part2.Format.SpaceAfter = Unit.FromPoint(8);
        part2.AddFormattedText("Substance-use records. ", TextFormat.Bold);
        part2.AddText(
            "Information disclosed from records protected by 42 CFR Part 2 may not be further disclosed unless expressly permitted by the written consent of the person to whom the records pertain or otherwise permitted by applicable law.");

        var rights = section.AddParagraph();
        rights.Format.Shading.Color = PaleNavy;
        rights.Format.Borders.Width = Unit.FromPoint(0.5);
        rights.Format.Borders.Color = Border;
        rights.Format.LeftIndent = Unit.FromPoint(9);
        rights.Format.RightIndent = Unit.FromPoint(9);
        rights.Format.SpaceAfter = Unit.FromPoint(8);
        rights.AddFormattedText("The person authorizing disclosure understands: ", TextFormat.Bold);
        rights.AddText(
            "I have the right to review information and material released and may revoke this authorization at any time by written request to the agency, except to the extent action has already been taken in reliance on it or as otherwise provided by law. This authorization is voluntary. Refusal to sign will not affect enrollment, eligibility for benefits, or coverage of services, except when applicable law permits a service or coverage decision to depend on the authorization. Information disclosed to a recipient may be subject to further disclosure and may no longer be protected by the same privacy rules. I may request and receive the agency's Notice of Privacy Practices before signing.");
    }

    private static void AddSignatures(
        Section section,
        AgencyReleaseSubject subject,
        AgencyReleaseRequest request,
        DateTime generatedAtUtc)
    {
        AddSectionHeading(section, request.IsRevocation ? "REVOCATION AND SIGNATURES" : "SIGNATURES");

        if (request.IsRevocation)
        {
            var revocation = section.AddParagraph();
            revocation.Format.Shading.Color = PaleDanger;
            revocation.Format.Borders.Left.Width = Unit.FromPoint(3);
            revocation.Format.Borders.Left.Color = Danger;
            revocation.Format.LeftIndent = Unit.FromPoint(9);
            revocation.Format.RightIndent = Unit.FromPoint(9);
            revocation.Format.SpaceAfter = Unit.FromPoint(9);
            revocation.AddFormattedText("Revocation effective ", TextFormat.Bold);
            revocation.AddText($"{FormatDate(request.RevokedOn)}. This does not apply to actions previously taken in reliance on the authorization.");
        }

        AddSignatureLine(section, "Consumer or legally responsible person", "Date");
        AddSignatureLine(section, "Guardian / representative, if applicable", "Date");

        if (request.ConfirmedObtainedRoi)
        {
            var attestation = section.AddParagraph();
            attestation.Format.Shading.Color = PaleTeal;
            attestation.Format.Borders.Width = Unit.FromPoint(0.5);
            attestation.Format.Borders.Color = Teal;
            attestation.Format.LeftIndent = Unit.FromPoint(9);
            attestation.Format.RightIndent = Unit.FromPoint(9);
            attestation.Format.SpaceBefore = Unit.FromPoint(8);
            attestation.AddFormattedText("STAFF ATTESTATION\n", TextFormat.Bold);
            attestation.AddText(AgencyReleaseRules.StaffAttestation);
            attestation.AddLineBreak();
            attestation.AddFormattedText(
                $"Recorded by {Safe(subject.CaseManagerName)} ({Safe(subject.CaseManagerRole)}) on {generatedAtUtc:yyyy-MM-dd HH:mm} UTC.\n",
                TextFormat.Bold);
            attestation.AddText(AgencyReleaseRules.AttestationScopeNotice);
        }
        else
        {
            var draft = section.AddParagraph();
            draft.Format.Shading.Color = PaleWarning;
            draft.Format.Borders.Left.Width = Unit.FromPoint(3);
            draft.Format.Borders.Left.Color = Warning;
            draft.Format.LeftIndent = Unit.FromPoint(9);
            draft.Format.RightIndent = Unit.FromPoint(9);
            draft.Format.SpaceBefore = Unit.FromPoint(8);
            draft.AddFormattedText("Prepared draft. ", TextFormat.Bold);
            draft.AddText("No staff attestation that the authorization was obtained is attached to this copy.");
        }
    }

    private static void AddSectionHeading(Section section, string text)
    {
        var heading = section.AddParagraph(text);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 10;
        heading.Format.Font.Color = Teal;
        heading.Format.SpaceBefore = Unit.FromPoint(11);
        heading.Format.SpaceAfter = Unit.FromPoint(6);
        heading.Format.KeepWithNext = true;
    }

    private static Table CreateKeyValueTable(Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Border;
        table.AddColumn(Unit.FromInch(1.25));
        table.AddColumn(Unit.FromInch(2.3));
        table.AddColumn(Unit.FromInch(1.25));
        table.AddColumn(Unit.FromInch(2.3));
        return table;
    }

    private static void AddKeyValueRow(Table table, string label1, string? value1, string label2, string? value2)
    {
        var row = table.AddRow();
        AddLabelCell(row.Cells[0], label1);
        AddValueCell(row.Cells[1], value1);
        AddLabelCell(row.Cells[2], label2);
        AddValueCell(row.Cells[3], value2);
    }

    private static void AddLabelCell(Cell cell, string label)
    {
        cell.Shading.Color = PaleNavy;
        cell.VerticalAlignment = VerticalAlignment.Center;
        cell.Format.LeftIndent = Unit.FromPoint(4);
        cell.Format.RightIndent = Unit.FromPoint(4);
        var paragraph = cell.AddParagraph(label.ToUpperInvariant());
        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Size = 7.3;
        paragraph.Format.Font.Color = MidGray;
        paragraph.Format.SpaceAfter = 0;
    }

    private static void AddValueCell(Cell cell, string? value)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        cell.Format.LeftIndent = Unit.FromPoint(4);
        cell.Format.RightIndent = Unit.FromPoint(4);
        var paragraph = cell.AddParagraph(Safe(value, "Not provided"));
        paragraph.Format.SpaceAfter = 0;
    }

    private static void AddCheckboxCell(Cell cell, bool selected, string label)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        cell.Format.LeftIndent = Unit.FromPoint(5);
        cell.Format.RightIndent = Unit.FromPoint(5);
        cell.Shading.Color = selected ? PaleTeal : Colors.White;
        var paragraph = cell.AddParagraph();
        paragraph.Format.SpaceAfter = 0;
        paragraph.AddFormattedText(selected ? "[X]  " : "[ ]  ", TextFormat.Bold);
        paragraph.AddText(Safe(label));
    }

    private static void AddChoiceBand(Section section, string label, bool? selected, string explanation)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Border;
        table.AddColumn(Unit.FromInch(4.45));
        table.AddColumn(Unit.FromInch(0.85));
        table.AddColumn(Unit.FromInch(0.85));
        table.AddColumn(Unit.FromInch(0.95));
        var row = table.AddRow();
        row.Cells[0].Format.LeftIndent = Unit.FromPoint(5);
        row.Cells[0].AddParagraph(Safe(label));
        AddCheckboxCell(row.Cells[1], selected == true, "Yes");
        AddCheckboxCell(row.Cells[2], selected == false, "No");
        row.Cells[3].Shading.Color = PaleNavy;
        row.Cells[3].Format.LeftIndent = Unit.FromPoint(4);
        row.Cells[3].AddParagraph(selected == true ? "Included" : "Excluded");

        var note = section.AddParagraph(explanation);
        note.Format.Font.Size = 7.5;
        note.Format.Font.Color = MidGray;
        note.Format.LeftIndent = Unit.FromPoint(6);
        note.Format.SpaceAfter = Unit.FromPoint(5);
    }

    private static void AddSignatureLine(Section section, string label, string dateLabel)
    {
        var table = section.AddTable();
        table.AddColumn(Unit.FromInch(5.25));
        table.AddColumn(Unit.FromInch(0.2));
        table.AddColumn(Unit.FromInch(1.65));
        var row = table.AddRow();
        var signature = row.Cells[0].AddParagraph("________________________________________________________");
        signature.Format.SpaceBefore = Unit.FromPoint(12);
        signature.Format.SpaceAfter = 0;
        row.Cells[0].AddParagraph(label).Format.Font.Size = 7.5;
        var date = row.Cells[2].AddParagraph("________________");
        date.Format.SpaceBefore = Unit.FromPoint(12);
        date.Format.SpaceAfter = 0;
        row.Cells[2].AddParagraph(dateLabel).Format.Font.Size = 7.5;
    }

    private static string YesNo(bool? value) => value == true ? "Yes" : "No";

    private static string FormatDate(DateTime? value) =>
        value is DateTime date && date != default
            ? date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)
            : "Not provided";

    private static string FormatDate(DateOnly? value) =>
        value?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "Not provided";

    private static string JoinNonBlank(params string?[] values) =>
        string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

    private static string Safe(string? value, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
