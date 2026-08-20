using System.Reflection;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>
/// Fills an official Maine DHHS form by setting AcroForm field values on the
/// state's own PDF.
///
/// This is deliberately not how <c>PersonAuditPdfGenerator</c> or
/// <c>ATRequestPdfExporter</c> work. Those compose a Sati document with MigraDoc,
/// which is right for a document Sati owns and wrong for one it does not: a
/// redrawn state form is a lookalike, and a lookalike gets rejected at intake.
/// Here the page content stream is never touched. Values ride in the form layer
/// above it, so the layout, seal, and legal text come out of the filler exactly as
/// DHHS published them — asserted, not assumed, by <c>DhhsFormFillerTests</c>.
///
/// Runs server-side, and only server-side. The Authorized Representative form asks
/// for an SSN, so a desktop-side filler would require shipping decrypted SSNs to
/// every workstation. The client receives finished PDF bytes instead and the
/// plaintext never leaves this process. See <c>ISsnProtector</c>.
///
/// The result stays a fillable form rather than being flattened. Sati supplies
/// demographics and whatever consent choices a case manager explicitly recorded;
/// the consumer completes the remaining boxes and signs. Flattening would take the
/// consumer's own pen out of the document.
/// </summary>
public sealed class DhhsFormFiller
{
    /// <summary>
    /// Blank-form resources, by form. The revision is in the resource name so that a
    /// filled form can be traced to the blank it came from, and so replacing a
    /// revision is a visible code change rather than a file swap.
    /// </summary>
    private static readonly IReadOnlyDictionary<DhhsFormDefinition.FormKey, string> BlankResources =
        new Dictionary<DhhsFormDefinition.FormKey, string>
        {
            [DhhsFormDefinition.FormKey.AuthorizedRepresentative] =
                "Sati.Forms.AuthorizedRepresentative-2024-10-10.pdf",
            [DhhsFormDefinition.FormKey.AuthorizationToRelease] =
                "Sati.Forms.AuthorizationToRelease-2025-11-24.pdf",
        };

    /// <summary>
    /// Produces the filled form.
    ///
    /// <paramref name="selections"/> carries consent choices a case manager entered
    /// on the consumer's instruction. Pass <see cref="DhhsFormDefinition.Selections.None"/>
    /// to fill demographics only, which is the safe default: an unchecked box asks a
    /// question, a wrongly checked one answers it on the consumer's behalf.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A demographic mapping reached a consent field, or a selection named a field
    /// that is not a consent field of this form.
    /// </exception>
    public byte[] Fill(
        DhhsFormDefinition.FormKey form,
        DhhsFormDefinition.Subject subject,
        DhhsFormDefinition.Selections selections)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(selections);

        using var blank = OpenBlank(form);
        var document = PdfReader.Open(blank, PdfDocumentOpenMode.Modify);
        var acroForm = document.AcroForm
            ?? throw new InvalidOperationException($"The blank {form} form has no AcroForm.");

        // The blanks ship without /NeedAppearances, so a viewer would render the
        // fields using appearance streams that were built for empty values — the
        // text is in the file but invisible until the field is clicked. Setting it
        // asks the viewer to rebuild them from the values below.
        acroForm.Elements.SetBoolean("/NeedAppearances", true);

        foreach (var (name, value) in DhhsFormDefinition.ProfileFields(form, subject))
            SetText(acroForm, form, name, value);

        foreach (var (name, value) in selections.Text ?? new Dictionary<string, string>())
        {
            DhhsFormDefinition.AssertSelectable(form, name);
            if (!string.IsNullOrWhiteSpace(value))
                SetFieldText(acroForm, name, value.Trim());
        }

        foreach (var (name, isChecked) in selections.Checks ?? new Dictionary<string, bool>())
        {
            DhhsFormDefinition.AssertSelectable(form, name);
            SetCheck(acroForm, name, isChecked);
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    /// <summary>Demographic write, routed through the guard that forbids consent fields.</summary>
    private static void SetText(
        PdfAcroForm acroForm,
        DhhsFormDefinition.FormKey form,
        string name,
        string value)
    {
        DhhsFormDefinition.AssertFillable(form, name);
        SetFieldText(acroForm, name, value);
    }

    private static void SetFieldText(PdfAcroForm acroForm, string name, string value)
    {
        if (acroForm.Fields[name] is not PdfTextField field)
            throw new InvalidOperationException($"'{name}' is not a text field on this form.");
        field.Value = new PdfString(value);
    }

    /// <summary>
    /// Sets a checkbox.
    ///
    /// The two forms disagree on the name of the checked state — the Authorized
    /// Representative form uses <c>/Yes</c> and the release form uses <c>/On</c> —
    /// so the state is read from the field's own appearance dictionary rather than
    /// assumed. Guessing wrong produces a box that is set in the file and blank on
    /// the page, which is the worst of the three outcomes.
    /// </summary>
    private static void SetCheck(PdfAcroForm acroForm, string name, bool isChecked)
    {
        if (acroForm.Fields[name] is not PdfCheckBoxField field)
            throw new InvalidOperationException($"'{name}' is not a checkbox on this form.");

        if (!isChecked)
        {
            field.Checked = false;
            return;
        }

        var onState = OnStateOf(field)
            ?? throw new InvalidOperationException(
                $"Checkbox '{name}' declares no checked appearance state.");
        field.Elements.SetName(PdfAcroField.Keys.V, onState);
        field.Elements.SetName(PdfAnnotation.Keys.AS, onState);
    }

    private static string? OnStateOf(PdfCheckBoxField field)
    {
        var normal = field.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        return normal?.Elements.Keys.FirstOrDefault(
            state => !string.Equals(state, "/Off", StringComparison.Ordinal));
    }

    private static Stream OpenBlank(DhhsFormDefinition.FormKey form)
    {
        if (!BlankResources.TryGetValue(form, out var resource))
            throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown DHHS form.");

        return typeof(DhhsFormFiller).GetTypeInfo().Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded blank form '{resource}' is missing from the assembly.");
    }
}
