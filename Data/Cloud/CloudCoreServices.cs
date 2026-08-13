using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data.Cloud;

public sealed class CloudPersonService(CloudApiClient api) : IPersonService
{
    public async Task<Person> AddPersonAsync(Person person) =>
        CloudContractMapper.ToPerson(await api.PostAsync<SavePersonRequest, PersonDto>(
            "/api/v1/people", CloudContractMapper.ToSavePersonRequest(person)));

    public async Task<Person> EditPersonAsync(Person person) =>
        CloudContractMapper.ToPerson(await api.PutAsync<SavePersonRequest, PersonDto>(
            $"/api/v1/people/{person.Id}", CloudContractMapper.ToSavePersonRequest(person)));

    public async Task<List<Person>> GetAllPeopleAsync(int userId) =>
        (await api.GetAsync<List<PersonDto>>($"/api/v1/caseload?userId={userId}")).Select(CloudContractMapper.ToPerson).ToList();

    public async Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
        (await api.GetAsync<List<PersonDto>>($"/api/v1/caseload?userId={userId}")).Select(CloudContractMapper.ToPersonSummary).ToList();

    public Task<string?> GetJournalAsync(int personId) =>
        api.GetAsync<string?>($"/api/v1/people/{personId}/journal");

    public Task SaveJournalAsync(int personId, string? journal) =>
        api.PutAsync($"/api/v1/people/{personId}/journal", new SaveJournalRequest(journal));
}

public sealed class CloudNoteService(CloudApiClient api) : INoteService
{
    public async Task<Note> AddNoteAsync(Note note) =>
        CloudContractMapper.ToNote(await api.PostAsync<SaveNoteRequest, NoteDto>(
            "/api/v1/notes", CloudContractMapper.ToSaveNoteRequest(note)));

    public async Task DeleteNoteAsync(Note note)
    {
        try
        {
            await api.DeleteAsync($"/api/v1/notes/{note.Id}?expectedRevision={note.Revision}");
        }
        catch (CloudApiException ex) when (ex.Code == "stale_note")
        {
            throw new NoteConcurrencyException(ex);
        }
    }

    public async Task UpdateNoteAsync(Note note)
    {
        try
        {
            var updated = await api.PutAsync<SaveNoteRequest, NoteDto>(
                $"/api/v1/notes/{note.Id}", CloudContractMapper.ToSaveNoteRequest(note));
            note.Revision = updated.Revision;
        }
        catch (CloudApiException ex) when (ex.Code == "stale_note")
        {
            throw new NoteConcurrencyException(ex);
        }
    }

    public async Task<List<Note>> GetAllByPersonAsync(int personId) =>
        (await api.GetAsync<List<NoteDto>>($"/api/v1/people/{personId}/notes")).Select(CloudContractMapper.ToNote).ToList();

    public async Task UpdateAbandonedNotesAsync(int abandonedAfterDays) =>
        _ = await api.PostAsync<object, CountDto>("/api/v1/notes/abandon-overdue", new { });

    public async Task<List<Note>> GetMonthlyNotesAsync(int userId) =>
        (await api.GetAsync<List<NoteDto>>($"/api/v1/notes/monthly?userId={userId}")).Select(CloudContractMapper.ToNote).ToList();

    public async Task<List<Note>> GetByYearAsync(int userId, int year) =>
        (await api.GetAsync<List<NoteDto>>($"/api/v1/notes/year/{year}")).Select(CloudContractMapper.ToNote).ToList();
}

public sealed class CloudSettingsService(CloudApiClient api) : ISettingsService
{
    public async Task<Settings> LoadAsync() =>
        CloudContractMapper.ToSettings(await api.GetAsync<SettingsDto>("/api/v1/settings"));

    public async Task SaveAsync(Settings settings)
    {
        try
        {
            var saved = await api.PutAsync<SettingsDto, SettingsDto>(
                "/api/v1/settings", CloudContractMapper.ToSettingsDto(settings));
            settings.Revision = saved.Revision;
        }
        catch (CloudApiException ex) when (ex.Code == "stale_settings")
        {
            throw new SettingsConcurrencyException(ex);
        }
    }
}

public sealed class CloudScratchpadService(CloudApiClient api) : IScratchpadService
{
    public async Task<Scratchpad> LoadTodayAsync(int userId) =>
        CloudContractMapper.ToScratchpad(await api.GetAsync<ScratchpadDto>("/api/v1/scratchpad/today"));

    public async Task<List<Scratchpad>> GetHistoryAsync(int userId) =>
        (await api.GetAsync<List<ScratchpadDto>>("/api/v1/scratchpad/history")).Select(CloudContractMapper.ToScratchpad).ToList();

    public async Task<ScratchpadComment> AddCommentAsync(int scratchpadId, int userId, string authorDisplayName, string content) =>
        CloudContractMapper.ToScratchpadComment(await api.PostAsync<AddScratchpadCommentRequest, ScratchpadCommentDto>(
            $"/api/v1/scratchpad/{scratchpadId}/comments", new AddScratchpadCommentRequest(content)));

    public Task SaveAsync(Scratchpad scratchpad) =>
        api.PutAsync("/api/v1/scratchpad", new SaveScratchpadRequest(scratchpad.Id, scratchpad.Content));
}

public sealed class CloudExemptDateService(CloudApiClient api) : IExemptDateService
{
    public async Task<List<ExemptDate>> GetByYearAsync(int userId, int year) =>
        (await api.GetAsync<List<ExemptDateDto>>($"/api/v1/exempt-dates/{year}"))
        .Select(x => CloudContractMapper.ToExemptDate(x, userId)).ToList();

    public async Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) =>
        CloudContractMapper.ToExemptDate(await api.PostAsync<AddExemptDateRequest, ExemptDateDto>(
            "/api/v1/exempt-dates", new AddExemptDateRequest(date, reason)), userId);

    public Task RemoveAsync(int id) => api.DeleteAsync($"/api/v1/exempt-dates/{id}");
}

public sealed class CloudIncentiveService(CloudApiClient api) : IIncentiveService
{
    public async Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year)
    {
        var response = await api.GetAsync<IncentiveEnvelopeDto>($"/api/v1/incentives/{year}/{month}?userId={userId}");
        return (CloudContractMapper.ToIncentive(response.Incentive), response.WasCreated);
    }

    public async Task SaveAsync(Incentive incentive) =>
        _ = await api.PutAsync<IncentiveDto, IncentiveDto>($"/api/v1/incentives/{incentive.Id}", new IncentiveDto(
            incentive.Id, incentive.UserId, incentive.Month, incentive.Year, incentive.DaysScheduled,
            incentive.BaseIncentive, incentive.PerUnitIncentive, incentive.UnitsPerDay, incentive.ExcludedDatesJson));

    public async Task<int> GetRemainingEligibleDaysAsync(int month, int year, HashSet<DateTime> daysAlreadyWorked, HashSet<DateTime> exemptDates) =>
        (await api.PostAsync<RemainingEligibleDaysRequest, CountDto>("/api/v1/incentives/remaining-days",
            new RemainingEligibleDaysRequest(month, year, daysAlreadyWorked.ToList(), exemptDates.ToList()))).Count;

    public async Task<int> GetEligibleDaysAsync(DateTime startInclusive, DateTime endInclusive) =>
        (await api.PostAsync<DateWindowRequest, CountDto>("/api/v1/incentives/eligible-days",
            new DateWindowRequest(startInclusive, endInclusive))).Count;

    public async Task<List<Incentive>> GetHistoryAsync(int userId) =>
        (await api.GetAsync<List<IncentiveDto>>("/api/v1/incentives/history")).Select(CloudContractMapper.ToIncentive).ToList();
}

public sealed class CloudFormService(CloudApiClient api) : IFormService
{
    public Task UpdateFormAsync(Form form) => SaveAsync(form);
    public Task OpenFormAsync(Form form) => SaveAsync(form);
    public async Task DeleteFormsAsync(IEnumerable<Form> forms) =>
        _ = await api.PostAsync<DeleteFormsRequest, CountDto>(
            "/api/v1/forms/delete",
            new DeleteFormsRequest(forms.Select(form => form.Id).Where(id => id > 0).Distinct().ToList()));

    private async Task SaveAsync(Form form)
    {
        var response = await api.PutAsync<UpdateFormRequest, FormDto>(
            $"/api/v1/forms/{form.Id}", new UpdateFormRequest(form.CompletedDate, form.OpenedDate));
        form.OpenedDate = response.OpenedDate;
        if (response.CompletedDate is DateTime completed)
            form.MarkComplete(completed);
        else
            form.Reset();
    }
}

internal static class CloudFeatureUnavailable
{
    public static Task For(string feature) => Task.FromException(Create(feature));
    public static Task<T> For<T>(string feature) => Task.FromException<T>(Create(feature));

    private static NotSupportedException Create(string feature) => new(
        $"{feature} is not available in the Azure Demo yet. No local or direct database fallback was attempted.");
}
