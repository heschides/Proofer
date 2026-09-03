using System.Globalization;
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Sati.Contracts.V1;

namespace Sati.Forms;

public sealed record DocumentTemplateRenderResult(byte[] Pdf, IReadOnlyList<string> BlankFields);

/// <summary>Renders the deliberately small, non-executable annual-document template language.</summary>
public sealed class DocumentTemplatePdfComposer
{
    private static readonly Color Navy = Color.FromRgb(23, 50, 77);
    private static readonly Color Teal = Color.FromRgb(47, 125, 122);
    private static readonly Color Border = Color.FromRgb(205, 214, 220);

    public DocumentTemplateRenderResult Generate(
        AnnualDocumentKind kind,
        string body,
        DocumentTemplateRenderContext context,
        DateTime generatedAtUtc)
    {
        var validation = DocumentTemplateRules.Validate(kind, body);
        if (validation.Count > 0)
            throw new ArgumentException(string.Join(" ", validation.SelectMany(item => item.Value)), nameof(body));

        var values = Values(context);
        var blankFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Merge(string text) => DocumentTemplateRules.TokenPattern().Replace(text, match =>
        {
            var token = match.Groups[1].Value;
            var value = values.GetValueOrDefault(token);
            if (string.IsNullOrWhiteSpace(value))
                blankFields.Add(token);
            return value ?? string.Empty;
        });

        var document = new Document();
        document.Info.Title = AnnualDocumentCatalog.ForKind(kind).DisplayName;
        document.Info.Author = "Sati";
        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException("MigraDoc did not provide its Normal style.");
        normal.Font.Name = "Arial";
        normal.Font.Size = 9.5;
        normal.Font.Color = Navy;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        var section = AddSection(document);
        AddHeaderFooter(section, context, kind);
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                continue;
            if (line.Equals(DocumentTemplateRules.PageBreakMarker, StringComparison.Ordinal))
            {
                section = AddSection(document);
                AddHeaderFooter(section, context, kind);
                continue;
            }
            if (line.StartsWith('|'))
            {
                var tableLines = new List<string>();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('|'))
                    tableLines.Add(lines[index++].Trim());
                index--;
                AddTable(section, tableLines, Merge);
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddHeading(section, Merge(line[3..]), 2);
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                AddHeading(section, Merge(line[2..]), 1);
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var paragraph = section.AddParagraph();
                paragraph.Format.LeftIndent = Unit.FromInch(0.2);
                paragraph.Format.FirstLineIndent = Unit.FromInch(-0.15);
                paragraph.AddText($"•  {Merge(line[2..])}");
                continue;
            }

            section.AddParagraph(Merge(line));
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Info.CreationDate = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc);
        renderer.PdfDocument.Info.ModificationDate = renderer.PdfDocument.Info.CreationDate;
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, closeStream: false);
        return new DocumentTemplateRenderResult(
            output.ToArray(),
            blankFields.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static Section AddSection(Document document)
    {
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(0.65);
        section.PageSetup.BottomMargin = Unit.FromInch(0.65);
        section.PageSetup.LeftMargin = Unit.FromInch(0.75);
        section.PageSetup.RightMargin = Unit.FromInch(0.75);
        return section;
    }

    private static void AddHeaderFooter(
        Section section,
        DocumentTemplateRenderContext context,
        AnnualDocumentKind kind)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.Format.Font.Name = "Arial";
        header.Format.Font.Size = 8;
        header.Format.Font.Color = Teal;
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.8);
        header.Format.Borders.Bottom.Color = Teal;
        header.AddFormattedText("SATI", TextFormat.Bold);
        header.AddText($"  |  {context.AgencyName ?? string.Empty}  |  {AnnualDocumentCatalog.ForKind(kind).DisplayName}");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = 7.5;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        footer.Format.Borders.Top.Color = Border;
        footer.AddText("CONFIDENTIAL  |  Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void AddHeading(Section section, string text, int level)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Color = level == 1 ? Navy : Teal;
        paragraph.Format.Font.Size = level == 1 ? 21 : 13;
        paragraph.Format.KeepWithNext = true;
        paragraph.Format.SpaceBefore = Unit.FromPoint(level == 1 ? 8 : 10);
        paragraph.Format.SpaceAfter = Unit.FromPoint(level == 1 ? 10 : 4);
    }

    private static void AddTable(Section section, IReadOnlyList<string> lines, Func<string, string> merge)
    {
        var rows = lines.Select(ParseCells).Where(cells => !IsSeparator(cells)).ToList();
        if (rows.Count == 0)
            return;
        var columns = rows.Max(row => row.Count);
        if (columns < 2)
            throw new ArgumentException("A template table must contain at least two columns.");

        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = Border;
        var usableWidth = 7.0 / columns;
        for (var column = 0; column < columns; column++)
            table.AddColumn(Unit.FromInch(usableWidth));
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = table.AddRow();
            row.Format.Font.Size = 8.5;
            if (rowIndex == 0)
            {
                row.Format.Font.Bold = true;
                row.Shading.Color = Color.FromRgb(238, 246, 245);
            }
            for (var column = 0; column < columns; column++)
                row.Cells[column].AddParagraph(merge(column < rows[rowIndex].Count ? rows[rowIndex][column] : string.Empty));
        }
        table.Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static List<string> ParseCells(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();

    private static bool IsSeparator(IReadOnlyList<string> cells) =>
        cells.Count > 0 && cells.All(cell => Regex.IsMatch(cell, "^:?-{3,}:?$", RegexOptions.CultureInvariant));

    private static Dictionary<string, string?> Values(DocumentTemplateRenderContext context) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["agency.name"] = context.AgencyName,
            ["agency.address"] = context.AgencyAddress,
            ["agency.phone"] = context.AgencyPhone,
            ["consumer.full_name"] = context.ConsumerFullName,
            ["consumer.birth_date"] = Format(context.ConsumerBirthDate),
            ["cycle.start"] = context.CycleStart.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
            ["cycle.end"] = context.CycleEnd.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
            ["case_manager.name"] = context.CaseManagerName,
            ["case_manager.role"] = context.CaseManagerRole,
            ["provider.name"] = context.ProviderName,
            ["provider.address"] = context.ProviderAddress,
            ["provider.phone"] = context.ProviderPhone,
            ["provider.fax"] = context.ProviderFax
        };

    private static string? Format(DateTime? value) =>
        value?.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
}
