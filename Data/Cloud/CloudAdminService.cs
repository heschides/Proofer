using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudAdminService(CloudApiClient api) : IAdminService
{
    public Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<AdminOverviewDto>("/api/v1/admin/overview", cancellationToken);

    public Task<AdminOperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<AdminOperationsDto>("/api/v1/admin/operations", cancellationToken);

    public async Task<DemoResetResultDto> RequestFullDemoResetAsync(
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        var result = await api.PostAsync<DemoResetRequest, DemoResetResultDto>(
            "/api/v1/admin/demo/reset",
            new DemoResetRequest(confirmation),
            cancellationToken);
        api.InvalidateCurrentSession();
        return result;
    }

    public Task<AdminIncidentDashboardDto> GetIncidentsAsync(
        int days = 30,
        int take = 250,
        CancellationToken cancellationToken = default) =>
        api.GetAsync<AdminIncidentDashboardDto>(
            $"/api/v1/admin/incidents?days={days}&take={take}",
            cancellationToken);

    public Task<IncidentGroupDto> UpdateIncidentStatusAsync(
        long incidentId,
        string status,
        CancellationToken cancellationToken = default) =>
        api.PutAsync<UpdateIncidentStatusRequest, IncidentGroupDto>(
            $"/api/v1/admin/incidents/{incidentId}/status",
            new UpdateIncidentStatusRequest(status),
            cancellationToken);

    public Task<byte[]> ExportAuditCsvAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string reason,
        CancellationToken cancellationToken = default) =>
        api.PostBytesAsync(
            "/api/v1/admin/audit-export.csv",
            new AdminAuditExportRequest(fromUtc, toUtc, reason),
            cancellationToken);

    public Task<List<AdminPersonListItemDto>> GetPeopleAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<List<AdminPersonListItemDto>>("/api/v1/admin/people", cancellationToken);

    public Task<TestConsumerDeletionResultDto> DeleteTestConsumerAsync(
        int personId,
        int expectedRevision,
        string attestation,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<DeleteTestConsumerRequest, TestConsumerDeletionResultDto>(
            $"/api/v1/admin/test-data/consumers/{personId}/delete",
            new DeleteTestConsumerRequest(expectedRevision, attestation),
            cancellationToken);

    public Task<List<AdminActivityDto>> GetActivityAsync(
        int days = 30,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        api.GetAsync<List<AdminActivityDto>>(
            $"/api/v1/admin/activity?days={days}&take={take}",
            cancellationToken);

    public Task<List<PersonVersionDto>> GetPersonHistoryAsync(
        int personId,
        CancellationToken cancellationToken = default) =>
        api.GetAsync<List<PersonVersionDto>>($"/api/v1/people/{personId}/history", cancellationToken);

    public Task<byte[]> ExportPersonHistoryPdfAsync(
        int personId,
        CancellationToken cancellationToken = default) =>
        api.GetBytesAsync($"/api/v1/people/{personId}/history.pdf", cancellationToken);

    public Task<LegalHoldDto> PlaceLegalHoldAsync(
        PlaceLegalHoldRequest request, CancellationToken cancellationToken = default) =>
        api.PostAsync<PlaceLegalHoldRequest, LegalHoldDto>(
            "/api/v1/admin/legal-holds", request, cancellationToken);

    public Task<LegalHoldDto> ReleaseLegalHoldAsync(
        int legalHoldId, string? releaseNote, CancellationToken cancellationToken = default) =>
        api.PostAsync<ReleaseLegalHoldRequest, LegalHoldDto>(
            $"/api/v1/admin/legal-holds/{legalHoldId}/release",
            new ReleaseLegalHoldRequest(releaseNote),
            cancellationToken);

    public Task<List<LegalHoldDto>> GetLegalHoldsAsync(
        int personId, CancellationToken cancellationToken = default) =>
        api.GetAsync<List<LegalHoldDto>>(
            $"/api/v1/admin/legal-holds?personId={personId}", cancellationToken);

    public Task<ConsumerDeletionResultDto> DeleteConsumerInWindowAsync(
        int personId,
        int expectedRevision,
        string attestation,
        string reason,
        CancellationToken cancellationToken = default) =>
        api.PostAsync<DeleteConsumerInWindowRequest, ConsumerDeletionResultDto>(
            $"/api/v1/admin/consumers/{personId}/delete-in-window",
            new DeleteConsumerInWindowRequest(expectedRevision, attestation, reason),
            cancellationToken);
}
