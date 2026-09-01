using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Sati.ViewModels.ClientDocuments;
using System.Text.Json;

namespace Sati.Services;

public sealed class DailyAgendaCoordinator(
    DailyAgendaPreferenceService preferences,
    ISettingsService settingsService,
    DailyAgendaBuilder builder,
    IComprehensiveAssessmentService assessments,
    DataEnvironmentInfo environment)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<DailyAgendaViewModel?> TryCreateAsync(
        User user,
        IReadOnlyList<Person> people,
        ScratchpadViewModel scratchpad,
        DateOnly localDate)
    {
        if (!user.HasCaseManagerPermissions)
            return null;

        var preference = await preferences.LoadForUserAsync(user.Id);
        if (!preference.ShowAtSignIn || preference.LastShownDate == localDate)
            return null;

        try
        {
            var settings = await settingsService.LoadAsync();
            var agenda = builder.Build(
                people,
                settings,
                localDate.ToDateTime(TimeOnly.MinValue));
            var assessmentProgress = string.Empty;

            if (agenda.AssessmentSuggestion is { } suggestion)
            {
                try
                {
                    var assessment = await assessments.GetLatestForAgendaAsync(suggestion.PersonId);
                    assessmentProgress = assessment is null
                        ? "Not started."
                        : DescribeAssessmentProgress(assessment);
                }
                catch (Exception ex)
                {
                    AppErrorLog.Record(ex, "daily-agenda.assessment-progress");
                    agenda = agenda with { AssessmentSuggestion = null };
                }
            }

            try
            {
                await preferences.MarkShownAsync(user.Id, localDate);
            }
            catch (DailyAgendaPreferenceSaveException ex)
            {
                AppErrorLog.Record(ex, "daily-agenda.mark-shown");
            }

            return new DailyAgendaViewModel(
                agenda,
                scratchpad,
                user.DisplayName,
                environment.DisplayName,
                environment.IsDemo,
                assessmentProgress,
                localDate);
        }
        catch (Exception ex)
        {
            AppErrorLog.Record(ex, "daily-agenda.build");
            return null;
        }
    }

    private static string DescribeAssessmentProgress(ComprehensiveAssessment assessment)
    {
        try
        {
            var document = JsonSerializer.Deserialize<AssessmentDocument>(
                    assessment.DocumentJson,
                    JsonOptions)
                ?? new AssessmentDocument();
            var progress = ComprehensiveAssessmentViewModel.CalculateProgress(document);
            return $"{progress.Text} · {DisplayStatus(assessment.Status)}.";
        }
        catch (JsonException)
        {
            return $"Progress unavailable · {DisplayStatus(assessment.Status)}.";
        }
    }

    private static string DisplayStatus(AssessmentStatus status) => status switch
    {
        AssessmentStatus.ReadyForReview => "Ready for review",
        _ => status.ToString()
    };
}
