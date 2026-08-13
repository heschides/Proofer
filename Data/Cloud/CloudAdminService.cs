using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudAdminService(CloudApiClient api) : IAdminService
{
    public Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<AdminOverviewDto>("/api/v1/admin/overview", cancellationToken);

    public Task<AdminOperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync<AdminOperationsDto>("/api/v1/admin/operations", cancellationToken);

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
}
