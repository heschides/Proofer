using Sati.Models;
using Sati.Services.LocalAi;
using System.Text;

namespace Sati.Services;

/// <summary>
/// Turns the meeting checkboxes into a structured case note and places the case
/// manager's own text underneath a Meeting Narrative header.
///
/// Deterministic and local. It invents nothing: every line comes from a control
/// the case manager ticked, phrased by <see cref="CaseNoteFactCompiler.VisitFacts"/>
/// so the template and the AI drafting path cannot describe the same selection two
/// different ways.
///
/// It never removes text. Existing narrative is preserved verbatim below the
/// header, which is why running it twice stacks two templates rather than
/// silently discarding the first one and anything the user edited into it.
/// </summary>
internal static class CaseNoteTemplateComposer
{
    internal const string NarrativeHeader = "MEETING NARRATIVE";

    // Grouped by fact id, not by the compiler's category, because the compiler
    // files "the consumer asked questions" and "the meeting took place at the
    // agency office" under one category and a case note reads them apart.
    private static readonly (string Heading, string[] Prefixes)[] Sections =
    [
        ("MEETING DETAILS",
        [
            "VISIT-CONSUMER-PRESENCE",
            "VISIT-SETTING",
            "VISIT-ATTENDEE",
            "VISIT-ADDITIONAL-ATTENDEES"
        ]),
        ("OBSERVATIONS",
        [
            "VISIT-APPEARANCE",
            "VISIT-PARTICIPATION",
            "VISIT-SAFETY",
            "VISIT-OBSERVATION-DETAIL"
        ]),
        ("DISCUSSION AND ACTIVITY",
        [
            "VISIT-PREFERENCES",
            "VISIT-QUESTIONS",
            "VISIT-CHOICES",
            "VISIT-COMMUNICATION-SUPPORT",
            "VISIT-GOALS",
            "VISIT-SERVICES",
            "VISIT-DOCUMENTS"
        ])
    ];

    /// <summary>
    /// True when at least one checkbox or detail field would produce a line.
    /// The command uses this so an empty meeting section cannot insert a
    /// template made only of headings.
    /// </summary>
    public static bool HasContent(VisitDocumentation? visit) =>
        visit is not null && CaseNoteFactCompiler.VisitFacts(visit).Count > 0;

    /// <summary>
    /// The template for the documented meeting facts, without the narrative
    /// header. Empty when nothing is documented.
    /// </summary>
    public static string Compose(VisitDocumentation? visit)
    {
        if (visit is null)
            return string.Empty;

        var facts = CaseNoteFactCompiler.VisitFacts(visit);
        if (facts.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var (heading, prefixes) in Sections)
        {
            var lines = facts
                .Where(fact => prefixes.Any(prefix =>
                    fact.Id.Equals(prefix, StringComparison.Ordinal) ||
                    fact.Id.StartsWith(prefix + "-", StringComparison.Ordinal)))
                .Select(fact => fact.Text.Trim())
                .Where(text => text.Length > 0)
                .ToList();

            if (lines.Count == 0)
                continue;

            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine(heading);
            foreach (var line in lines)
                builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The template, then the narrative header, then whatever was already in the
    /// box. <paramref name="existingNarrative"/> is preserved exactly, including
    /// its internal blank lines; only trailing whitespace is trimmed.
    /// </summary>
    public static string Merge(string template, string? existingNarrative)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(template))
        {
            builder.AppendLine(template.TrimEnd());
            builder.AppendLine();
        }

        builder.AppendLine(NarrativeHeader);

        var existing = existingNarrative?.TrimEnd();
        if (!string.IsNullOrWhiteSpace(existing))
            builder.Append(existing);

        return builder.ToString().TrimEnd();
    }
}
