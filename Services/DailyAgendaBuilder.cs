using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.Services;

public enum DailyAgendaItemKind
{
    OverdueForm,
    UpcomingWork,
    SuggestedAssessment
}

public sealed record DailyAgendaItem(
    string Key,
    int PersonId,
    string PersonName,
    string Title,
    DateTime DueDate,
    DailyAgendaItemKind Kind,
    bool BlocksBilling,
    FormType? FormType = null)
{
    public bool IsOverdue => Kind == DailyAgendaItemKind.OverdueForm;
    public string DueText => $"due {DueDate:MMMM d, yyyy}";
    public string ScratchpadLine => DailyAgendaText.FormatItem(this);
}

public sealed record DailyAgendaBuildResult(
    int PersonCount,
    int LookaheadDays,
    int OverdueTotal,
    IReadOnlyList<DailyAgendaItem> OverdueItems,
    IReadOnlyList<DailyAgendaItem> UpcomingItems,
    DailyAgendaItem? AssessmentSuggestion)
{
    public bool HasOverdue => OverdueTotal > 0;
    public bool HasUpcoming => UpcomingItems.Count > 0;
}

public static class DailyAgendaText
{
    public static string FormatItem(DailyAgendaItem item) => item.Kind switch
    {
        DailyAgendaItemKind.OverdueForm =>
            $"Overdue: {item.Title} for {item.PersonName} — {item.DueText}",
        DailyAgendaItemKind.SuggestedAssessment =>
            $"Comprehensive Assessment for {item.PersonName} — {item.DueText}",
        _ => $"{item.Title} — {item.DueText}"
    };
}

/// <summary>
/// Builds the read-only agenda snapshot from the caseload already loaded during
/// shell initialization. It never changes form or assessment state.
/// </summary>
public sealed class DailyAgendaBuilder(IUpcomingEventService upcomingEvents)
{
    public const int SectionLimit = 5;

    public DailyAgendaBuildResult Build(
        IEnumerable<Person> people,
        Settings settings,
        DateTime asOfDate)
    {
        var today = asOfDate.Date;
        var caseload = people.ToList();

        var allOverdue = caseload
            .SelectMany(person => person.Forms
                .Where(form => BillingComplianceGate.IsIncompleteAndOverdue(
                    form.DueDate,
                    form.CompletedDate,
                    today))
                .Select(form => new DailyAgendaItem(
                    $"form:{person.Id}:{form.Id}:{form.Type}",
                    person.Id,
                    person.FullName,
                    BillingComplianceGate.DisplayName(form.Type.ToString()),
                    form.DueDate.Date,
                    DailyAgendaItemKind.OverdueForm,
                    BillingComplianceGate.IsRequired(
                        form.Type.ToString(),
                        settings.BillingComplianceRequirements),
                    form.Type)))
            .OrderBy(item => item.DueDate)
            .ThenBy(item => item.PersonName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // LateReview events describe the same forms as the unbounded lookback.
        // The lookback owns them so one form cannot appear in two sections.
        var upcoming = upcomingEvents
            .GenerateEvents(caseload, settings, today)
            .Where(item => item.Kind != UpcomingEventKind.LateReview)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(SectionLimit)
            .Select(item => new DailyAgendaItem(
                $"event:{item.PersonId}:{item.Kind}:{item.Date:yyyyMMdd}:{item.Title}",
                item.PersonId,
                item.ClientName,
                item.Title,
                item.Date.Date,
                DailyAgendaItemKind.UpcomingWork,
                false))
            .ToList();

        var assessmentSuggestion = allOverdue.Count == 0 && upcoming.Count == 0
            ? caseload
                .SelectMany(person => person.Forms
                    .Where(form => form.Type == FormType.ComprehensiveAssessment &&
                                   form.CompletedDate is null)
                    .Select(form => new DailyAgendaItem(
                        $"assessment:{person.Id}:{form.Id}",
                        person.Id,
                        person.FullName,
                        "Comprehensive Assessment",
                        form.DueDate.Date,
                        DailyAgendaItemKind.SuggestedAssessment,
                        false,
                        FormType.ComprehensiveAssessment)))
                .OrderBy(item => item.DueDate)
                .ThenBy(item => item.PersonName, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault()
            : null;

        return new DailyAgendaBuildResult(
            caseload.Count,
            GuaranteedLookaheadDays(settings),
            allOverdue.Count,
            allOverdue.Take(SectionLimit).ToList(),
            upcoming,
            assessmentSuggestion);
    }

    private static int GuaranteedLookaheadDays(Settings settings) =>
        new[]
        {
            settings.PcpOpenDaysBefore,
            settings.CompAssessmentOpenDaysBefore,
            settings.ReclassificationOpenDaysBefore,
            settings.SafetyPlanOpenDaysBefore,
            settings.PrivacyPracticesOpenDaysBefore,
            settings.ReleaseAgencyOpenDaysBefore,
            settings.ReleaseDhhsOpenDaysBefore,
            settings.ReleaseMedicalOpenDaysBefore,
            settings.ReviewOpenDaysBefore,
            30 // Scheduled-note lookahead in UpcomingEventService.
        }.Min();
}
