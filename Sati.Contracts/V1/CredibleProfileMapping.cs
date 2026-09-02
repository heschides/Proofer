using System.Globalization;
using System.Text.RegularExpressions;

namespace Sati.Contracts.V1;

/// <summary>
/// Turns a parsed Credible export into a draft of Sati field values.
///
/// <para>
/// The single owner of what a Credible label means. Pure and free of I/O: it takes a
/// <see cref="ClientExportDocument"/> and a <see cref="CredibleLayoutProfile"/> and returns a
/// draft, so it can be tested from a literal with no HTML involved, and so a future server-side
/// path cannot map a field differently from the desktop.
/// </para>
///
/// <para>
/// <b>It never guesses.</b> A label the profile expects and the export does not carry produces a
/// reported absence, not a value from a neighbouring cell; a value it cannot convert is reported
/// unreadable rather than coerced. Missing data is a nuisance a reviewer can fix. A plausible
/// wrong value in a clinical or billing field survives review precisely because it looks right,
/// which is the failure this whole import design exists to avoid — see the print-to-PDF evidence
/// in CREDIBLE_IMPORT_DESIGN.md, where an SSN landed silently in the MaineCare ID field.
/// </para>
/// </summary>
public static class CredibleProfileMapping
{
    // Credible pads its cells with non-breaking spaces, which char.IsWhiteSpace does treat as
    // whitespace but a naive Trim(' ') would not. Named so the intent survives a refactor.
    private static readonly char[] Padding = [' ', '\t', '\r', '\n', ' ', '​'];

    // "(F84.0) Autistic disorder" -> F84.0. Anchored at the start: a code in parentheses
    // anywhere else in the prose is part of the description, not the diagnosis.
    private static readonly Regex ParenthesizedCode =
        new(@"^\(\s*(?<code>[^)]+?)\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // An ICD-10-CM code standing on its own, for layouts that print the code without prose.
    private static readonly Regex BareCode =
        new(@"^[A-Za-z][0-9A-Za-z]{1,6}(\.[0-9A-Za-z]{1,4})?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Credible prints M/D/YYYY and MM/DD/YYYY. Listed explicitly rather than letting
    // DateTime.Parse guess: a machine set to a day-first culture would read 04/03/1990 as
    // 3 April and be wrong about a birth date without anything looking amiss.
    private static readonly string[] DateFormats =
        ["MM/dd/yyyy", "M/d/yyyy", "MM/d/yyyy", "M/dd/yyyy"];

    public static CredibleProfileDraft Map(
        ClientExportDocument document,
        CredibleLayoutProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);

        var sections = BuildSectionIndex(document);
        var fields = new List<CredibleFieldDraft>(profile.Fields.Count);
        var missingSections = new List<string>();

        foreach (var mapping in profile.Fields)
        {
            if (!sections.TryGetValue(Key(mapping.Section), out var section))
            {
                if (!missingSections.Contains(mapping.Section, StringComparer.OrdinalIgnoreCase))
                    missingSections.Add(mapping.Section);

                fields.Add(Absent(mapping, CredibleFieldStatus.SectionMissing));
                continue;
            }

            if (!section.TryGetValue(Key(mapping.Label), out var raw))
            {
                fields.Add(Absent(mapping, CredibleFieldStatus.LabelMissing));
                continue;
            }

            var trimmed = Clean(raw);
            if (string.IsNullOrEmpty(trimmed))
            {
                fields.Add(new CredibleFieldDraft(
                    mapping.SatiField, mapping.Section, mapping.Label,
                    raw, null, CredibleFieldStatus.Blank));
                continue;
            }

            var converted = Convert(trimmed, mapping.Kind);
            fields.Add(new CredibleFieldDraft(
                mapping.SatiField,
                mapping.Section,
                mapping.Label,
                trimmed,
                converted,
                converted is null ? CredibleFieldStatus.Unreadable : CredibleFieldStatus.Mapped));
        }

        return new CredibleProfileDraft(
            Clean(document.CredibleClientId) is { Length: > 0 } id ? id : null,
            fields,
            missingSections,
            UnmappedLabels(document, profile));
    }

    /// <summary>
    /// Converts one raw cell. Returns null when the value cannot be read, which the caller
    /// records as <see cref="CredibleFieldStatus.Unreadable"/>.
    /// </summary>
    public static string? Convert(string? rawValue, CredibleValueKind kind)
    {
        var value = Clean(rawValue);
        if (string.IsNullOrEmpty(value))
            return null;

        return kind switch
        {
            CredibleValueKind.Text => value,
            CredibleValueKind.Date => ParseDate(value),
            CredibleValueKind.Gender => ParseGender(value),
            CredibleValueKind.DiagnosisCode => ParseDiagnosisCode(value),
            CredibleValueKind.YesNo => ParseYesNo(value) is bool yes
                ? yes ? "true" : "false"
                : null,
            // The negation, and the reason this is a declared kind rather than a copy:
            // "Consumer is Own Guardian? = YES" means the consumer has NO guardian.
            CredibleValueKind.InvertedYesNo => ParseYesNo(value) is bool own
                ? own ? "false" : "true"
                : null,
            _ => null
        };
    }

    /// <summary>Credible's <c>MM/DD/YYYY</c> to ISO. Null when it is not a date this understands.</summary>
    public static string? ParseDate(string? rawValue)
    {
        var value = Clean(rawValue);
        if (string.IsNullOrEmpty(value))
            return null;

        return DateTime.TryParseExact(
            value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Credible's gender onto Sati's vocabulary.
    ///
    /// <para>
    /// An unrecognized value returns null rather than falling back to <c>Unknown</c>. The two
    /// are different: <c>Unknown</c> asserts that the consumer's gender is not recorded, and
    /// silently asserting that on every value Sati has not been taught would launder a mapping
    /// gap into a clinical fact.
    /// </para>
    /// </summary>
    public static string? ParseGender(string? rawValue) => Clean(rawValue).ToUpperInvariant() switch
    {
        "" => null,
        "M" or "MALE" => "Male",
        "F" or "FEMALE" => "Female",
        "NB" or "NON-BINARY" or "NONBINARY" or "NON BINARY" => "NonBinary",
        "U" or "UNKNOWN" or "UNSPECIFIED" or "DECLINED" => "Unknown",
        _ => null
    };

    /// <summary>
    /// The code out of <c>(F84.0) Autistic disorder</c>.
    ///
    /// <para>
    /// Accepts a bare code too, for layouts that print one without prose. Anything else returns
    /// null: <c>Person.DiagnosisCode</c> reaches an 837P claim, and storing a sentence there
    /// produces a rejected claim rather than an obvious error.
    /// </para>
    /// </summary>
    public static string? ParseDiagnosisCode(string? rawValue)
    {
        var value = Clean(rawValue);
        if (string.IsNullOrEmpty(value))
            return null;

        var match = ParenthesizedCode.Match(value);
        if (match.Success)
        {
            var code = Clean(match.Groups["code"].Value);
            return BareCode.IsMatch(code) ? code : null;
        }

        return BareCode.IsMatch(value) ? value : null;
    }

    /// <summary>YES/NO as Credible writes it. Null when it is neither.</summary>
    public static bool? ParseYesNo(string? rawValue) => Clean(rawValue).ToUpperInvariant() switch
    {
        "Y" or "YES" or "TRUE" or "1" => true,
        "N" or "NO" or "FALSE" or "0" => false,
        _ => null
    };

    /// <summary>Trims Credible's padding, including the non-breaking spaces it pads cells with.</summary>
    public static string Clean(string? value) => value?.Trim(Padding) ?? string.Empty;

    // ---- internals ----

    private static CredibleFieldDraft Absent(CredibleFieldMapping mapping, CredibleFieldStatus status) =>
        new(mapping.SatiField, mapping.Section, mapping.Label, null, null, status);

    private static string Key(string value) => Clean(value).ToUpperInvariant();

    /// <summary>
    /// Sections and their labels, indexed for lookup.
    ///
    /// <para>
    /// A repeated section keeps its first occurrence, and so does a repeated label. Credible
    /// repeats <c>CONSUMER EPISODE INFO</c> once per episode — 31 times in the test export — and
    /// none of those are mapped in v1, but a profile that did map a repeated section would
    /// otherwise take whichever copy happened to come last.
    /// </para>
    /// </summary>
    private static Dictionary<string, Dictionary<string, string?>> BuildSectionIndex(
        ClientExportDocument document)
    {
        var sections = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);

        foreach (var section in document.Sections)
        {
            var sectionKey = Key(section.Banner);
            if (!sections.TryGetValue(sectionKey, out var fields))
            {
                fields = new Dictionary<string, string?>(StringComparer.Ordinal);
                sections[sectionKey] = fields;
            }

            foreach (var field in section.Fields)
            {
                var labelKey = Key(field.Label);
                if (labelKey.Length > 0 && !fields.ContainsKey(labelKey))
                    fields[labelKey] = field.Value;
            }
        }

        return sections;
    }

    private static List<string> UnmappedLabels(
        ClientExportDocument document,
        CredibleLayoutProfile profile)
    {
        var mapped = profile.Fields
            .Select(mapping => $"{Key(mapping.Section)}{Key(mapping.Label)}")
            .ToHashSet(StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unmapped = new List<string>();

        foreach (var section in document.Sections)
        {
            foreach (var field in section.Fields)
            {
                var label = Clean(field.Label);
                if (label.Length == 0)
                    continue;

                var key = $"{Key(section.Banner)}{Key(label)}";
                if (mapped.Contains(key) || !seen.Add(key))
                    continue;

                unmapped.Add($"{Clean(section.Banner)} / {label}");
            }
        }

        return unmapped;
    }
}
