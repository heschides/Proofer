using Sati.Contracts.V1;
using Sati.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sati.Services.LocalAi;

internal sealed record CaseNoteDraftSnapshot(
    int PersonId,
    string RawNarrative,
    NoteType? NoteType,
    FormType? FormType,
    string CaseManagerFullName,
    string? ConsumerFirstName,
    IReadOnlyList<CaseNoteDraftFact> Facts,
    string Fingerprint);

/// <summary>
/// Converts current note-entry state into a closed-world fact packet. It never reads historical
/// records. Every selected template control gets a stable fact id; omitted/not-documented controls
/// produce no narrative fact.
/// </summary>
internal static partial class CaseNoteFactCompiler
{
    public static CaseNoteDraftSnapshot Build(
        int personId,
        string rawNarrative,
        NoteType? noteType,
        FormType? formType,
        string caseManagerFullName,
        string? consumerFirstName,
        VisitDocumentation? visit)
    {
        if (personId <= 0)
            throw new ArgumentOutOfRangeException(nameof(personId));
        if (string.IsNullOrWhiteSpace(rawNarrative))
            throw new ArgumentException("A rough narrative is required.", nameof(rawNarrative));
        if (string.IsNullOrWhiteSpace(caseManagerFullName))
            throw new ArgumentException("A case-manager name is required.", nameof(caseManagerFullName));

        var facts = new List<CaseNoteDraftFact>();
        var fragments = SplitRawNarrative(rawNarrative);
        for (var index = 0; index < fragments.Count; index++)
        {
            var fragment = fragments[index];
            var usage = FollowUpSignalRegex().IsMatch(fragment)
                ? CaseNoteFactUsage.Narrative | CaseNoteFactUsage.FollowUp
                : CaseNoteFactUsage.Narrative;
            facts.Add(new($"RAW-{index + 1:000}", fragment, "Rough note", usage));
        }

        if (noteType == NoteType.Visit && visit is not null)
            AppendVisitFacts(facts, visit);

        facts.Add(new(
            CaseNoteDraftRules.NoFollowUpFactId,
            CaseNoteDraftRules.NoFollowUpText,
            "Safe fallback",
            CaseNoteFactUsage.FollowUp,
            Required: false));

        var fingerprintPayload = JsonSerializer.Serialize(new
        {
            PersonId = personId,
            RawNarrative = rawNarrative.Trim(),
            NoteType = noteType?.ToString(),
            FormType = formType?.ToString(),
            CaseManagerFullName = caseManagerFullName.Trim(),
            ConsumerFirstName = consumerFirstName?.Trim(),
            Facts = facts.Select(fact => new
            {
                fact.Id,
                fact.Text,
                fact.Category,
                Usage = (int)fact.Usage,
                fact.Required,
                fact.RequiredTerms
            })
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintPayload)));

        return new CaseNoteDraftSnapshot(
            personId,
            rawNarrative.Trim(),
            noteType,
            formType,
            caseManagerFullName.Trim(),
            string.IsNullOrWhiteSpace(consumerFirstName) ? null : consumerFirstName.Trim(),
            facts,
            fingerprint);
    }

    private static IReadOnlyList<string> SplitRawNarrative(string rawNarrative)
    {
        var fragments = FragmentBoundaryRegex()
            .Split(rawNarrative.Trim())
            .Select(fragment => fragment.Trim().TrimStart('-', '•', '*', ' '))
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .ToList();

        return fragments.Count == 0 ? [rawNarrative.Trim()] : fragments;
    }

    private static void AppendVisitFacts(ICollection<CaseNoteDraftFact> facts, VisitDocumentation visit)
    {
        if (visit.ConsumerPresent.HasValue)
        {
            facts.Add(new(
                "VISIT-CONSUMER-PRESENCE",
                visit.ConsumerPresent.Value ? "The consumer was present." : "The consumer was not present.",
                "Meeting selection",
                CaseNoteFactUsage.Narrative,
                RequiredTerms: visit.ConsumerPresent.Value ? ["consumer", "present"] : ["consumer", "not present"]));
        }

        var setting = visit.Setting switch
        {
            VisitSetting.ConsumerHome => "The meeting occurred in the consumer's home.",
            VisitSetting.Community => "The meeting occurred in a community setting.",
            VisitSetting.AgencyOffice => "The meeting occurred at the agency office.",
            VisitSetting.ProviderLocation => "The meeting occurred at a provider location.",
            VisitSetting.Other => "The meeting occurred in another setting described by the case manager.",
            _ => null
        };
        AddOptional(facts, "VISIT-SETTING", setting, "Meeting selection", SettingTerms(visit.Setting));
        AddOptional(facts, "VISIT-SETTING-DETAIL", visit.SettingDetails, "Meeting detail");

        var appearance = visit.Appearance switch
        {
            VisitAppearance.NeatAndAppropriatelyDressed => "The consumer was observed to be neat and appropriately dressed.",
            VisitAppearance.ConcernObserved => "An appearance concern was observed.",
            VisitAppearance.NotObserved => "The consumer's appearance was not observed.",
            _ => null
        };
        AddOptional(facts, "VISIT-APPEARANCE", appearance, "Observation selection", AppearanceTerms(visit.Appearance));

        var participation = visit.Participation switch
        {
            VisitParticipation.ParticipatedThroughout => "The consumer participated throughout the meeting.",
            VisitParticipation.ParticipatedWithSupport => "The consumer participated with support.",
            VisitParticipation.LimitedParticipation => "The consumer's participation was limited.",
            VisitParticipation.Declined => "The consumer declined to participate.",
            _ => null
        };
        AddOptional(facts, "VISIT-PARTICIPATION", participation, "Participation selection", ParticipationTerms(visit.Participation));

        var safety = visit.SafetyObservation switch
        {
            VisitSafetyObservation.NoConcernsObserved => "No health or safety concerns were observed.",
            VisitSafetyObservation.ConcernObserved => "A health or safety concern was observed.",
            _ => null
        };
        AddOptional(facts, "VISIT-SAFETY", safety, "Observation selection", SafetyTerms(visit.SafetyObservation));

        for (var index = 0; index < visit.Attendees.Count; index++)
        {
            var attendee = visit.Attendees[index];
            if (string.IsNullOrWhiteSpace(attendee.FullName))
                continue;

            var description = attendee.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(attendee.Role))
                description += $", {attendee.Role.Trim()}";
            if (!string.IsNullOrWhiteSpace(attendee.Organization))
                description += $" ({attendee.Organization.Trim()})";
            facts.Add(new(
                $"VISIT-ATTENDEE-{index + 1:000}",
                $"{description} attended the meeting.",
                "Selected attendee",
                CaseNoteFactUsage.Narrative,
                RequiredTerms: [attendee.FullName.Trim(), "attended"]));
        }

        AddOptional(facts, "VISIT-ADDITIONAL-ATTENDEES", visit.AdditionalAttendees, "Attendee detail");
        AddSelected(facts, "VISIT-PREFERENCES", visit.ExpressedPreferences, "The consumer expressed preferences.", ["consumer", "preferences"]);
        AddSelected(facts, "VISIT-QUESTIONS", visit.AskedQuestions, "The consumer asked questions.", ["consumer", "questions"]);
        AddSelected(facts, "VISIT-CHOICES", visit.MadeChoices, "The consumer made choices.", ["consumer", "choices"]);
        AddSelected(facts, "VISIT-COMMUNICATION-SUPPORT", visit.CommunicationSupportUsed, "Communication support was used.", ["communication support", "used"]);
        AddSelected(facts, "VISIT-GOALS", visit.GoalsReviewed, "Goals were reviewed.", ["goals", "reviewed"]);
        AddSelected(facts, "VISIT-SERVICES", visit.ServicesDiscussed, "Services were discussed.", ["services", "discussed"]);
        AddSelected(facts, "VISIT-DOCUMENTS", visit.DocumentsReviewed, "Documents were reviewed.", ["documents", "reviewed"]);
        AddOptional(facts, "VISIT-OBSERVATION-DETAIL", visit.ObservationDetails, "Observation detail");
    }

    private static void AddSelected(
        ICollection<CaseNoteDraftFact> facts,
        string id,
        bool selected,
        string text,
        IReadOnlyList<string> requiredTerms)
    {
        if (selected)
            facts.Add(new(id, text, "Meeting selection", CaseNoteFactUsage.Narrative, RequiredTerms: requiredTerms));
    }

    private static void AddOptional(
        ICollection<CaseNoteDraftFact> facts,
        string id,
        string? text,
        string category,
        IReadOnlyList<string>? requiredTerms = null)
    {
        if (!string.IsNullOrWhiteSpace(text))
            facts.Add(new(id, text.Trim(), category, CaseNoteFactUsage.Narrative, RequiredTerms: requiredTerms));
    }

    private static IReadOnlyList<string>? SettingTerms(VisitSetting value) => value switch
    {
        VisitSetting.ConsumerHome => ["consumer's home"],
        VisitSetting.Community => ["community setting"],
        VisitSetting.AgencyOffice => ["agency office"],
        VisitSetting.ProviderLocation => ["provider location"],
        VisitSetting.Other => ["another setting"],
        _ => null
    };

    private static IReadOnlyList<string>? AppearanceTerms(VisitAppearance value) => value switch
    {
        VisitAppearance.NeatAndAppropriatelyDressed => ["neat", "appropriately dressed"],
        VisitAppearance.ConcernObserved => ["appearance concern", "observed"],
        VisitAppearance.NotObserved => ["appearance", "not observed"],
        _ => null
    };

    private static IReadOnlyList<string>? ParticipationTerms(VisitParticipation value) => value switch
    {
        VisitParticipation.ParticipatedThroughout => ["participated throughout"],
        VisitParticipation.ParticipatedWithSupport => ["participated", "support"],
        VisitParticipation.LimitedParticipation => ["participation", "limited"],
        VisitParticipation.Declined => ["declined", "participate"],
        _ => null
    };

    private static IReadOnlyList<string>? SafetyTerms(VisitSafetyObservation value) => value switch
    {
        VisitSafetyObservation.NoConcernsObserved => ["no health or safety concerns", "observed"],
        VisitSafetyObservation.ConcernObserved => ["health or safety concern", "observed"],
        _ => null
    };

    [GeneratedRegex(@"(?:\r?\n)+|(?<=[.!?])\s+|;\s*")]
    private static partial Regex FragmentBoundaryRegex();

    [GeneratedRegex(@"\b(?:follow[ -]?up|f/u|next step|next steps|plan to|will|confirm|send|check back|prepare for)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FollowUpSignalRegex();
}
