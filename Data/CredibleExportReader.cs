using System.IO;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Sati.Contracts.V1;
using System.Text.RegularExpressions;

namespace Sati.Data;

/// <summary>Reads a saved Credible client print view into a <see cref="ClientExportDocument"/>.</summary>
public interface IClientExportReader
{
    /// <summary>Reads one saved export. Never throws for a bad artifact — it refuses with a reason.</summary>
    Task<ClientExportReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Reads already-loaded markup. Same rules; used by tests and the bulk reader.</summary>
    ClientExportReadResult Read(string markup);
}

/// <summary>
/// Parses a saved Credible print view as inert data.
///
/// <para>
/// <b>The document is never rendered or loaded.</b> A saved print view references scripts and
/// stylesheets on <c>assets.cbh3.crediblebh.com</c> and carries an Akamai RUM beacon pointed at
/// <c>s.go-mpulse.net</c>. Handing it to a WebBrowser control — even for a preview — would phone
/// out to Credible and Akamai from a machine holding an open client record, and run vendor script
/// against it. This uses a bare <c>HtmlParser</c>, which builds a DOM and nothing else: with no
/// browsing context there is no requester to fetch a resource and no engine to run a script. That
/// is a structural property of what is constructed here rather than a setting someone can flip,
/// and <c>CredibleExportReaderTests</c> holds the line.
/// </para>
///
/// <para>
/// The document's <c>__VIEWSTATE</c> is read past and dropped. ASP.NET ViewState is serialized
/// server state that can carry field values, so it is never logged, stored, or parsed.
/// </para>
/// </summary>
public sealed class CredibleExportReader : IClientExportReader
{
    // Full-width banner cells. `shc` carries the top-level banners (CONSUMER INFO, CONSUMER
    // EPISODE INFO); `hc` carries the sub-banners the field map actually keys on (Consumer
    // Address, Consumer Demograpics, Medical, ...). Both are section starts at the same level:
    // the labels under "Consumer Address" belong to it, not to the CONSUMER INFO above it.
    //
    // Matched as class TOKENS, never substrings — "shc".Contains("hc") is true, and a substring
    // test would silently treat every top-level banner as a sub-banner as well.
    private static readonly string[] BannerClasses = ["shc", "hc"];
    private static readonly string[] LabelClasses = ["lc", "lc2"];
    private static readonly string[] ValueClasses = ["vc", "vc2"];

    // `shHeader` is the page's own title row — the client's name, id and date of birth — not a
    // section. Treating it as one opens a section that swallows the first real block of fields.
    //
    // The explicit skip below does no work as the classes stand, since `shHeader` matches none
    // of the three lists. It earns its place against one specific and quite likely edit: the
    // name reads exactly like a section-header class, so adding it to BannerClasses is an easy
    // mistake, and this is what keeps that harmless. Mutation testing confirmed the pair —
    // adding it to BannerClasses passes with this skip and fails without it.
    private const string DocumentHeaderClass = "shHeader";

    private static readonly Regex ClientIdPattern =
        new(@"client_id=(?<id>\d{1,12})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    public async Task<ClientExportReadResult> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The path, never the content: a failure reading a client export must not put the
            // export in a message that reaches a log.
            return ClientExportReadResult.Refused(
                ClientExportRejection.Unreadable, "The file could not be opened.");
        }

        // Magic bytes rather than extension, so a PDF saved as .html is still caught. This is
        // the single most likely operator error — printing to PDF is the natural instinct for a
        // document like this, and it produces a file that looks complete and imports garbage.
        if (LooksLikePdf(bytes))
            return ClientExportReadResult.Refused(ClientExportRejection.NotHtml);

        return Read(DecodeText(bytes));
    }

    public ClientExportReadResult Read(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
            return ClientExportReadResult.Refused(ClientExportRejection.NotAPrintView);

        IDocument document;
        try
        {
            // A bare HtmlParser, deliberately: it builds a DOM and does nothing else. There is
            // no BrowsingContext, so there is no requester to fetch a stylesheet and no script
            // engine to run an inline <script>. A <script> tag is inert markup here and an
            // asset URL is a dead attribute.
            //
            // Do not swap this for BrowsingContext.OpenAsync, and never add WithDefaultLoader()
            // or WithJs(): either one turns opening a client record into outbound requests to
            // Credible and Akamai from the machine holding it.
            document = new HtmlParser().ParseDocument(markup);
        }
        catch (Exception exception)
        {
            return ClientExportReadResult.Refused(
                ClientExportRejection.Unreadable, exception.GetType().Name);
        }

        // Credible is a frames-based application, so Ctrl+S on its window saves the frameset
        // definition rather than the print view inside it — about 14KB with no client data.
        if (document.QuerySelector("frameset") is not null)
            return ClientExportReadResult.Refused(ClientExportRejection.ApplicationShell);

        var sections = ReadSections(document);
        if (sections.Count == 0)
            return ClientExportReadResult.Refused(ClientExportRejection.NotAPrintView);

        return ClientExportReadResult.Accepted(
            new ClientExportDocument(ReadClientId(markup), sections));
    }

    // ---- internals ----

    private static bool LooksLikePdf(byte[] bytes) =>
        bytes.Length >= PdfMagic.Length && bytes.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic);

    /// <summary>
    /// Decodes the saved bytes. Credible's pages declare windows-1252 in places and UTF-8 in
    /// others; AngleSharp resolves the declared encoding itself when handed text, so the bytes
    /// are decoded permissively here and the parser does the rest.
    /// </summary>
    private static string DecodeText(byte[] bytes) =>
        System.Text.Encoding.UTF8.GetString(bytes);

    private static List<ClientExportSection> ReadSections(IDocument document)
    {
        var sections = new List<ClientExportSection>();
        string? banner = null;
        var fields = new List<ClientExportField>();
        string? pendingLabel = null;

        // A pending label with no value cell after it is a real shape in these documents —
        // the test export carries 678 label cells against 651 value cells. It becomes a field
        // with no value, which the mapper reads as Blank: the label was there, the client has
        // nothing in it. Dropping it would report the row as missing instead.
        void FlushPendingLabel()
        {
            if (pendingLabel is not null)
            {
                fields.Add(new ClientExportField(pendingLabel, null));
                pendingLabel = null;
            }
        }

        void CloseSection()
        {
            FlushPendingLabel();
            if (banner is not null)
                sections.Add(new ClientExportSection(banner, fields));
            fields = [];
        }

        foreach (var cell in document.QuerySelectorAll("td"))
        {
            if (cell.ClassList.Contains(DocumentHeaderClass))
                continue;

            if (HasAnyClass(cell, BannerClasses))
            {
                CloseSection();
                banner = Text(cell);
                continue;
            }

            if (banner is null)
                continue;

            if (HasAnyClass(cell, LabelClasses))
            {
                FlushPendingLabel();
                var label = Text(cell);
                if (label.Length > 0)
                    pendingLabel = label;
                continue;
            }

            if (HasAnyClass(cell, ValueClasses) && pendingLabel is not null)
            {
                var value = Text(cell);
                fields.Add(new ClientExportField(pendingLabel, value.Length == 0 ? null : value));
                pendingLabel = null;
            }
        }

        CloseSection();
        return sections;
    }

    private static bool HasAnyClass(IElement element, string[] classes)
    {
        foreach (var candidate in classes)
        {
            if (element.ClassList.Contains(candidate))
                return true;
        }

        return false;
    }

    /// <summary>Cell text with Credible's non-breaking-space padding removed.</summary>
    private static string Text(IElement element) =>
        CredibleProfileMapping.Clean(element.TextContent);

    /// <summary>
    /// Credible's own record id, from the page's <c>client_id</c>.
    ///
    /// <para>
    /// Only when the document names exactly one. A saved page that mentions two different ids is
    /// not something to resolve by preference — this is the dedupe key, and guessing it wrong
    /// merges two clinical records.
    /// </para>
    /// </summary>
    private static string? ReadClientId(string markup)
    {
        var ids = ClientIdPattern.Matches(markup)
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return ids.Count == 1 ? ids[0] : null;
    }
}
