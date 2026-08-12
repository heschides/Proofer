using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Sati.Api.Data;
using Sati.Api.Security;

namespace Sati.Api.Infrastructure;

internal sealed class PersonAuditPdfGenerator
{
    private static readonly Color Navy = Color.FromRgb(23, 50, 77);
    private static readonly Color Teal = Color.FromRgb(47, 125, 122);
    private static readonly Color PaleTeal = Color.FromRgb(238, 246, 245);
    private static readonly Color PaleGray = Color.FromRgb(244, 246, 248);
    private static readonly Color MidGray = Color.FromRgb(92, 105, 117);

    public byte[] Generate(
        ServerPerson person,
        IReadOnlyList<ServerPersonVersion> versions,
        ServerAgency agency,
        Actor requester,
        DateTime generatedAtUtc)
    {
        var document = CreateDocument(person, versions, agency, requester, generatedAtUtc);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static Document CreateDocument(
        ServerPerson person,
        IReadOnlyList<ServerPersonVersion> versions,
        ServerAgency agency,
        Actor requester,
        DateTime generatedAtUtc)
    {
        var document = new Document();
        document.Info.Title = $"Person lifecycle audit - {Safe(person.FirstName)} {Safe(person.LastName)}".Trim();
        document.Info.Subject = "Immutable Person profile change history";
        document.Info.Author = "Sati";

        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException("MigraDoc did not provide its Normal style.");
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;
        normal.Font.Color = Navy;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(0.7);
        section.PageSetup.BottomMargin = Unit.FromInch(0.65);
        section.PageSetup.LeftMargin = Unit.FromInch(0.7);
        section.PageSetup.RightMargin = Unit.FromInch(0.7);

        AddHeaderAndFooter(section, agency, person);
        AddTitle(section, person, agency, requester, generatedAtUtc);
        AddSummary(section, person, versions);

        var versionsHeading = section.AddParagraph("LIFECYCLE HISTORY");
        versionsHeading.Format.SpaceBefore = Unit.FromPoint(14);
        versionsHeading.Format.SpaceAfter = Unit.FromPoint(8);
        versionsHeading.Format.Font.Bold = true;
        versionsHeading.Format.Font.Size = 10;
        versionsHeading.Format.Font.Color = Teal;

        foreach (var version in versions.OrderBy(value => value.Version))
            AddVersion(section, version);

        var certification = section.AddParagraph();
        certification.Format.SpaceBefore = Unit.FromPoint(14);
        certification.Format.Shading.Color = PaleGray;
        certification.Format.Borders.Width = Unit.FromPoint(0.5);
        certification.Format.Borders.Color = Color.FromRgb(204, 212, 218);
        certification.Format.LeftIndent = Unit.FromPoint(8);
        certification.Format.RightIndent = Unit.FromPoint(8);
        certification.Format.SpaceBefore = Unit.FromPoint(10);
        certification.Format.SpaceAfter = Unit.FromPoint(6);
        certification.AddFormattedText("Report statement\n", TextFormat.Bold);
        certification.AddText(
            "This report was generated from Sati's append-only Person lifecycle ledger. " +
            "It contains profile values recorded after each successful API change. Related records " +
            "such as notes, contacts, forms, assessments, and billing items maintain separate histories.");

        return document;
    }

    private static void AddHeaderAndFooter(Section section, ServerAgency agency, ServerPerson person)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.Format.Font.Name = "Arial";
        header.Format.Font.Size = 8;
        header.Format.Font.Color = MidGray;
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.7);
        header.Format.Borders.Bottom.Color = Teal;
        header.Format.SpaceAfter = Unit.FromPoint(5);
        header.AddFormattedText("SATI", TextFormat.Bold);
        header.AddText($"  |  {Safe(agency.Name)}  |  Person #{person.Id}");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = MidGray;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        footer.Format.Borders.Top.Color = Color.FromRgb(204, 212, 218);
        footer.Format.SpaceBefore = Unit.FromPoint(5);
        footer.AddText("CONFIDENTIAL - PERSON LIFECYCLE AUDIT  |  Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void AddTitle(
        Section section,
        ServerPerson person,
        ServerAgency agency,
        Actor requester,
        DateTime generatedAtUtc)
    {
        var eyebrow = section.AddParagraph("PERSON LIFECYCLE AUDIT");
        eyebrow.Format.Font.Bold = true;
        eyebrow.Format.Font.Size = 9;
        eyebrow.Format.Font.Color = Teal;
        eyebrow.Format.SpaceBefore = Unit.FromPoint(10);

        var title = section.AddParagraph($"{Safe(person.FirstName)} {Safe(person.LastName)}".Trim());
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 23;
        title.Format.Font.Color = Navy;
        title.Format.SpaceAfter = Unit.FromPoint(3);

        var subtitle = section.AddParagraph(
            $"{Safe(agency.Name)}  |  Person ID {person.Id}  |  Current revision {person.Revision}");
        subtitle.Format.Font.Size = 10;
        subtitle.Format.Font.Color = MidGray;
        subtitle.Format.SpaceAfter = Unit.FromPoint(10);

        var notice = section.AddParagraph();
        notice.Format.Shading.Color = PaleTeal;
        notice.Format.Borders.Left.Width = Unit.FromPoint(3);
        notice.Format.Borders.Left.Color = Teal;
        notice.Format.LeftIndent = Unit.FromPoint(9);
        notice.Format.RightIndent = Unit.FromPoint(9);
        notice.Format.SpaceBefore = Unit.FromPoint(5);
        notice.Format.SpaceAfter = Unit.FromPoint(10);
        notice.AddFormattedText("Protected audit record. ", TextFormat.Bold);
        notice.AddText(
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC by {Safe(requester.DisplayName)} " +
            $"(user {requester.UserId}). Handle according to agency privacy, retention, and disclosure policy.");
    }

    private static void AddSummary(
        Section section,
        ServerPerson person,
        IReadOnlyList<ServerPersonVersion> versions)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Color.FromRgb(211, 218, 223);
        table.AddColumn(Unit.FromInch(1.45));
        table.AddColumn(Unit.FromInch(2.15));
        table.AddColumn(Unit.FromInch(1.45));
        table.AddColumn(Unit.FromInch(2.15));

        var first = versions.OrderBy(value => value.Version).FirstOrDefault();
        var last = versions.OrderByDescending(value => value.Version).FirstOrDefault();
        AddSummaryRow(table,
            "Versions", versions.Count.ToString(CultureInfo.InvariantCulture),
            "Current revision", person.Revision.ToString(CultureInfo.InvariantCulture));
        AddSummaryRow(table,
            "History begins", first?.ChangedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "No history",
            "Last change", last?.ChangedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "No history");
    }

    private static void AddSummaryRow(
        Table table,
        string labelOne,
        string valueOne,
        string labelTwo,
        string valueTwo)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(5);
        row.BottomPadding = Unit.FromPoint(5);
        SetCell(row.Cells[0], labelOne, bold: true, shaded: true);
        SetCell(row.Cells[1], valueOne);
        SetCell(row.Cells[2], labelTwo, bold: true, shaded: true);
        SetCell(row.Cells[3], valueTwo);
    }

    private static void SetCell(Cell cell, string text, bool bold = false, bool shaded = false)
    {
        if (shaded)
            cell.Shading.Color = PaleGray;
        var paragraph = cell.AddParagraph(Safe(text));
        paragraph.Format.Font.Bold = bold;
        paragraph.Format.LeftIndent = Unit.FromPoint(3);
        paragraph.Format.RightIndent = Unit.FromPoint(3);
    }

    private static void AddVersion(Section section, ServerPersonVersion version)
    {
        var dto = PersonLifecycle.ToDto(version);
        var banner = section.AddParagraph();
        banner.Format.KeepWithNext = true;
        banner.Format.SpaceBefore = Unit.FromPoint(10);
        banner.Format.SpaceAfter = Unit.FromPoint(3);
        banner.Format.Shading.Color = Navy;
        banner.Format.LeftIndent = Unit.FromPoint(8);
        banner.Format.RightIndent = Unit.FromPoint(8);
        banner.Format.Font.Color = Colors.White;
        banner.Format.Font.Size = 10;
        banner.AddFormattedText(
            $"Version {dto.Version} - {Safe(DisplayChangeKind(dto.ChangeKind))}",
            TextFormat.Bold);

        var meta = section.AddParagraph(
            $"{dto.ChangedAtUtc:yyyy-MM-dd HH:mm:ss} UTC  |  {Safe(dto.ActorDisplayName)} " +
            $"(user {dto.ActorUserId})  |  Request {Safe(dto.CorrelationId)}");
        meta.Format.KeepWithNext = true;
        meta.Format.Font.Size = 8;
        meta.Format.Font.Color = MidGray;
        meta.Format.SpaceAfter = Unit.FromPoint(5);

        foreach (var change in dto.Changes)
            AddChange(section, change);
    }

    private static void AddChange(Section section, Sati.Contracts.V1.PersonFieldChangeDto change)
    {
        var heading = section.AddParagraph(Safe(change.Label));
        heading.Format.KeepWithNext = true;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 9;
        heading.Format.Font.Color = Teal;
        heading.Format.SpaceBefore = Unit.FromPoint(4);
        heading.Format.SpaceAfter = Unit.FromPoint(1);

        if (change.PreviousValue is not null)
        {
            var previous = section.AddParagraph();
            previous.AddFormattedText("Previous: ", TextFormat.Bold);
            previous.AddText(Display(change.PreviousValue));
            previous.Format.Font.Color = MidGray;
            previous.Format.LeftIndent = Unit.FromPoint(8);
        }

        var current = section.AddParagraph();
        current.AddFormattedText(change.PreviousValue is null ? "Initial value: " : "New value: ", TextFormat.Bold);
        current.AddText(Display(change.NewValue));
        current.Format.LeftIndent = Unit.FromPoint(8);
        current.Format.Borders.Bottom.Width = Unit.FromPoint(0.3);
        current.Format.Borders.Bottom.Color = Color.FromRgb(221, 226, 230);
        current.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static string DisplayChangeKind(string value) => value switch
    {
        "TrackingBaseline" => "Tracking baseline",
        _ => value
    };

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not recorded" : Safe(value);

    private static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return new string(value
            .Select(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t') ? ' ' : character)
            .ToArray());
    }
}
