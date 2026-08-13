using System.Security;
using System.Net;
using System.IO;
using System.Text.Json;
using Sati.Contracts.V1;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.Models.Billing;
using Sati.Services.LocalAi;

namespace Sati.Data.Cloud;

public sealed class CloudUserService(CloudApiClient api) : IUserService
{
    public async Task<User> CreateAsync(User user, SecureString initialPassword)
    {
        var plainText = ToPlainText(initialPassword);
        try
        {
            var request = new CreateUserRequest(
                user.Username, user.DisplayName, user.Role.ToString(), user.SupervisorId,
                user.AgencyId, user.Email, user.Phone, plainText);
            return CloudContractMapper.ToUser(
                await api.PostAsync<CreateUserRequest, UserProfileDto>("/api/v1/users", request));
        }
        finally
        {
            plainText = string.Empty;
        }
    }
    public async Task<List<User>> GetAllAsync() =>
        (await api.GetAsync<List<UserProfileDto>>("/api/v1/users/switchable"))
        .Select(CloudContractMapper.ToUser)
        .ToList();
    public Task UpdateAsync(User user) => api.PutAsync($"/api/v1/users/{user.Id}", ToRequest(user));
    public async Task ResetPasswordAsync(User user, SecureString newPassword)
    {
        var plainText = ToPlainText(newPassword);
        try
        {
            await api.PutAsync($"/api/v1/users/{user.Id}/password", new ResetPasswordRequest(plainText));
        }
        finally
        {
            plainText = string.Empty;
        }
    }
    public async Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword)
    {
        var currentPlainText = ToPlainText(currentPassword);
        var newPlainText = ToPlainText(newPassword);
        try
        {
            await api.PutAsync("/api/v1/users/me/password",
                new ChangePasswordRequest(currentPlainText, newPlainText));
        }
        finally
        {
            currentPlainText = string.Empty;
            newPlainText = string.Empty;
        }
    }
    public async Task<List<User>> GetSuperviseesAsync(int supervisorId) =>
        (await api.GetAsync<List<UserProfileDto>>("/api/v1/supervisor/supervisees"))
        .Select(CloudContractMapper.ToUser)
        .ToList();

    private static SaveUserRequest ToRequest(User user) => new(
        user.Username, user.DisplayName, user.Role.ToString(), user.SupervisorId,
        user.AgencyId, user.Email, user.Phone);

    private static string ToPlainText(SecureString password) =>
        new NetworkCredential(string.Empty, password).Password;
}

public sealed class CloudPersonContactService(CloudApiClient api) : IPersonContactService
{
    public async Task<List<PersonContact>> GetActiveByPersonAsync(int personId) =>
        (await api.GetAsync<List<PersonContactDto>>($"/api/v1/people/{personId}/contacts"))
        .Select(CloudContractMapper.ToPersonContact)
        .ToList();

    public async Task<PersonContact> SaveAsync(PersonContact contact)
    {
        var request = CloudContractMapper.ToSavePersonContactRequest(contact);
        var response = contact.Id == 0
            ? await api.PostAsync<SavePersonContactRequest, PersonContactDto>(
                $"/api/v1/people/{contact.PersonId}/contacts", request)
            : await api.PutAsync<SavePersonContactRequest, PersonContactDto>(
                $"/api/v1/people/{contact.PersonId}/contacts/{contact.Id}", request);
        return CloudContractMapper.ToPersonContact(response);
    }

    public Task ArchiveAsync(int contactId) => api.DeleteAsync($"/api/v1/contacts/{contactId}");
}

public sealed class CloudSupervisorService(CloudApiClient api) : ISupervisorService
{
    public async Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false) =>
        (await api.GetAsync<List<NoteDto>>($"/api/v1/supervisor/notes?compliant=true&allSupervisees={allSupervisees.ToString().ToLowerInvariant()}"))
        .Select(CloudContractMapper.ToNote)
        .ToList();

    public async Task<IEnumerable<Note>> GetNonCompliantNotesAsync(int supervisorId, bool allSupervisees = false) =>
        (await api.GetAsync<List<NoteDto>>($"/api/v1/supervisor/notes?compliant=false&allSupervisees={allSupervisees.ToString().ToLowerInvariant()}"))
        .Select(CloudContractMapper.ToNote)
        .ToList();

    public async Task ApproveNoteAsync(int noteId, int supervisorId, int expectedRevision) =>
        _ = await SendNoteActionAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve",
            new SupervisorNoteActionRequest(null, expectedRevision));

    public async Task ApproveWithOverrideAsync(int noteId, int supervisorId, string overrideReason, int expectedRevision) =>
        _ = await SendNoteActionAsync(
            $"/api/v1/supervisor/notes/{noteId}/approve-override",
            new SupervisorNoteActionRequest(overrideReason, expectedRevision));

    public async Task ReturnNoteAsync(int noteId, int supervisorId, string reason, int expectedRevision) =>
        _ = await SendNoteActionAsync(
            $"/api/v1/supervisor/notes/{noteId}/return",
            new SupervisorNoteActionRequest(reason, expectedRevision));

    private async Task<NoteDto> SendNoteActionAsync(string path, SupervisorNoteActionRequest request)
    {
        try
        {
            return await api.PostAsync<SupervisorNoteActionRequest, NoteDto>(path, request);
        }
        catch (CloudApiException ex) when (ex.Code == "stale_note")
        {
            throw new NoteConcurrencyException(ex);
        }
    }
}

public sealed class CloudReviewItemService(CloudApiClient api) : IReviewItemService
{
    public async Task<List<ReviewItem>> GetForCaseloadAsync(int userId) =>
        (await api.GetAsync<List<ReviewItemDto>>($"/api/v1/reviews?userId={userId}"))
        .Select(CloudContractMapper.ToReviewItem).ToList();

    public async Task<List<ReviewItem>> GetForPersonAsync(int personId) =>
        (await api.GetAsync<List<ReviewItemDto>>($"/api/v1/people/{personId}/reviews"))
        .Select(CloudContractMapper.ToReviewItem).ToList();

    public async Task<int> EnsureCurrentCycleItemsAsync(IEnumerable<Person> people, DateTime today) =>
        (await api.PostAsync<EnsureReviewItemsRequest, CountDto>("/api/v1/reviews/ensure-current",
            new EnsureReviewItemsRequest(people.Select(x => x.Id).Distinct().ToList(), today))).Count;

    public async Task<ReviewItem> SetStageDateAsync(int reviewItemId, ReviewStage stage, DateTime? date) =>
        CloudContractMapper.ToReviewItem(await api.PutAsync<SetReviewStageRequest, ReviewItemDto>(
            $"/api/v1/reviews/{reviewItemId}/stage", new SetReviewStageRequest(stage.ToString(), date)));

    public async Task<ReviewItem> SetAppointmentAsync(int reviewItemId, DateTime? date, string? providerName) =>
        CloudContractMapper.ToReviewItem(await api.PutAsync<SetAppointmentRequest, ReviewItemDto>(
            $"/api/v1/reviews/{reviewItemId}/appointment", new SetAppointmentRequest(date, providerName)));

    public async Task<(Appointment? Medical, Appointment? Dental)> GetLatestAppointmentsAsync(int personId)
    {
        var result = await api.GetAsync<LatestAppointmentsDto>($"/api/v1/people/{personId}/appointments/latest");
        return (result.Medical is null ? null : CloudContractMapper.ToAppointment(result.Medical),
            result.Dental is null ? null : CloudContractMapper.ToAppointment(result.Dental));
    }
}

public sealed class CloudAtRequestService(CloudApiClient api) : IATRequestService
{
    public async Task<List<ATRequestListItem>> GetAllForUserAsync(int userId) =>
        (await api.GetAsync<List<AtRequestListItemDto>>($"/api/v1/at-requests?userId={userId}"))
        .Select(x => new ATRequestListItem { Id = x.Id, ClientName = x.ClientName,
            Status = Enum.Parse<ATRequestStatus>(x.Status), TotalCost = x.TotalCost,
            SubmittedDate = x.SubmittedDate, VendorName = x.VendorName,
            CaseManagerName = x.CaseManagerName, HasSnapshot = x.HasSnapshot }).ToList();
    public async Task<ATRequest?> GetByIdAsync(int id) =>
        CloudContractMapper.ToAtRequest(await api.GetAsync<AtRequestDto>($"/api/v1/at-requests/{id}"));
    public async Task<byte[]?> GetSnapshotAsync(int id)
    {
        var payload = await api.GetAsync<BinaryPayloadDto>($"/api/v1/at-requests/{id}/snapshot");
        return string.IsNullOrEmpty(payload.Base64) ? null : Convert.FromBase64String(payload.Base64);
    }
    public async Task<ATRequest> AddAsync(ATRequest request) => CloudContractMapper.ToAtRequest(
        await api.PostAsync<SaveAtRequestRequest, AtRequestDto>("/api/v1/at-requests", CloudContractMapper.ToSaveAtRequestRequest(request)));
    public async Task<ATRequest> UpdateAsync(ATRequest request)
    {
        try
        {
            return CloudContractMapper.ToAtRequest(
                await api.PutAsync<SaveAtRequestRequest, AtRequestDto>(
                    $"/api/v1/at-requests/{request.Id}", CloudContractMapper.ToSaveAtRequestRequest(request)));
        }
        catch (CloudApiException ex) when (ex.Code == "stale_at_request")
        {
            throw new AtRequestConcurrencyException(ex);
        }
    }

    public async Task DeleteAsync(ATRequest request)
    {
        try
        {
            await api.DeleteAsync($"/api/v1/at-requests/{request.Id}?expectedRevision={request.Revision}");
        }
        catch (CloudApiException ex) when (ex.Code == "stale_at_request")
        {
            throw new AtRequestConcurrencyException(ex);
        }
    }
}

public sealed class CloudProviderService(CloudApiClient api) : IProviderService
{
    public async Task<List<Provider>> GetAllAsync() =>
        (await api.GetAsync<List<ProviderDto>>("/api/v1/providers")).Select(CloudContractMapper.ToProvider).ToList();
    public async Task<List<Provider>> GetPassthroughProvidersAsync() =>
        (await api.GetAsync<List<ProviderDto>>("/api/v1/providers?passthroughOnly=true")).Select(CloudContractMapper.ToProvider).ToList();
    public async Task<Provider> AddAsync(Provider provider) => CloudContractMapper.ToProvider(
        await api.PostAsync<SaveProviderRequest, ProviderDto>("/api/v1/providers", CloudContractMapper.ToSaveProviderRequest(provider)));
    public async Task<Provider> UpdateAsync(Provider provider) => CloudContractMapper.ToProvider(
        await api.PutAsync<SaveProviderRequest, ProviderDto>($"/api/v1/providers/{provider.Id}", CloudContractMapper.ToSaveProviderRequest(provider)));
    public Task DeleteAsync(Provider provider) => api.DeleteAsync($"/api/v1/providers/{provider.Id}");
}

public sealed class CloudComprehensiveAssessmentService(CloudApiClient api) : IComprehensiveAssessmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId) =>
        CloudContractMapper.ToAssessment(await api.PostAsync<object, ComprehensiveAssessmentDto>(
            $"/api/v1/people/{personId}/assessments/draft?authorUserId={authorUserId}", new { }));

    public async Task SaveDocumentAsync(
        ComprehensiveAssessment assessment,
        AssessmentDocument document)
    {
        var updated = await api.PutAsync<SaveAssessmentDocumentRequest, ComprehensiveAssessmentDto>(
            $"/api/v1/assessments/{assessment.Id}/document",
            new SaveAssessmentDocumentRequest(
                JsonSerializer.Serialize(document, JsonOptions),
                assessment.Revision));
        ApplyServerState(assessment, updated);
    }

    public async Task SubmitForReviewAsync(ComprehensiveAssessment assessment)
    {
        var updated = await api.PostAsync<object, ComprehensiveAssessmentDto>(
            $"/api/v1/assessments/{assessment.Id}/submit?authorUserId={assessment.AuthorUserId}&expectedRevision={assessment.Revision}",
            new { });
        ApplyServerState(assessment, updated);
    }

    private static void ApplyServerState(
        ComprehensiveAssessment assessment,
        ComprehensiveAssessmentDto updated)
    {
        assessment.Status = Enum.Parse<AssessmentStatus>(updated.Status);
        assessment.UpdatedAt = updated.UpdatedAt;
        assessment.SubmittedAt = updated.SubmittedAt;
        assessment.ApprovedAt = updated.ApprovedAt;
        assessment.ApprovedByUserId = updated.ApprovedByUserId;
        assessment.DocumentJson = updated.DocumentJson;
        assessment.Revision = updated.Revision;
    }
}

public sealed class CloudPersonCenteredPlanSourceService(CloudApiClient api) : IPersonCenteredPlanSourceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId)
    {
        var source = await api.GetAsync<PersonCenteredPlanSourceDto?>(
            $"/api/v1/people/{personId}/pcp-source?preferredAuthorUserId={preferredAuthorUserId}");
        return source is null ? null : new PersonCenteredPlanSource(
            source.AssessmentId, source.Version,
            Enum.Parse<AssessmentStatus>(source.Status), source.UpdatedAt,
            JsonSerializer.Deserialize<AssessmentDocument>(source.DocumentJson, JsonOptions) ?? new AssessmentDocument());
    }
}

public sealed class CloudConsumerBillingLossReportService(CloudApiClient api) : IConsumerBillingLossReportService
{
    public async Task<ConsumerBillingLossReport> GetAsync(int userId, DateTime windowStart, DateTime windowEnd)
    {
        var start = windowStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var end = windowEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var report = await api.GetAsync<ConsumerBillingLossReportDto>(
            $"/api/v1/reports/consumer-billing-loss?start={start}&end={end}");

        return new ConsumerBillingLossReport(
            report.Consumers.Select(x => new ConsumerBillingLossRow(
                x.PersonId,
                x.ConsumerName,
                x.BillableDays,
                x.NonBillableDays,
                x.BillableUnits,
                x.NonBillableUnits,
                x.LostWorkPercentage)).ToList(),
            report.TotalBillableUnits,
            report.TotalNonBillableUnits,
            report.LostWorkPercentage);
    }
}

public sealed class CloudBillingService(CloudApiClient api) : IBillingService
{
    private readonly Dictionary<int, IReadOnlyList<string>> _candidateErrors = [];

    public async Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year) =>
        CloudContractMapper.ToBillingPeriod(await api.PostAsync<object, BillingPeriodDto>(
            $"/api/v1/billing/periods/{year}/{month}?userId={userId}", new { }));

    public async Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId) =>
        (await api.GetAsync<List<BillingPeriodDto>>($"/api/v1/billing/periods?userId={userId}"))
        .Select(CloudContractMapper.ToBillingPeriod)
        .ToList();

    public async Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync() =>
        (await api.GetAsync<List<BillingPeriodDto>>("/api/v1/billing/periods"))
        .Select(CloudContractMapper.ToBillingPeriod)
        .ToList();

    public async Task<ClaimLine> CreateClaimLineAsync(int noteId, bool isComplianceException = false, string? complianceExceptionReason = null) =>
        CloudContractMapper.ToClaimLine(await api.PostAsync<CreateClaimLineRequest, ClaimLineDto>(
            "/api/v1/billing/claim-lines",
            new CreateClaimLineRequest(noteId, isComplianceException, complianceExceptionReason)));

    public async Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId) =>
        (await api.GetAsync<List<ClaimLineDto>>($"/api/v1/billing/claim-lines/draft?userId={userId}"))
        .Select(CloudContractMapper.ToClaimLine)
        .ToList();

    public async Task SubmitBillingPeriodAsync(int billingPeriodId) =>
        _ = await api.PostAsync<object, BillingPeriodDto>($"/api/v1/billing/periods/{billingPeriodId}/submit", new { });

    public async Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync()
    {
        var candidates = await api.GetAsync<List<BillingCandidateDto>>("/api/v1/billing/candidates");
        _candidateErrors.Clear();
        foreach (var candidate in candidates)
            _candidateErrors[candidate.Note.Id] = candidate.Errors;
        return candidates.Select(candidate => CloudContractMapper.ToNote(candidate.Note)).ToList();
    }

    public BillingValidationResult ValidateNoteForBilling(Note note)
    {
        var errors = _candidateErrors.GetValueOrDefault(note.Id) ?? ["Billing validation was not loaded for this note."];
        return new BillingValidationResult(errors.Count == 0, note, errors);
    }
}

public sealed class CloudEdiService(CloudApiClient api) : IEdiService
{
    public async Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey)
    {
        var file = await api.PostAsync<GenerateEdiRequest, EdiFileDto>(
            $"/api/v1/billing/periods/{billingPeriodId}/edi",
            new GenerateEdiRequest(isTest, idempotencyKey));
        var safeName = Path.GetFileName(file.FileName);
        if (!string.Equals(safeName, file.FileName, StringComparison.Ordinal) ||
            !safeName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Demo API returned an invalid EDI file name.");

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Satilogica", "Sati Demo", "EDI");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, safeName);
        await File.WriteAllTextAsync(path, file.Content);
        return path;
    }
}

public sealed class CloudClientAiContextService(CloudApiClient api) : IClientAiContextService
{
    public async Task<ClientAiContext> BuildAsync(int personId, int requestingUserId, string roughNarrative,
        int? excludedNoteId = null, CancellationToken cancellationToken = default)
    {
        var context = await api.PostAsync<BuildClientAiContextRequest, ClientAiContextDto>(
            $"/api/v1/people/{personId}/ai-context",
            new BuildClientAiContextRequest(requestingUserId, roughNarrative, excludedNoteId), cancellationToken);
        return new ClientAiContext(context.PromptText,
            context.Sources.Select(x => new ClientAiContextSource(x.Category, x.Description)).ToList());
    }
}
