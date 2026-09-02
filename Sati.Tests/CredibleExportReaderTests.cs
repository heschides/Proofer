using Sati.Contracts.V1;
using Sati.Data;
using System.IO;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// Reading a saved Credible print view.
///
/// <para>
/// The fixture below is hand-authored to match the real markup: an <c>shHeader</c> title row, an
/// <c>shc</c> top-level banner, <c>hc</c> sub-banners, four-column <c>lc</c>/<c>vc</c> rows with
/// non-breaking-space padding, an <c>lc2</c>/<c>vc2</c> pair, an empty value cell, a label with
/// no value cell after it, a <c>__VIEWSTATE</c> field, and the external script and stylesheet
/// references the real page carries. Values are Credible's demo client, which is fabricated.
/// </para>
/// </summary>
public sealed class CredibleExportReaderTests
{
    private static readonly IClientExportReader Reader = new CredibleExportReader();

    // ---- Structure ----

    [Fact]
    public void ItReadsTheTopLevelSectionAndItsFields()
    {
        var result = Reader.Read(Fixture());

        Assert.True(result.Succeeded);
        var section = result.Document!.Sections.Single(s => s.Banner == "CONSUMER INFO");
        Assert.Equal("CREDIBLE", section.Fields.Single(f => f.Label == "First Name").Value);
        Assert.Equal("12345678A", section.Fields.Single(f => f.Label == "MaineCare ID").Value);
    }

    // `hc` is a full-width sub-banner in this document, not a column header — all 44 of them in
    // the real export carry colspan="4". Reading it as a column header would fold Consumer
    // Address, Consumer Demograpics and Medical into whatever section preceded them, and the
    // field map keys on exactly those names.
    [Fact]
    public void ASubBannerStartsItsOwnSectionRatherThanExtendingThePreviousOne()
    {
        var result = Reader.Read(Fixture());

        var address = result.Document!.Sections.Single(s => s.Banner == "Consumer Address");
        Assert.Equal("Alexander", address.Fields.Single(f => f.Label == "City").Value);

        // And the address labels did not leak upward into the section above.
        var info = result.Document.Sections.Single(s => s.Banner == "CONSUMER INFO");
        Assert.DoesNotContain(info.Fields, f => f.Label == "City");
    }

    // The page's own title row carries the client's name, id and date of birth. Treating it as a
    // banner would open a section that swallowed the first real block of fields.
    [Fact]
    public void ThePageTitleRowIsNotASection()
    {
        var result = Reader.Read(Fixture());

        Assert.DoesNotContain(
            result.Document!.Sections,
            section => section.Banner.Contains("CREDIBLE TEST", StringComparison.Ordinal));
    }

    [Fact]
    public void BothCellStylesAreReadAsLabelAndValue()
    {
        var result = Reader.Read(Fixture());

        // Primary Diagnosis is an lc2/vc2 pair in the fixture, as it is in the real document.
        var medical = result.Document!.Sections.Single(s => s.Banner == "Medical");
        Assert.Equal(
            "(F84.0) Autistic disorder",
            medical.Fields.Single(f => f.Label == "Primary Diagnosis").Value);
    }

    [Fact]
    public void PaddingIsTrimmedFromLabelsAndValues()
    {
        var result = Reader.Read(Fixture());

        var info = result.Document!.Sections.Single(s => s.Banner == "CONSUMER INFO");
        Assert.Contains(info.Fields, f => f.Label == "First Name" && f.Value == "CREDIBLE");
    }

    // Present-but-empty, which the mapper reads as Blank. Distinct from a label the export does
    // not carry at all, which is what "Hide Empty Profile Fields" produces.
    [Fact]
    public void AnEmptyValueCellBecomesAFieldWithNoValue()
    {
        var result = Reader.Read(Fixture());

        var info = result.Document!.Sections.Single(s => s.Banner == "CONSUMER INFO");
        var saddleback = info.Fields.Single(f => f.Label == "Saddleback ID");
        Assert.Null(saddleback.Value);
    }

    // The real export carries 678 label cells against 651 value cells, so a trailing label with
    // no partner is an ordinary shape rather than a malformed document. It is still a label the
    // export carried, so it must not be dropped — dropping it would report the field as missing.
    [Fact]
    public void ATrailingLabelWithNoValueCellIsStillReported()
    {
        var result = Reader.Read(Fixture());

        var info = result.Document!.Sections.Single(s => s.Banner == "CONSUMER INFO");
        var orphan = info.Fields.Single(f => f.Label == "Foster Home ID");
        Assert.Null(orphan.Value);
    }

    // ---- The client id ----

    [Fact]
    public void TheCredibleClientIdIsTakenFromThePage()
    {
        var result = Reader.Read(Fixture());

        Assert.Equal("21864", result.Document!.CredibleClientId);
    }

    // This is the dedupe key. A page naming two different ids is not something to resolve by
    // preference — guessing wrong merges two clinical records.
    [Fact]
    public void AnAmbiguousClientIdIsNotGuessedAt()
    {
        var markup = Fixture().Replace(
            "client_printview.aspx?client_id=21864",
            "client_printview.aspx?client_id=21864&amp;other=client_id=99999",
            StringComparison.Ordinal);

        var result = Reader.Read(markup);

        Assert.True(result.Succeeded);
        Assert.Null(result.Document!.CredibleClientId);
    }

    // ---- Refusals ----

    [Fact]
    public async Task APdfIsRefusedByItsMagicBytesEvenWhenNamedHtml()
    {
        // Named .html deliberately: extension checks are what an operator's rename defeats.
        var path = Path.Combine(Path.GetTempPath(), $"credible-{Guid.NewGuid():N}.html");
        await File.WriteAllBytesAsync(path, "%PDF-1.7\n%âãÏÓ\n"u8.ToArray());
        try
        {
            var result = await Reader.ReadAsync(path);

            Assert.False(result.Succeeded);
            Assert.Equal(ClientExportRejection.NotHtml, result.Rejection);
            Assert.Contains("printing to PDF", result.Describe(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Ctrl+S on the Credible application window saves the frameset definition, not the print
    // view inside it: about 14KB with no client data at all.
    [Fact]
    public void TheApplicationFramesetIsRefused()
    {
        var result = Reader.Read(
            """
            <html><head><title>Credible</title></head>
            <frameset rows="95,*">
              <frame name="banner" src="/qualifacts_nav.asp">
              <frame name="main" src="/common/my_cw.asp">
            </frameset></html>
            """);

        Assert.False(result.Succeeded);
        Assert.Equal(ClientExportRejection.ApplicationShell, result.Rejection);
        Assert.Contains("application window", result.Describe(), StringComparison.Ordinal);
    }

    // The print options page, saved before Print View was pressed. Real HTML from the right URL,
    // with checkboxes and no client data.
    [Fact]
    public void TheOptionsPageSavedBeforePrintViewIsRefused()
    {
        var result = Reader.Read(
            """
            <html><body>
            <form action="./client_printview.aspx?client_id=21864">
              <table><tr><td>Print Options:</td></tr>
              <tr><td><input id="cbClients" type="checkbox" checked="checked" />Client Profile</td></tr>
              <tr><td><input type="submit" value="Print View" /></td></tr></table>
            </form></body></html>
            """);

        Assert.False(result.Succeeded);
        Assert.Equal(ClientExportRejection.NotAPrintView, result.Rejection);
        Assert.Contains("Print View first", result.Describe(), StringComparison.Ordinal);
    }

    // ---- The safety property ----

    // A saved print view references scripts on assets.cbh3.crediblebh.com and carries an Akamai
    // RUM beacon. If this ever gained a browsing context with a loader, opening a client record
    // would mean outbound requests to Credible and Akamai from the machine holding it. The
    // script below would inject a section if it ran; nothing here should run it.
    [Fact]
    public void ScriptInTheDocumentIsNotExecuted()
    {
        var markup = Fixture().Replace(
            "<!--INJECTION-POINT-->",
            """<script>document.write('<table><tr><td class="hc" colspan="4"><b>INJECTED</b></td></tr></table>');</script>""",
            StringComparison.Ordinal);

        var result = Reader.Read(markup);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Document!.Sections, section => section.Banner == "INJECTED");
    }

    // The fixture carries the real page's external references. This asserts they change nothing
    // about the parse — and would start doing DNS, and failing or hanging, if a loader were added.
    [Fact]
    public void ExternalResourceReferencesDoNotAffectTheParse()
    {
        var withRefs = Reader.Read(Fixture());
        var withoutRefs = Reader.Read(Fixture(includeExternalReferences: false));

        Assert.Equal(
            withoutRefs.Document!.Sections.Select(s => s.Banner),
            withRefs.Document!.Sections.Select(s => s.Banner));
    }

    // ---- Reader and mapper together ----

    [Fact]
    public void AReadExportMapsThroughToADraft()
    {
        var result = Reader.Read(Fixture());

        var draft = CredibleProfileMapping.Map(result.Document!, CredibleLayoutProfile.Default);

        Assert.Equal("CREDIBLE", Value(draft, CredibleFields.FirstName));
        Assert.Equal("1990-01-02", Value(draft, CredibleFields.BirthDate));
        Assert.Equal("12345678A", Value(draft, CredibleFields.MaineCareId));
        Assert.Equal("Alexander", Value(draft, CredibleFields.BillingCity));
        Assert.Equal("F84.0", Value(draft, CredibleFields.DiagnosisCode));

        // Own guardian YES means no guardian.
        Assert.Equal("false", Value(draft, CredibleFields.HasGuardian));

        // And the field the PDF path silently filled with the SSN.
        Assert.Equal("000001800", Value(draft, CredibleFields.Ssn));
    }

    private static string? Value(CredibleProfileDraft draft, string satiField) =>
        draft.Fields.Single(drafted => drafted.SatiField == satiField).Value;

    // ---- Fixture ----

    private static string Fixture(bool includeExternalReferences = true)
    {
        var externals = includeExternalReferences
            ? """
              <script type='text/javascript' src='//assets.cbh3.crediblebh.com/js/global.js'></script>
              <link rel='stylesheet' href='//assets.cbh3.crediblebh.com/css/stylebase.css' type='text/css' />
              """
            : string.Empty;

        //   is the non-breaking space the real markup pads every cell with.
        return $$"""
            <html><head>{{externals}}</head>
            <body>
            <form name="form1" method="post" action="./client_printview.aspx?client_id=21864" id="form1">
            <input type="hidden" name="__VIEWSTATE" id="__VIEWSTATE" value="cG6f/iNEtKNs4g3lEwBcFcA0o" />
            <!--INJECTION-POINT-->
            <table>
              <tr><td class="shHeader" colspan="4"><b>CREDIBLE TEST (21864) [DOB: 1/2/1990]</b></td></tr>
              <tr><td class="shc" colspan="4"><b>CONSUMER INFO</b></td></tr>
              <tr>
                <td class="lc"><b> First Name</b></td><td class="vc">CREDIBLE </td>
                <td class="lc"><b> Last Name</b></td><td class="vc">TEST </td>
              </tr>
              <tr>
                <td class="lc"><b> Saddleback ID</b></td><td class="vc"> </td>
                <td class="lc"><b> Consumer ID</b></td><td class="vc">21864 </td>
              </tr>
              <tr>
                <td class="lc"><b> MaineCare ID</b></td><td class="vc">12345678A </td>
                <td class="lc"><b> DOB</b></td><td class="vc">01/02/1990 </td>
              </tr>
              <tr>
                <td class="lc"><b> SSN</b></td><td class="vc">000001800 </td>
                <td class="lc"><b> Consumer is Own Guardian?</b></td><td class="vc">YES </td>
              </tr>
              <tr>
                <td class="lc"><b> Foster Home ID</b></td>
              </tr>
              <tr><td class="hc" colspan="4"><b>Consumer Address</b></td></tr>
              <tr>
                <td class="lc"><b> address1</b></td><td class="vc">1 Choice Hotels Circle </td>
                <td class="lc"><b> City</b></td><td class="vc">Alexander </td>
              </tr>
              <tr>
                <td class="lc"><b> State</b></td><td class="vc">MD </td>
                <td class="lc"><b> Zip</b></td><td class="vc">20850 </td>
              </tr>
              <tr><td class="hc" colspan="4"><b>Consumer Demograpics</b></td></tr>
              <tr>
                <td class="lc"><b> Age</b></td><td class="vc">36 </td>
                <td class="lc"><b> Gender</b></td><td class="vc">Male </td>
              </tr>
              <tr><td class="hc" colspan="4"><b>Medical</b></td></tr>
              <tr>
                <td class="lc2"><b> Primary Diagnosis</b></td>
                <td class="vc2">(F84.0) Autistic disorder </td>
              </tr>
            </table>
            </form></body></html>
            """;
    }
}
