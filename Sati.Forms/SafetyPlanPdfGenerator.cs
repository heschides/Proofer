using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>Renders structured plan content without treating a draft as an approved plan.</summary>
public sealed class SafetyPlanPdfGenerator
{
    public byte[] Generate(string consumerName, DateTime cycleStart, SafetyPlanDocument plan, string status, DateTime generatedAtUtc)
    {
        var document = new Document();
        document.Info.Title = "Safety Plan";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial"; normal.Font.Size = 10;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(.65); section.PageSetup.BottomMargin = Unit.FromInch(.65);
        section.PageSetup.LeftMargin = Unit.FromInch(.75); section.PageSetup.RightMargin = Unit.FromInch(.75);
        var heading = section.AddParagraph("Safety Plan"); heading.Format.Font.Bold = true; heading.Format.Font.Size = 20;
        section.AddParagraph($"Consumer: {consumerName}\nCycle beginning: {cycleStart:MMMM d, yyyy}\nStatus: {status}").Format.SpaceAfter = Unit.FromPoint(12);
        if (!string.Equals(status, "Approved", StringComparison.Ordinal))
        {
            var notice = section.AddParagraph("DRAFT - NOT APPROVED FOR FINAL USE");
            notice.Format.Font.Bold = true; notice.Format.Font.Color = Colors.DarkRed; notice.Format.SpaceAfter = Unit.FromPoint(12);
        }
        foreach (var item in plan.Sections)
        {
            var title = section.AddParagraph(item.Id.Replace('-', ' ').ToUpperInvariant());
            title.Format.Font.Bold = true; title.Format.Font.Size = 11; title.Format.SpaceBefore = Unit.FromPoint(8);
            title.Format.KeepWithNext = true;
            section.AddParagraph(string.IsNullOrWhiteSpace(item.Text) ? "[Not yet completed]" : item.Text.Trim());
        }
        var footer = section.Footers.Primary.AddParagraph("CONFIDENTIAL  |  Page "); footer.Format.Alignment = ParagraphAlignment.Center; footer.AddPageField();
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument();
        renderer.PdfDocument.Info.CreationDate = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc);
        using var stream = new MemoryStream(); renderer.PdfDocument.Save(stream, false); return stream.ToArray();
    }
}
