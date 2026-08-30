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
    // A hosted deployment is never without an administrator, because its first one
    // is provisioned by an operator before anyone can sign in. Reporting "yes" here
    // keeps the desktop from ever offering first-run setup against a cloud
    // database — and CreateFirstAdministratorAsync refuses outright if it somehow
    // gets asked anyway, so the answer is a convenience rather than the control.
    public Task<bool> AnyAdministratorExistsAsync() => Task.FromResult(true);

    // Deliberately unavailable. Bootstrapping an administrator without credentials
    // is defensible on a database the caller already has direct access to; exposing
    // the same capability over the network on a multi-tenant service is not, and no
    // API route exists for it. Hosted environments use
    // scripts/Provision-DemoGlobalAdmin.ps1, run by an operator who is already
    // trusted with the database.
    public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) =>
        throw new NotSupportedException(
            "First-run administrator setup is not available against a hosted database. " +
            "Provision the first administrator with scripts/Provision-DemoGlobalAdmin.ps1.");

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
    public async Task<List<User>> GetAllAsync()
    {
        try
        {
            return (await api.GetAsync<List<UserProfileDto>>("/api/v1/users/switchable"))
                .Select(CloudContractMapper.ToUser)
                .ToList();
        }
        catch (CloudSessionEndedException ex)
        {
            // The switch-user directory is the one screen a user reaches *because*
            // something looks wrong, so an expired session has to be named here
            // rather than collapsing into an empty account list.
            throw new SessionExpiredException(ex);
        }
    }
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
        .Select(ToListItem).ToList();

    private static ATRequestListItem ToListItem(AtRequestListItemDto x) => new()
    {
        Id = x.Id, ClientName = x.ClientName, Status = Enum.Parse<ATRequestStatus>(x.Status),
        TotalCost = x.TotalCost, SubmittedDate = x.SubmittedDate, VendorName = x.VendorName,
        CaseManagerName = x.CaseManagerName, HasSnapshot = x.HasSnapshot,
        SignedByName = x.SignedByName, SignedAtUtc = x.SignedAtUtc
    };
    public async Task<List<ATRequestListItem>> GetAllForPersonAsync(int personId) =>
        (await api.GetAsync<List<AtRequestListItemDto>>($"/api/v1/people/{personId}/at-requests"))
        .Select(ToListItem).ToList();
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
        catch (CloudApiException ex) when (ex.Code == "published_at_request")
        {
            throw new AtRequestLockedException(ex);
        }
    }

    // Two calls, in this order: save the edits, then ask the server to publish
    // what it now holds. The caseManager argument is ignored on this path — the
    // API derives the signer from the bearer token, and taking the client's word
    // for who signed is precisely what the publish route refuses to do. It stays
    // in the signature because the desktop-local implementation, which has no
    // token to read, genuinely needs it.
    public async Task<ATRequest> PublishAsync(ATRequest request, User caseManager)
    {
        try
        {
            var saved = request.Id == 0
                ? CloudContractMapper.ToAtRequest(await api.PostAsync<SaveAtRequestRequest, AtRequestDto>(
                    "/api/v1/at-requests", CloudContractMapper.ToSaveAtRequestRequest(request)))
                : await UpdateAsync(request);

            var published = CloudContractMapper.ToAtRequest(
                await api.PostAsync<PublishAtRequestRequest, AtRequestDto>(
                    $"/api/v1/at-requests/{saved.Id}/publish", new PublishAtRequestRequest(saved.Revision)));

            CopyPublicationState(published, request);
            return request;
        }
        catch (CloudApiException ex) when (ex.Code == "stale_at_request")
        {
            throw new AtRequestConcurrencyException(ex);
        }
        catch (CloudApiException ex) when (ex.Code == "published_at_request")
        {
            throw new AtRequestLockedException(ex);
        }
    }

    public async Task<ATRequest> ReopenAsync(ATRequest request)
    {
        try
        {
            var reopened = CloudContractMapper.ToAtRequest(
                await api.PostAsync<ReopenAtRequestRequest, AtRequestDto>(
                    $"/api/v1/at-requests/{request.Id}/reopen", new ReopenAtRequestRequest(request.Revision)));

            CopyPublicationState(reopened, request);
            return request;
        }
        catch (CloudApiException ex) when (ex.Code == "stale_at_request")
        {
            throw new AtRequestConcurrencyException(ex);
        }
    }

    // The server's answer is authoritative for everything publication touches.
    // Copied onto the caller's instance rather than returned in its place so the
    // open editor keeps pointing at the object it was built around.
    private static void CopyPublicationState(ATRequest source, ATRequest target)
    {
        target.RehydrateIdentity(source.Id);
        target.RehydrateAttestation(
            source.SignedByName, source.SignedByRole, source.SignedByUserId,
            source.SignedAtUtc, source.AttestationStatement, source.PassthroughRate);
        target.SubmittedDate = source.SubmittedDate;
        target.SetStatus(source.Status);
        target.Revision = source.Revision;
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

public sealed class CloudConsumerProviderService(CloudApiClient api) : IConsumerProviderService
{
    public async Task<List<PersonProvider>> GetByPersonAsync(int personId) =>
        (await api.GetAsync<List<ConsumerProviderDto>>($"/api/v1/people/{personId}/providers"))
        .Select(CloudContractMapper.ToConsumerProvider)
        .ToList();

    public async Task<PersonProvider> SaveAsync(PersonProvider link)
    {
        var request = CloudContractMapper.ToSaveConsumerProviderRequest(link);
        var response = link.Id == 0
            ? await api.PostAsync<SaveConsumerProviderRequest, ConsumerProviderDto>(
                $"/api/v1/people/{link.PersonId}/providers", request)
            : await api.PutAsync<SaveConsumerProviderRequest, ConsumerProviderDto>(
                $"/api/v1/people/{link.PersonId}/providers/{link.Id}", request);
        return CloudContractMapper.ToConsumerProvider(response);
    }

    // Ending is the ordinary update with EndDate set. Reading the link back first keeps
    // the other fields intact — a PUT replaces the whole row, so ending a relationship
    // must not quietly blank the role or the release flag along the way.
    public async Task EndAsync(int personId, int linkId, DateTime endDate)
    {
        var links = await GetByPersonAsync(personId);
        var link = links.SingleOrDefault(candidate => candidate.Id == linkId)
            ?? throw new InvalidOperationException(
                "That provider entry is no longer on this consumer's record.");
        link.EndDate = endDate.Date;
        await SaveAsync(link);
    }

    public Task RemoveAsync(int personId, int linkId) =>
        api.DeleteAsync($"/api/v1/people/{personId}/providers/{linkId}");
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

    public async Task<List<ProviderContact>> GetContactsAsync(int providerId) =>
        (await api.GetAsync<List<ProviderContactDto>>($"/api/v1/providers/{providerId}/contacts"))
        .Select(CloudContractMapper.ToProviderContact).ToList();

    public async Task<ProviderContact> SaveContactAsync(ProviderContact contact)
    {
        var request = CloudContractMapper.ToSaveProviderContactRequest(contact);
        var response = contact.Id == 0
            ? await api.PostAsync<SaveProviderContactRequest, ProviderContactDto>(
                $"/api/v1/providers/{contact.ProviderId}/contacts", request)
            : await api.PutAsync<SaveProviderContactRequest, ProviderContactDto>(
                $"/api/v1/providers/{contact.ProviderId}/contacts/{contact.Id}", request);
        return CloudContractMapper.ToProviderContact(response);
    }

    public Task RemoveContactAsync(int providerId, int contactId) =>
        api.DeleteAsync($"/api/v1/providers/{providerId}/contacts/{contactId}");

    public async Task<string> MergeAsync(int survivingProviderId, int mergedProviderId) =>
        (await api.PostAsync<MergeProvidersRequest, MergeProvidersResultDto>(
            $"/api/v1/providers/{survivingProviderId}/merge",
            new MergeProvidersRequest(mergedProviderId))).Summary;
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

    public async Task<BillingConfiguration> GetBillingConfigurationAsync() =>
        CloudContractMapper.ToBillingConfiguration(
            await api.GetAsync<BillingConfigurationDto>("/api/v1/billing/configuration"));

    public async Task SaveBillingConfigurationAsync(BillingConfiguration configuration) =>
        _ = await api.PutAsync<SaveBillingConfigurationRequest, BillingConfigurationDto>(
            "/api/v1/billing/configuration",
            new SaveBillingConfigurationRequest(
                configuration.ProcedureCode,
                configuration.Modifier,
                configuration.UnitRate,
                configuration.EdiSubmitterId,
                configuration.PayerName,
                configuration.PayerId,
                configuration.ContactName,
                configuration.ContactPhone));

    public async Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync() =>
        await api.GetAsync<List<BillingSubmissionHistoryDto>>("/api/v1/billing/submissions");

    public async Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync() =>
        await api.GetAsync<List<RemittanceClaimOutcomeDto>>("/api/v1/billing/remittances");

    public async Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync() =>
        await api.GetAsync<List<RemittanceDepositDto>>("/api/v1/billing/remittance-deposits");
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
            "SatiLogica", "Sati Demo", "EDI");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, safeName);
        await File.WriteAllTextAsync(path, file.Content);
        return path;
    }
}

public sealed class CloudClientAiContextService(CloudApiClient api) : IClientAiContextService
{
    public async Task<ClientAiContext> BuildAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var context = await api.GetAsync<ClientAiContextDto>(
            $"/api/v1/people/{personId}/ai-context", cancellationToken);
        return new ClientAiContext(context.PersonId, context.ConsumerFirstName,
            context.Sources.Select(x => new ClientAiContextSource(x.Category, x.Description)).ToList());
    }
}
