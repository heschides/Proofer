namespace Sati.Contracts.V1;

/// <summary>One label/value pair as it appeared in the export.</summary>
/// <param name="Label">The label text, already trimmed of the markup's padding.</param>
/// <param name="Value">
/// The value text, or null where the cell was present but empty. Present-but-empty and absent
/// are different facts: the first says the export carried this field and the client has no
/// value, the second says the export did not carry it at all. Collapsing them would make a
/// truncated export indistinguishable from a sparse client.
/// </param>
public sealed record ClientExportField(string Label, string? Value);

/// <summary>One banner-delimited section of the export.</summary>
public sealed record ClientExportSection(string Banner, IReadOnlyList<ClientExportField> Fields);

/// <summary>
/// A Credible client print view, parsed into sections of label/value pairs.
///
/// <para>
/// Deliberately not a DOM. The reader that produces this holds the HTML parser; everything
/// downstream — the mapper, its tests, and any future server-side path — works on this shape,
/// so the parsing library stays at the edge and the mapping rule can be tested from a literal
/// with no markup in sight.
/// </para>
/// </summary>
/// <param name="CredibleClientId">
/// Credible's own identifier for the record, from the page's <c>client_id</c>. The dedupe and
/// idempotency key for import — see CREDIBLE_IMPORT_DESIGN.md.
/// </param>
public sealed record ClientExportDocument(
    string? CredibleClientId,
    IReadOnlyList<ClientExportSection> Sections);

/// <summary>
/// How a raw export value becomes a Sati one.
///
/// <para>
/// Named on the mapping rather than inferred from the field, because the two dangerous
/// conversions are invisible from the label alone: <c>Consumer is Own Guardian?</c> is the
/// negation of <c>HasGuardian</c>, and <c>Primary Diagnosis</c> carries a code and a description
/// in one cell.
/// </para>
/// </summary>
public enum CredibleValueKind
{
    /// <summary>Taken as written.</summary>
    Text,

    /// <summary>A Credible date, <c>MM/DD/YYYY</c>, normalized to ISO.</summary>
    Date,

    /// <summary>Mapped onto Sati's gender vocabulary.</summary>
    Gender,

    /// <summary><c>(F84.0) Autistic disorder</c> — the parenthesized code is kept, the prose dropped.</summary>
    DiagnosisCode,

    /// <summary>YES/NO to a boolean.</summary>
    YesNo,

    /// <summary>
    /// YES/NO to the boolean's negation. <c>Consumer is Own Guardian? = YES</c> means the
    /// consumer has no guardian, so a straight copy sets <c>HasGuardian</c> backwards on every
    /// imported record.
    /// </summary>
    InvertedYesNo
}

/// <summary>One label in one section, and the Sati field it feeds.</summary>
public sealed record CredibleFieldMapping(
    string Section,
    string Label,
    string SatiField,
    CredibleValueKind Kind = CredibleValueKind.Text);

/// <summary>
/// Which labels in which sections become which Sati fields.
///
/// <para>
/// Data, not code. Credible print views are configurable per agency and the page is a vendor
/// artifact that changes with their release cycle, so a new agency variant or a Credible UI
/// update should be an edit to a stored profile rather than a build. <see cref="Default"/> is
/// the starting point, verified against a real export on 2026-09-01; an agency's own profile
/// is stored as JSON on Settings.
/// </para>
/// </summary>
public sealed record CredibleLayoutProfile(
    string Name,
    int Version,
    IReadOnlyList<CredibleFieldMapping> Fields)
{
    /// <summary>
    /// The stock Credible layout.
    ///
    /// <para>
    /// Every section name here is the string that is actually in the document, including
    /// <c>Consumer Demograpics</c> — the misspelling is Credible's, in their own UI. Nobody
    /// would reproduce that constant correctly from memory, which is the argument for the
    /// profile being data: when a Credible release fixes the typo, this is a profile edit and
    /// not a bug report.
    /// </para>
    /// </summary>
    public static CredibleLayoutProfile Default { get; } = new(
        "Credible stock print view",
        1,
        [
            new("CONSUMER INFO", "First Name", CredibleFields.FirstName),
            new("CONSUMER INFO", "Last Name", CredibleFields.LastName),
            new("CONSUMER INFO", "DOB", CredibleFields.BirthDate, CredibleValueKind.Date),
            new("CONSUMER INFO", "Consumer ID", CredibleFields.CredibleClientId),
            new("CONSUMER INFO", "MaineCare ID", CredibleFields.MaineCareId),
            new("CONSUMER INFO", "SSN", CredibleFields.Ssn),
            new("CONSUMER INFO", "Consumer is Own Guardian?", CredibleFields.HasGuardian,
                CredibleValueKind.InvertedYesNo),

            new("Consumer Address", "address1", CredibleFields.BillingStreet),
            new("Consumer Address", "City", CredibleFields.BillingCity),
            new("Consumer Address", "State", CredibleFields.BillingState),
            new("Consumer Address", "Zip", CredibleFields.BillingZip),

            new("Consumer Contact Info", "Home Phone", CredibleFields.PhoneNumber),
            new("Consumer Contact Info", "Consumer Email", CredibleFields.Email),

            // Kept as two fields rather than one joined name. A 1:1 label-to-field mapper is
            // far easier to reason about and to test; composing "First Last" is the review
            // screen's business, at the point it builds a SavePersonRequest.
            new("Consumer Guardian #1", "Guardian First Name", CredibleFields.GuardianFirstName),
            new("Consumer Guardian #1", "Guardian Last Name", CredibleFields.GuardianLastName),

            new("Consumer Demograpics", "Gender", CredibleFields.Gender, CredibleValueKind.Gender),

            new("Medical", "Primary Diagnosis", CredibleFields.DiagnosisCode,
                CredibleValueKind.DiagnosisCode)
        ]);
}

/// <summary>
/// Names for the fields a draft can carry.
///
/// <para>
/// Mostly <see cref="Person"/> property names, but not all of them: the guardian name arrives as
/// two cells, and the SSN is not a <c>SavePersonRequest</c> field at all — it travels its own
/// audited, encrypted route and must never ride the demographic save.
/// </para>
/// </summary>
public static class CredibleFields
{
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string BirthDate = "birthDate";
    public const string Gender = "gender";
    public const string CredibleClientId = "credibleClientId";
    public const string MaineCareId = "maineCareId";
    public const string DiagnosisCode = "diagnosisCode";
    public const string HasGuardian = "hasGuardian";
    public const string GuardianFirstName = "guardianFirstName";
    public const string GuardianLastName = "guardianLastName";
    public const string PhoneNumber = "phoneNumber";
    public const string Email = "email";
    public const string BillingStreet = "billingStreet";
    public const string BillingCity = "billingCity";
    public const string BillingState = "billingState";
    public const string BillingZip = "billingZip";

    /// <summary>
    /// Never part of a demographic save. Held separately by the review screen and applied through
    /// the SSN route, which encrypts it and audits the write without the value.
    /// </summary>
    public const string Ssn = "ssn";
}

/// <summary>What happened to one mapped field.</summary>
public enum CredibleFieldStatus
{
    /// <summary>A value was found and converted.</summary>
    Mapped,

    /// <summary>The label was found and its cell was empty. The client has no value for it.</summary>
    Blank,

    /// <summary>
    /// The section was present but the label was not. The export's shape differs from the
    /// profile — usually "Hide Empty Profile Fields" was left on, occasionally a Credible change.
    /// </summary>
    LabelMissing,

    /// <summary>The section was not in the export at all — usually an unticked print option.</summary>
    SectionMissing,

    /// <summary>
    /// A value was found but could not be converted. Reported rather than guessed at: a date
    /// Sati cannot read is a fact for the reviewer, not licence to invent one.
    /// </summary>
    Unreadable
}

/// <summary>
/// One field of the draft, carrying where it came from.
///
/// <para>
/// The provenance is not decoration. The review screen shows the reviewer which section and
/// label produced each value, because a field-by-field acceptance step is only meaningful if
/// they can see what they are accepting.
/// </para>
/// </summary>
/// <param name="RawValue">Exactly what the cell held, for display beside the converted value.</param>
/// <param name="Value">The converted value, or null when there is nothing usable.</param>
public sealed record CredibleFieldDraft(
    string SatiField,
    string Section,
    string Label,
    string? RawValue,
    string? Value,
    CredibleFieldStatus Status);

/// <summary>
/// The result of mapping one export: what was found, what was not, and what the profile had no
/// opinion about.
/// </summary>
/// <param name="MissingSections">
/// Profile sections absent from the export, named once rather than once per field.
/// </param>
/// <param name="UnmappedLabels">
/// Labels the export carried that the profile does not map, as <c>Section / Label</c>. Not an
/// error — most of the export is deliberately ignored — but the signal that tells whether a
/// Credible layout has moved on from the profile.
/// </param>
public sealed record CredibleProfileDraft(
    string? CredibleClientId,
    IReadOnlyList<CredibleFieldDraft> Fields,
    IReadOnlyList<string> MissingSections,
    IReadOnlyList<string> UnmappedLabels)
{
    // The lambda parameter is not called "field": in C# 14 that is a contextual keyword inside
    // a property accessor and binds to a synthesized backing field instead.
    public bool HasAnyValue => Fields.Any(drafted => drafted.Status == CredibleFieldStatus.Mapped);

    public IEnumerable<CredibleFieldDraft> Problems =>
        Fields.Where(drafted => drafted.Status is CredibleFieldStatus.Unreadable
                                              or CredibleFieldStatus.LabelMissing
                                              or CredibleFieldStatus.SectionMissing);
}

/// <summary>Why an artifact was refused as a Credible client export.</summary>
public enum ClientExportRejection
{
    None = 0,

    /// <summary>A PDF. The print view must be saved as HTML — see CREDIBLE_IMPORT_DESIGN.md.</summary>
    NotHtml,

    /// <summary>The Credible application window rather than a print view: a frameset shell.</summary>
    ApplicationShell,

    /// <summary>HTML, but with no section banners — usually the options page, saved before Print View.</summary>
    NotAPrintView,

    /// <summary>The file could not be read or parsed at all.</summary>
    Unreadable
}

/// <summary>
/// A read attempt: the document, or why it was refused.
///
/// <para>
/// Refusals are named and specific because all three wrong artifacts look plausible to whoever
/// produced them, and telling an operator "this looks like the Credible application window, not
/// a print view" is worth more than any amount of parser tolerance.
/// </para>
/// </summary>
public sealed record ClientExportReadResult(
    ClientExportDocument? Document,
    ClientExportRejection Rejection,
    string? Detail = null)
{
    public bool Succeeded => Document is not null && Rejection == ClientExportRejection.None;

    public static ClientExportReadResult Accepted(ClientExportDocument document) =>
        new(document, ClientExportRejection.None);

    public static ClientExportReadResult Refused(
        ClientExportRejection rejection, string? detail = null) =>
        new(null, rejection, detail);

    /// <summary>Operator-facing explanation, naming the fix rather than the symptom.</summary>
    public string Describe() => Rejection switch
    {
        ClientExportRejection.None => string.Empty,
        ClientExportRejection.NotHtml =>
            "This is a PDF. Open the client's print view, press Print View, then save the page " +
            "as a web page — printing to PDF loses which value belongs to which field.",
        ClientExportRejection.ApplicationShell =>
            "This is the Credible application window, not a client print view. Open the print " +
            "view in its own tab and save that page.",
        ClientExportRejection.NotAPrintView =>
            "This page has no client sections. It looks like the print options page — press " +
            "Print View first, then save the page it produces.",
        _ => Detail ?? "This file could not be read as a Credible client export."
    };
}
