using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Sati.Forms;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// Points PdfSharp at the Windows font collection before any test runs.
///
/// Same reason as <c>Sati.Tests.PdfFontResolverInitializer</c>: PdfSharp resolves a
/// field's font when it materializes a <c>PdfTextField</c>, caches the outcome, and
/// takes every later PDF test down with it if the first one ran unresolved.
/// <c>Sati.Api/Program.cs</c> does the same at its own startup, but a unit test of
/// the filler never goes through <c>Program</c>.
/// </summary>
internal static class ApiPdfFontResolverInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;
}

public sealed class DhhsFormFillerTests
{
    private static readonly DhhsFormDefinition.Subject Subject = new(
        FullName: "Sample, Test Q.",
        BirthDate: new DateTime(1970, 1, 15),
        Address: "12 Example Rd, Augusta, ME 04330",
        PhoneNumber: "(207) 555-0100",
        SocialSecurityNumber: "999-00-1234",
        RepresentativeName: "Sample, Rep",
        RepresentativeAddress: "34 Placeholder St, Bangor, ME 04401",
        RepresentativePhone: "(207) 555-0199",
        RepresentativeEmail: "rep@example.invalid");

    public static TheoryData<DhhsFormDefinition.FormKey> AllForms => new()
    {
        DhhsFormDefinition.FormKey.AuthorizedRepresentative,
        DhhsFormDefinition.FormKey.AuthorizationToRelease,
    };

    /// <summary>
    /// The reason this filler exists. These are the state's forms, not Sati's, so the
    /// printed page has to come out of the filler exactly as DHHS published it — a
    /// redrawn lookalike gets rejected at intake.
    ///
    /// Comparing the decompressed content stream of every page is the strongest
    /// available statement of that: it is the drawing instructions for the page. If a
    /// future change starts stamping text into the content stream instead of the form
    /// layer, or flattens the fields, this fails.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllForms))]
    public void Filling_leaves_every_page_content_stream_byte_identical(DhhsFormDefinition.FormKey form)
    {
        var blank = ReadBlank(form);
        var filled = new DhhsFormFiller().Fill(form, Subject, DhhsFormDefinition.Selections.None);

        using var before = PdfReader.Open(new MemoryStream(blank), PdfDocumentOpenMode.Import);
        using var after = PdfReader.Open(new MemoryStream(filled), PdfDocumentOpenMode.Import);

        Assert.Equal(before.PageCount, after.PageCount);
        for (var page = 0; page < before.PageCount; page++)
        {
            Assert.Equal(
                Digest(before.Pages[page].Contents.CreateSingleContent().Stream.UnfilteredValue),
                Digest(after.Pages[page].Contents.CreateSingleContent().Stream.UnfilteredValue));
            Assert.Equal(before.Pages[page].MediaBox.ToString(), after.Pages[page].MediaBox.ToString());
        }
    }

    [Fact]
    public void Demographic_values_reach_the_fields_they_are_mapped_to()
    {
        var filled = new DhhsFormFiller().Fill(
            DhhsFormDefinition.FormKey.AuthorizedRepresentative,
            Subject,
            DhhsFormDefinition.Selections.None);

        var fields = FieldsOf(filled);
        Assert.Equal("Sample, Test Q.", Text(fields, "Individual's Name"));
        Assert.Equal("01/15/1970", Text(fields, "Individual's DOB"));
        Assert.Equal("999-00-1234", Text(fields, "Individual's SSN"));
        Assert.Equal("rep@example.invalid", Text(fields, "AR Email Address"));
    }

    /// <summary>
    /// A birth date the profile never captured must leave the box empty rather than
    /// stamp a default. "01/01/0001" on a state form reads as an answer.
    /// </summary>
    [Fact]
    public void A_missing_profile_value_leaves_its_box_blank()
    {
        var filled = new DhhsFormFiller().Fill(
            DhhsFormDefinition.FormKey.AuthorizedRepresentative,
            Subject with { BirthDate = null, RepresentativeEmail = null },
            DhhsFormDefinition.Selections.None);

        var fields = FieldsOf(filled);
        Assert.True(string.IsNullOrEmpty(Text(fields, "Individual's DOB")));
        Assert.True(string.IsNullOrEmpty(Text(fields, "AR Email Address")));
    }

    /// <summary>
    /// The rule the whole design rests on: a profile says who someone is, never what
    /// they agreed to. No demographic mapping on either form may resolve to a field
    /// that records a decision, a signature, or an attestation.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllForms))]
    public void No_demographic_mapping_reaches_a_consent_field(DhhsFormDefinition.FormKey form)
    {
        var mapped = DhhsFormDefinition.ProfileFields(form, Subject).Keys;
        var consent = DhhsFormDefinition.ConsentFields(form);

        Assert.DoesNotContain(mapped, consent.Contains);
    }

    /// <summary>
    /// And the guard itself refuses, so a future mapping that reaches for a checkbox
    /// fails at the seam instead of quietly producing a signed-looking release.
    /// </summary>
    [Fact]
    public void Filling_a_consent_field_from_stored_data_is_refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            DhhsFormDefinition.AssertFillable(
                DhhsFormDefinition.FormKey.AuthorizationToRelease,
                "Include all drugalcohol information in the release"));

        Assert.Contains("never filled from stored data", refusal.Message);
    }

    /// <summary>
    /// Sati fills nothing on the consent half of the page unless a human said so, so
    /// a demographics-only fill must leave every consent box exactly as the blank had
    /// it. This is the whole-form statement of the rule the mapping test makes field
    /// by field.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllForms))]
    public void A_demographics_only_fill_sets_no_consent_field(DhhsFormDefinition.FormKey form)
    {
        var filled = new DhhsFormFiller().Fill(form, Subject, DhhsFormDefinition.Selections.None);
        var fields = FieldsOf(filled);

        foreach (var name in DhhsFormDefinition.ConsentFields(form))
        {
            if (!fields.Names.Contains(name))
                continue;
            var value = Raw(fields, name);
            Assert.True(
                value.Length == 0 || value is "()" or "/Off",
                $"'{name}' on {form} was set to {value} without a human choosing it.");
        }
    }

    /// <summary>
    /// The two forms disagree on the name of the checked state — <c>/Yes</c> on the
    /// Appointment form, <c>/On</c> on the release form. Reading it from the field
    /// rather than assuming one is what stops a box from being set in the file and
    /// blank on the printed page.
    /// </summary>
    [Fact]
    public void A_recorded_choice_uses_the_forms_own_checked_state()
    {
        var appointment = FieldsOf(new DhhsFormFiller().Fill(
            DhhsFormDefinition.FormKey.AuthorizedRepresentative,
            Subject,
            new DhhsFormDefinition.Selections(
                Checks: new Dictionary<string, bool> { ["Guardianship"] = true })));

        var release = FieldsOf(new DhhsFormFiller().Fill(
            DhhsFormDefinition.FormKey.AuthorizationToRelease,
            Subject,
            new DhhsFormDefinition.Selections(
                Checks: new Dictionary<string, bool> { ["Office of Aging and Disability Services"] = true })));

        Assert.Equal("/Yes", Raw(appointment, "Guardianship"));
        Assert.Equal("/On", Raw(release, "Office of Aging and Disability Services"));
    }

    /// <summary>
    /// A selection naming something that is not a consent field is a typo or an
    /// attempt to drive a demographic box through the human-choice channel. Ignoring
    /// it would let a case manager believe they recorded a choice the PDF never got.
    /// </summary>
    [Fact]
    public void A_selection_that_is_not_a_consent_field_is_refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            new DhhsFormFiller().Fill(
                DhhsFormDefinition.FormKey.AuthorizedRepresentative,
                Subject,
                new DhhsFormDefinition.Selections(
                    Checks: new Dictionary<string, bool> { ["Individual's Name"] = true })));

        Assert.Contains("not a consent field", refusal.Message);
    }

    /// <summary>
    /// The filled form stays fillable. Flattening would take the consumer's own pen
    /// out of a document they are the one who has to sign.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllForms))]
    public void The_filled_form_is_still_a_form(DhhsFormDefinition.FormKey form)
    {
        var filled = new DhhsFormFiller().Fill(form, Subject, DhhsFormDefinition.Selections.None);
        using var document = PdfReader.Open(new MemoryStream(filled), PdfDocumentOpenMode.Import);

        Assert.NotNull(document.AcroForm);
        Assert.NotEmpty(document.AcroForm!.Fields.Names);
    }

    private static string Text(PdfAcroField.PdfAcroFieldCollection fields, string name) =>
        Raw(fields, name).Trim('(', ')');

    /// <summary>The field's stored value, or empty when the form has no such field.</summary>
    private static string Raw(PdfAcroField.PdfAcroFieldCollection fields, string name) =>
        fields[name] is { } field ? field.Value?.ToString() ?? string.Empty : string.Empty;

    private static PdfAcroField.PdfAcroFieldCollection FieldsOf(byte[] pdf)
    {
        var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return document.AcroForm!.Fields;
    }

    private static string Digest(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private static byte[] ReadBlank(DhhsFormDefinition.FormKey form)
    {
        var resource = form switch
        {
            DhhsFormDefinition.FormKey.AuthorizedRepresentative =>
                "Sati.Forms.AuthorizedRepresentative-2024-10-10.pdf",
            DhhsFormDefinition.FormKey.AuthorizationToRelease =>
                "Sati.Forms.AuthorizationToRelease-2025-11-24.pdf",
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        using var stream = typeof(DhhsFormFiller).Assembly.GetManifestResourceStream(resource)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
