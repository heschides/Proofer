using System.Globalization;
using System.IO;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Reporting;

/// <summary>
/// Renders a published AT request as the exportable PDF a case manager sends on.
///
/// This is a Sati-generated summary document, NOT a reproduction of the OADS
/// Authorized Payment Information Form. It carries the same information in a
/// legible layout; if OADS requires their exact form, only this class changes —
/// publication, attestation, and locking do not know what the page looks like.
///
/// Every figure printed here comes from the stored request, not from a live
/// recalculation, with one exception: the passthrough fee needs the rate, which
/// the entity cannot see. The caller passes the rate the request was filed
/// under. See the snapshot-semantics note on ATRequest.
/// </summary>
public sealed class ATRequestPdfExporter
{
    private static readonly Color Navy = Color.FromRgb(23, 50, 77);
    private static readonly Color Teal = Color.FromRgb(47, 125, 122);
    private static readonly Color PaleTeal = Color.FromRgb(238, 246, 245);
    private static readonly Color PaleGray = Color.FromRgb(244, 246, 248);
    private static readonly Color MidGray = Color.FromRgb(92, 105, 117);
    private static readonly Color HairLine = Color.FromRgb(211, 218, 223);

    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Renders the request.
    ///
    /// REGENERATION IS FAITHFUL, NOT REFRESHED. A published request re-rendered
    /// months later must be the document that was attested to, so every figure
    /// comes from the stored record: the client and case-manager snapshots, the
    /// stored tax amount, the frozen passthrough rate, and the frozen attestation.
    /// <paramref name="currentPassthroughRate"/> is used only for a DRAFT, which
    /// has no frozen rate and should show the agency's live terms.
    ///
    /// <paramref name="generatedAtUtc"/> likewise only reaches the page on a
    /// draft, in the "not published" banner. For a published request the output
    /// is deterministic, including the PDF's own creation timestamp.
    /// </summary>
    public byte[] Generate(ATRequest request, decimal currentPassthroughRate, DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rate = request.EffectivePassthroughRate(currentPassthroughRate);
        var document = CreateDocument(request, rate, generatedAtUtc);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        // Pin the document timestamps for a published request. Without this the
        // only thing separating two regenerations of the same filed record is a
        // clock reading, which is exactly the kind of difference that makes people
        // wonder whether anything else changed too.
        if (request.SignedAtUtc is DateTime signedAtUtc)
        {
            renderer.PdfDocument.Info.CreationDate = signedAtUtc;
            renderer.PdfDocument.Info.ModificationDate = signedAtUtc;
        }

        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, closeStream: false);
        return output.ToArray();
    }

    /// <summary>
    /// Filename offered in the save dialog. Kept here rather than in the view so
    /// the desktop and any future server-side export agree on what these files
    /// are called. Sortable date first, and the client name reduced to characters
    /// that survive a filesystem.
    /// </summary>
    public static string SuggestedFileName(ATRequest request)
    {
        var stamp = (request.SignedAtUtc ?? DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var client = new string((request.ClientName ?? "Client")
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrEmpty(client))
            client = "Client";
        return $"AT-Request-{stamp}-{client}.pdf";
    }

    private static Document CreateDocument(ATRequest request, decimal passthroughRate, DateTime generatedAtUtc)
    {
        var document = new Document();
        document.Info.Title = $"Assistive Technology payment request - {Safe(request.ClientName)}".Trim();
        document.Info.Subject = "Authorized payment request for assistive technology";
        document.Info.Author = "SatiLogica";

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

        AddHeaderAndFooter(section, request);
        AddTitle(section, request, generatedAtUtc);
        AddParties(section, request);
        AddVendor(section, request);
        AddItems(section, request);
        AddTotals(section, request, passthroughRate);
        AddAttestation(section, request);
        AddEvidencePage(document, request);

        return document;
    }

    /// <summary>
    /// The evidence page: each item's pasted screenshot with the URL it came
    /// from. Its own page rather than an appendix to the listing, because page
    /// one is the payment request a reviewer reads and this is the supporting
    /// material behind it.
    ///
    /// Omitted entirely when nothing was pasted — a page headed "Item
    /// screenshots" with no screenshots on it invites the reader to wonder what
    /// went missing.
    /// </summary>
    private static void AddEvidencePage(Document document, ATRequest request)
    {
        var documented = request.Items.Where(item => item.HasScreenshot).ToList();
        if (documented.Count == 0)
            return;

        // A new Section rather than a page break inside the existing one, so this
        // page can carry its own header and footer wording.
        var section = document.LastSection.Clone();
        document.Add(section);
        section.Elements.Clear();
        section.Headers.Primary.Elements.Clear();
        section.Footers.Primary.Elements.Clear();

        var header = section.Headers.Primary.AddParagraph();
        header.Format.Font.Name = "Arial";
        header.Format.Font.Size = 8;
        header.Format.Font.Color = MidGray;
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.7);
        header.Format.Borders.Bottom.Color = Teal;
        header.Format.SpaceAfter = Unit.FromPoint(5);
        header.AddFormattedText("SATILOGICA", TextFormat.Bold);
        header.AddText($"  |  Item screenshots  |  Request #{request.Id}");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = MidGray;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        footer.Format.Borders.Top.Color = HairLine;
        footer.Format.SpaceBefore = Unit.FromPoint(5);
        footer.AddText("CONFIDENTIAL - ASSISTIVE TECHNOLOGY PAYMENT REQUEST  |  Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();

        var title = section.AddParagraph("ITEM SCREENSHOTS");
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 13;
        title.Format.Font.Color = Navy;
        title.Format.SpaceAfter = Unit.FromPoint(2);

        var subtitle = section.AddParagraph(
            $"{Display(request.ClientName)}  |  Request #{request.Id}  |  " +
            $"{documented.Count} of {request.Items.Count} items documented");
        subtitle.Format.Font.Size = 9;
        subtitle.Format.Font.Color = MidGray;
        subtitle.Format.SpaceAfter = Unit.FromPoint(10);

        foreach (var item in documented)
            AddEvidenceBlock(section, item);
    }

    private static void AddEvidenceBlock(Section section, ATRequestItem item)
    {
        var heading = section.AddParagraph(Display(item.Name));
        heading.Format.KeepWithNext = true;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 10;
        heading.Format.Font.Color = Teal;
        heading.Format.SpaceBefore = Unit.FromPoint(12);
        heading.Format.SpaceAfter = Unit.FromPoint(2);

        // The URL as plain text, never a link annotation. These are values a user
        // typed, and turning user-entered text into a clickable target inside a
        // document that leaves the agency hands the reader a hazard nobody
        // reviewed. Someone who wants to visit it can copy it.
        var url = section.AddParagraph(
            string.IsNullOrWhiteSpace(item.Url) ? "No URL recorded." : Safe(item.Url));
        url.Format.KeepWithNext = true;
        url.Format.Font.Size = 8;
        url.Format.Font.Color = MidGray;
        url.Format.SpaceAfter = Unit.FromPoint(5);

        var bytes = item.ScreenshotPng;
        if (bytes is null || bytes.Length == 0)
            return;

        var container = section.AddParagraph();
        container.Format.SpaceAfter = Unit.FromPoint(8);

        try
        {
            // MigraDoc reads the image straight from the encoded bytes with this
            // prefix, so nothing is written to a temporary file — these are
            // clinical-adjacent records and should not be spilling onto disk on
            // the way into a PDF.
            var image = container.AddImage("base64:" + Convert.ToBase64String(bytes));

            // Constrain the width and let the height follow. LockAspectRatio keeps
            // a tall screenshot from being squashed; the cap is the printable
            // width of a Letter page inside this section's margins.
            image.LockAspectRatio = true;
            image.Width = Unit.FromInch(6.0);

            // A hairline frame so a screenshot with a white background does not
            // bleed into the page and leave the reader guessing where it ends.
            image.LineFormat.Visible = true;
            image.LineFormat.Width = Unit.FromPoint(0.5);
            image.LineFormat.Color = HairLine;
        }
        catch (Exception)
        {
            // A screenshot that will not decode must not cost the case manager the
            // whole document. Say so on the page instead, so the gap is visible
            // rather than silent.
            var failed = section.AddParagraph("The screenshot stored for this item could not be rendered.");
            failed.Format.Font.Size = 8;
            failed.Format.Font.Italic = true;
            failed.Format.Font.Color = MidGray;
        }
    }

    private static void AddHeaderAndFooter(Section section, ATRequest request)
    {
        var header = section.Headers.Primary.AddParagraph();
        header.Format.Font.Name = "Arial";
        header.Format.Font.Size = 8;
        header.Format.Font.Color = MidGray;
        header.Format.Borders.Bottom.Width = Unit.FromPoint(0.7);
        header.Format.Borders.Bottom.Color = Teal;
        header.Format.SpaceAfter = Unit.FromPoint(5);
        header.AddFormattedText("SATILOGICA", TextFormat.Bold);
        header.AddText($"  |  {Safe(request.CaseManagerAgency)}  |  Request #{request.Id}");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Name = "Arial";
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = MidGray;
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.5);
        footer.Format.Borders.Top.Color = HairLine;
        footer.Format.SpaceBefore = Unit.FromPoint(5);
        footer.AddText("CONFIDENTIAL - ASSISTIVE TECHNOLOGY PAYMENT REQUEST  |  Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void AddTitle(Section section, ATRequest request, DateTime generatedAtUtc)
    {
        var eyebrow = section.AddParagraph("ASSISTIVE TECHNOLOGY PAYMENT REQUEST");
        eyebrow.Format.Font.Bold = true;
        eyebrow.Format.Font.Size = 9;
        eyebrow.Format.Font.Color = Teal;
        eyebrow.Format.SpaceBefore = Unit.FromPoint(10);

        var title = section.AddParagraph(Display(request.ClientName));
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 23;
        title.Format.Font.Color = Navy;
        title.Format.SpaceAfter = Unit.FromPoint(3);

        var subtitle = section.AddParagraph(
            $"Request #{request.Id}  |  Status {request.Status}  |  " +
            $"Submitted {Date(request.SubmittedDate)}");
        subtitle.Format.Font.Size = 10;
        subtitle.Format.Font.Color = MidGray;
        subtitle.Format.SpaceAfter = Unit.FromPoint(10);

        // A draft that reached the exporter is still exportable — a case manager
        // may want to circulate one for review — but it must not be mistaken for
        // the published document. Say so on the page rather than relying on the
        // reader noticing an empty signature block.
        if (request.IsPublished)
            return;

        var draft = section.AddParagraph();
        draft.Format.Shading.Color = PaleGray;
        draft.Format.Borders.Left.Width = Unit.FromPoint(3);
        draft.Format.Borders.Left.Color = MidGray;
        draft.Format.LeftIndent = Unit.FromPoint(9);
        draft.Format.RightIndent = Unit.FromPoint(9);
        draft.Format.SpaceBefore = Unit.FromPoint(5);
        draft.Format.SpaceAfter = Unit.FromPoint(10);
        draft.AddFormattedText("DRAFT - NOT PUBLISHED. ", TextFormat.Bold);
        draft.AddText(
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC. This copy carries no attestation " +
            "and is not a submitted request.");
    }

    private static void AddParties(Section section, ATRequest request)
    {
        AddSectionHeading(section, "CLIENT AND CASE MANAGER", first: true);

        var table = NewTable(section, Unit.FromInch(1.45), Unit.FromInch(2.15), Unit.FromInch(1.45), Unit.FromInch(2.15));
        AddPairRow(table,
            "Client", Display(request.ClientName),
            "Evergreen ID", Display(request.ClientEvergreenId));
        AddPairRow(table,
            "Case manager", Display(request.CaseManagerName),
            "Agency", Display(request.CaseManagerAgency));
        AddPairRow(table,
            "Email", Display(request.CaseManagerEmail),
            "Phone", Display(request.CaseManagerPhone));
    }

    private static void AddVendor(Section section, ATRequest request)
    {
        AddSectionHeading(section, "VENDOR / PROVIDER");

        var table = NewTable(section, Unit.FromInch(1.45), Unit.FromInch(2.15), Unit.FromInch(1.45), Unit.FromInch(2.15));
        AddPairRow(table,
            "Vendor", Display(request.VendorName),
            "Billing location (EIS)", Display(request.VendorBillingLocation));
        AddPairRow(table,
            "Program contact", Display(request.VendorProgramContact),
            "Billing contact", Display(request.VendorBillingContact));
    }

    private static void AddItems(Section section, ATRequest request)
    {
        AddSectionHeading(section, "ITEMS OR SERVICES");

        var table = NewTable(section,
            Unit.FromInch(3.4), Unit.FromInch(1.1), Unit.FromInch(0.8), Unit.FromInch(1.9));

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.TopPadding = Unit.FromPoint(4);
        head.BottomPadding = Unit.FromPoint(4);
        SetCell(head.Cells[0], "Item or service", bold: true, shaded: true);
        SetCell(head.Cells[1], "Unit cost", bold: true, shaded: true, right: true);
        SetCell(head.Cells[2], "Qty", bold: true, shaded: true, right: true);
        SetCell(head.Cells[3], "Line total", bold: true, shaded: true, right: true);

        foreach (var item in request.Items)
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(4);
            row.BottomPadding = Unit.FromPoint(4);

            // Name only. The URL belongs on the evidence page beside the
            // screenshot it goes with, not in this listing — the state form has no
            // URL column, and a bare link here is noise to whoever is reading the
            // costs.
            var name = row.Cells[0].AddParagraph(Display(item.Name));
            name.Format.LeftIndent = Unit.FromPoint(3);
            name.Format.RightIndent = Unit.FromPoint(3);

            SetCell(row.Cells[1], Currency(item.ItemCost), right: true);
            SetCell(row.Cells[2], item.Quantity.ToString(CultureInfo.InvariantCulture), right: true);
            SetCell(row.Cells[3], Currency(item.LineTotal), right: true);
        }
    }

    private static void AddTotals(Section section, ATRequest request, decimal passthroughRate)
    {
        var table = NewTable(section, Unit.FromInch(5.3), Unit.FromInch(1.9));
        table.Rows.LeftIndent = Unit.FromInch(0);

        AddTotalRow(table, "Items subtotal", Currency(request.ItemsTotal));

        // The tax LABEL names the source rather than a rate, because an overridden
        // figure was not derived from any rate and printing one next to it would
        // be a claim the record does not support.
        AddTotalRow(table,
            request.SalesTaxOverridden ? "Sales tax (entered manually)" : "Sales tax",
            Currency(request.SalesTax));

        AddTotalRow(table,
            $"Passthrough fee ({passthroughRate:P2})",
            Currency(request.PassthroughFee(passthroughRate)));

        var total = AddTotalRow(table, "TOTAL", Currency(request.TotalCost(passthroughRate)), bold: true);
        total.Shading.Color = PaleTeal;
    }

    private static void AddAttestation(Section section, ATRequest request)
    {
        AddSectionHeading(section, "ATTESTATION");

        if (!request.IsPublished)
        {
            var unsigned = section.AddParagraph(
                "This request has not been published. No attestation has been recorded.");
            unsigned.Format.Font.Color = MidGray;
            return;
        }

        // The frozen statement, not the current constant — see the note on
        // ATRequest.AttestationStatement. The fallback covers requests published
        // before the statement was stored; it names the situation rather than
        // silently substituting today's wording for wording nobody read.
        var statement = section.AddParagraph(
            string.IsNullOrWhiteSpace(request.AttestationStatement)
                ? "The statement recorded with this attestation is not available."
                : Safe(request.AttestationStatement));
        statement.Format.Shading.Color = PaleTeal;
        statement.Format.Borders.Left.Width = Unit.FromPoint(3);
        statement.Format.Borders.Left.Color = Teal;
        statement.Format.LeftIndent = Unit.FromPoint(9);
        statement.Format.RightIndent = Unit.FromPoint(9);
        statement.Format.SpaceBefore = Unit.FromPoint(4);
        statement.Format.SpaceAfter = Unit.FromPoint(8);

        var table = NewTable(section, Unit.FromInch(1.45), Unit.FromInch(2.15), Unit.FromInch(1.45), Unit.FromInch(2.15));
        AddPairRow(table,
            "Published by", Display(request.SignedByName),
            "Role", Display(request.SignedByRole));
        AddPairRow(table,
            "Published (UTC)",
            request.SignedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? "Not recorded",
            "Sati user", request.SignedByUserId?.ToString(CultureInfo.InvariantCulture) ?? "Not recorded");

        var notice = section.AddParagraph(AtRequestPublication.AttestationScopeNotice);
        notice.Format.Font.Size = 7.5;
        notice.Format.Font.Color = MidGray;
        notice.Format.SpaceBefore = Unit.FromPoint(8);
    }

    // ---- Layout helpers ----

    private static void AddSectionHeading(Section section, string text, bool first = false)
    {
        var heading = section.AddParagraph(text);
        heading.Format.KeepWithNext = true;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 10;
        heading.Format.Font.Color = Teal;
        heading.Format.SpaceBefore = Unit.FromPoint(first ? 4 : 14);
        heading.Format.SpaceAfter = Unit.FromPoint(5);
    }

    private static Table NewTable(Section section, params Unit[] widths)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.5);
        table.Borders.Color = HairLine;
        foreach (var width in widths)
            table.AddColumn(width);
        return table;
    }

    private static void AddPairRow(Table table, string labelOne, string valueOne, string labelTwo, string valueTwo)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(5);
        row.BottomPadding = Unit.FromPoint(5);
        SetCell(row.Cells[0], labelOne, bold: true, shaded: true);
        SetCell(row.Cells[1], valueOne);
        SetCell(row.Cells[2], labelTwo, bold: true, shaded: true);
        SetCell(row.Cells[3], valueTwo);
    }

    private static Row AddTotalRow(Table table, string label, string value, bool bold = false)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(4);
        row.BottomPadding = Unit.FromPoint(4);
        SetCell(row.Cells[0], label, bold: bold, right: true);
        SetCell(row.Cells[1], value, bold: bold, right: true);
        return row;
    }

    private static void SetCell(Cell cell, string text, bool bold = false, bool shaded = false, bool right = false)
    {
        if (shaded)
            cell.Shading.Color = PaleGray;
        var paragraph = cell.AddParagraph(Safe(text));
        paragraph.Format.Font.Bold = bold;
        paragraph.Format.LeftIndent = Unit.FromPoint(3);
        paragraph.Format.RightIndent = Unit.FromPoint(3);
        if (right)
            paragraph.Format.Alignment = ParagraphAlignment.Right;
    }

    private static string Currency(decimal value) => value.ToString("C", Money);

    private static string Date(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not submitted";

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not recorded" : Safe(value);

    // Control characters are stripped rather than escaped: they carry no meaning
    // in a rendered document and are a well-known way to make what a reader sees
    // differ from what was stored. Same reasoning as PersonAuditPdfExporter.
    private static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return new string(value
            .Select(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t') ? ' ' : character)
            .ToArray());
    }
}
