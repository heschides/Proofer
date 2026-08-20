using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>
/// Sole owner of which consumer value belongs in which box on an official Maine
/// DHHS form, and — more importantly — of which boxes Sati is never allowed to
/// fill at all.
///
/// These are not Sati-generated documents. Unlike <c>ATRequestPdfExporter</c>,
/// which draws a Sati summary page from scratch, these two PDFs are the state's
/// own forms and are filled by setting AcroForm field values only. The page
/// content stream is never rewritten, so the official layout, seal, and legal
/// text survive byte-for-byte. <c>DhhsFormFillerTests</c> asserts that.
///
/// THE CENTRAL RULE: a profile answers "who is this person", never "what did
/// this person agree to". Every checkbox on both forms, and every signature,
/// printed name, and signing date, encodes a decision the consumer makes at the
/// moment of signing — which agencies may receive records, what authority a
/// representative holds, whether substance-use records (42 CFR Part 2) and
/// mental-health records travel with the rest. Deriving any of those from stored
/// data would manufacture a consent nobody gave, on a page the consumer then
/// signs. <see cref="ConsentFields"/> names them and
/// <see cref="AssertFillable"/> makes filling one an exception rather than a
/// code-review question.
///
/// Case-manager-supplied selections are a separate, explicit input path — see the
/// selections argument on the fill request. They are still never inferred here.
/// </summary>
public static class DhhsFormDefinition
{
    /// <summary>Identifies a form without leaking a filename or a storage path to a caller.</summary>
    public enum FormKey
    {
        /// <summary>Appointment of Authorized Representative (rev. 10.10.24).</summary>
        AuthorizedRepresentative,

        /// <summary>Authorization to Release/Obtain Information (rev. 11.24.25).</summary>
        AuthorizationToRelease,
    }

    /// <summary>
    /// The demographic facts a form can ask for. A flat record rather than the
    /// <c>Person</c> entity because <c>Sati.Contracts</c> must not depend on a
    /// persistence model, and because it keeps the set of values that can reach an
    /// official form small enough to read in one screen.
    ///
    /// <paramref name="SocialSecurityNumber"/> arrives decrypted and is expected to
    /// be null on every path except server-side form generation. It must not be
    /// carried on any DTO; see the containment test.
    /// </summary>
    public sealed record Subject(
        string? FullName,
        DateTime? BirthDate,
        string? Address,
        string? PhoneNumber,
        string? SocialSecurityNumber,
        string? RepresentativeName,
        string? RepresentativeAddress,
        string? RepresentativePhone,
        string? RepresentativeEmail);

    /// <summary>
    /// Consent choices a case manager entered on the consumer's instruction, carried
    /// explicitly so that the only way a box gets checked is that a human said so.
    ///
    /// This is the deliberate exception to <see cref="AssertFillable"/>: these fields
    /// ARE consent fields, and that is precisely why they may only arrive here, by
    /// name, from a caller. Nothing derives them.
    ///
    /// <paramref name="Checks"/> maps a checkbox field to whether it is checked;
    /// <paramref name="Text"/> supplies the free text that qualifies one ("Other:",
    /// the recipient organization, an earlier expiry date). A field absent from both
    /// is left blank for the consumer to complete on paper.
    /// </summary>
    public sealed record Selections(
        IReadOnlyDictionary<string, bool>? Checks = null,
        IReadOnlyDictionary<string, string>? Text = null)
    {
        /// <summary>Nothing chosen: demographics only, every consent box left blank.</summary>
        public static Selections None { get; } = new();
    }

    /// <summary>
    /// Throws unless <paramref name="fieldName"/> is a consent field of
    /// <paramref name="form"/>.
    ///
    /// Guards the selections path from both directions. A name that is not a consent
    /// field of this form is either a typo or an attempt to drive a demographic box
    /// through the human-choice channel, and silently ignoring it would let a case
    /// manager believe they recorded a choice the PDF never received.
    /// </summary>
    /// <exception cref="InvalidOperationException">The field is not a consent field of this form.</exception>
    public static void AssertSelectable(FormKey form, string fieldName)
    {
        if (ConsentFields(form).Contains(fieldName))
            return;

        throw new InvalidOperationException(
            $"'{fieldName}' is not a consent field on {form}, so it cannot be set as a consumer " +
            "choice. Demographic values come from the profile mapping instead.");
    }

    // Invariant so a form does not change shape with the machine's locale. A state
    // form is read by people who never saw the machine that produced it.
    private const string DateFormat = "MM/dd/yyyy";

    /// <summary>
    /// Demographic mapping for the Appointment of Authorized Representative form.
    /// Field names are the PDF's own, apostrophes and all.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> AuthorizedRepresentativeFields(Subject subject)
    {
        yield return Pair("Individual's Name", subject.FullName);
        yield return Pair("Individual's DOB", FormatDate(subject.BirthDate));
        yield return Pair("Individual's SSN", subject.SocialSecurityNumber);
        yield return Pair("Individual's Address", subject.Address);
        yield return Pair("AR Name", subject.RepresentativeName);
        yield return Pair("AR Address", subject.RepresentativeAddress);
        yield return Pair("AR Telephone Number", subject.RepresentativePhone);
        yield return Pair("AR Email Address", subject.RepresentativeEmail);
    }

    /// <summary>
    /// Demographic mapping for the Authorization to Release form.
    ///
    /// This form asks for telephone and email in a single box, so they are joined
    /// rather than given a box each. The consumer's own name and date of birth are
    /// the only identity fields; the recipient block ("Name of Individual",
    /// "Organization", and their address) describes who receives the records and is
    /// a case-manager selection, not a profile fact.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> AuthorizationToReleaseFields(Subject subject)
    {
        yield return Pair("Individuals Name", subject.FullName);
        yield return Pair("Date of Birth", FormatDate(subject.BirthDate));
        yield return Pair("Home Address TownCity State Zip Code", subject.Address);
        yield return Pair(
            "Telephone Email address of individualpersonal representative optional",
            subject.PhoneNumber);
    }

    /// <summary>
    /// The demographic values Sati will put on the form, with empty entries dropped
    /// so a missing profile value leaves the box blank for hand-completion rather
    /// than stamping an empty string over it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProfileFields(FormKey form, Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var source = form switch
        {
            FormKey.AuthorizedRepresentative => AuthorizedRepresentativeFields(subject),
            FormKey.AuthorizationToRelease => AuthorizationToReleaseFields(subject),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown DHHS form."),
        };

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in source)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            AssertFillable(form, name);
            fields[name] = value.Trim();
        }

        return fields;
    }

    /// <summary>
    /// Fields that encode a decision or an attestation rather than a fact, listed
    /// per form. Nothing in Sati may write these from stored data.
    ///
    /// The checkboxes are enumerated by name because a category test ("is it a
    /// checkbox?") would silently stop protecting the free-text boxes that qualify
    /// a checkbox — "Other (explain)", the earlier-expiry date, the initials that
    /// authorize emailed delivery. Those are consent too, and they are text.
    /// </summary>
    public static IReadOnlySet<string> ConsentFields(FormKey form) => form switch
    {
        FormKey.AuthorizedRepresentative => AuthorizedRepresentativeConsent,
        FormKey.AuthorizationToRelease => AuthorizationToReleaseConsent,
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown DHHS form."),
    };

    /// <summary>
    /// Throws if <paramref name="fieldName"/> is a consent, signature, or
    /// attestation field on <paramref name="form"/>.
    ///
    /// Called on the way in to every fill, so that a future mapping change that
    /// reaches for a checkbox fails loudly at the seam instead of quietly producing
    /// a signed-looking release.
    /// </summary>
    /// <exception cref="InvalidOperationException">The field is not Sati's to fill.</exception>
    public static void AssertFillable(FormKey form, string fieldName)
    {
        if (!ConsentFields(form).Contains(fieldName))
            return;

        throw new InvalidOperationException(
            $"'{fieldName}' on {form} records a consumer's decision or signature and is never " +
            "filled from stored data. It is left blank for the consumer to complete.");
    }

    /// <summary>
    /// Composes the representative block from the signed-in case manager and their
    /// agency.
    ///
    /// The representative on the Appointment form is always the case manager
    /// requesting it — appointing them is what the form is for — so the values come
    /// from the <c>User</c> and <c>Agency</c> records rather than from anything stored
    /// on the consumer. Nothing is copied onto <c>Person</c> or <c>PersonContact</c>;
    /// the form reads the current truth each time it is filled.
    ///
    /// Missing pieces stay missing. A partially known address is worse than a blank
    /// one on a state form, so the address is composed only from the parts present and
    /// is null when the agency has no street.
    /// </summary>
    public static Subject WithRepresentative(
        this Subject subject,
        string? caseManagerName,
        string? caseManagerPhone,
        string? caseManagerEmail,
        string? agencyStreet,
        string? agencyCity,
        string? agencyState,
        string? agencyZip)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return subject with
        {
            RepresentativeName = Blank(caseManagerName),
            RepresentativePhone = Blank(caseManagerPhone),
            RepresentativeEmail = Blank(caseManagerEmail),
            RepresentativeAddress = ComposeAddress(agencyStreet, agencyCity, agencyState, agencyZip),
        };
    }

    private static string? ComposeAddress(string? street, string? city, string? state, string? zip)
    {
        if (string.IsNullOrWhiteSpace(street))
            return null;

        var locality = string.Join(" ", new[] { city, state, zip }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        return string.IsNullOrEmpty(locality) ? street.Trim() : $"{street.Trim()}, {locality}";
    }

    /// <summary>
    /// Every demographic box on a form, whether or not a value was available. The
    /// order is the order they appear on the page, so a warning listing them reads
    /// the way the form does.
    /// </summary>
    public static IReadOnlyList<string> DemographicFieldNames(FormKey form) => form switch
    {
        FormKey.AuthorizedRepresentative =>
        [
            "Individual's Name", "Individual's DOB", "Individual's SSN", "Individual's Address",
            "AR Name", "AR Address", "AR Telephone Number", "AR Email Address",
        ],
        FormKey.AuthorizationToRelease =>
        [
            "Individuals Name", "Date of Birth", "Home Address TownCity State Zip Code",
            "Telephone Email address of individualpersonal representative optional",
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, "Unknown DHHS form."),
    };

    /// <summary>
    /// The demographic boxes this fill will leave empty, for a non-blocking warning
    /// naming what the case manager must complete by hand.
    ///
    /// A blank box is never an error. An SSN in an environment that does not store
    /// one, or a representative whose phone is not on file, still produces a correct
    /// and usable form — it just needs a pen.
    /// </summary>
    public static IReadOnlyList<string> UnfilledFields(FormKey form, Subject subject)
    {
        var filled = ProfileFields(form, subject);
        return [.. DemographicFieldNames(form).Where(name => !filled.ContainsKey(name))];
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static KeyValuePair<string, string> Pair(string name, string? value) =>
        new(name, value ?? string.Empty);

    private static string FormatDate(DateTime? value) =>
        value is DateTime date && date != default
            ? date.ToString(DateFormat, CultureInfo.InvariantCulture)
            : string.Empty;

    // The scope of a representative's authority, plus the legal instrument claimed
    // and the free text qualifying either. All of it is the consumer's to state.
    private static readonly HashSet<string> AuthorizedRepresentativeConsent = new(StringComparer.Ordinal)
    {
        "Guardianship",
        "Power of Attorney",
        "Advanced Healthcare Directive",
        "Other Legal Authority",
        "Other LA 1",
        "Sign and submit app",
        "Sign and submit review",
        "Receive copies",
        "Obtain FS benefits",
        "Represent at a Fair Hearing",
        "Act on my behalf",
        "AR Other",
        "Other AR 1",
    };

    // Which DHHS offices may disclose, which record categories travel, and the
    // signature block. The substance-use and mental-health entries carry their own
    // federal and state disclosure rules and are the reason this list is explicit.
    private static readonly HashSet<string> AuthorizationToReleaseConsent = new(StringComparer.Ordinal)
    {
        // Disclosing offices.
        "undefined",
        "undefined_2",
        "Office for Family Independence and Medical Review Team",
        "Office of Child and Family Services",
        "Maine Center for Disease Control and Prevention",
        "Office of Aging and Disability Services",
        "Dorothea Dix Psychiatric Center",
        "Division of Administrative Hearings",
        "Riverview Psychiatric Center",
        "Division of Licensing and Certification",
        "Other",
        "Other_2",
        "Other_3",
        "Other_4",

        // Direction of the disclosure and the party on the other end.
        "ReleaseSend my information to",
        "ObtainGet my information from",
        "Name of Individual",
        "Organization",
        "Address CityState Zip Code",
        "Telephone Email address optional",

        // Record categories, including 42 CFR Part 2 substance-use material and the
        // mental-health review election.
        "Include all drugalcohol information in the release",
        "Include only the specific drugalcohol records checked",
        "Diagnosis and treatment",
        "Clinical notes and discharge summaries",
        "DrugAlcohol history or summary",
        "Payment or claims information",
        "Living situation and social supports",
        "Medication dosages or supplies",
        "Lab results",
        "Financial information including billing payment",
        "Limit to the following dates or types of information",
        "Include this information in the release",
        "Include this information in the release_2",
        "I want to review my mental healthbehavioral health",
        "Other_6",
        "Other_7",
        "2024",
        "undefined_6",
        "undefined_7",

        // Delivery by email is an election with its own risk disclosure.
        "Other_5",
        "information by email INITIALHERE",
        "Please print the email address where you want your information sent",

        // Expiry, attestation, and the signature block.
        "This form expires one year from the date below unless I write an earlier date here",
        "I affirm that the written or digital signature below is mine and that I have the authority to sign this document",
        "Signature1_es_:signer:signature",
        "Printed name",
        "Date",
        "Parent",
        "Legal Guardian",
        "Conservator",
        "Other Explain",
        "Text2",
        "Page 2 of 2",
    };
}
