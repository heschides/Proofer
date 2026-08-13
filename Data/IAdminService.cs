using Sati.Contracts.V1;

namespace Sati.Data;

public interface IAdminService
{
    Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<AdminOperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default);
    Task<AdminIncidentDashboardDto> GetIncidentsAsync(
        int days = 30,
        int take = 250,
        CancellationToken cancellationToken = default);
    Task<byte[]> ExportAuditCsvAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string reason,
        CancellationToken cancellationToken = default);
    Task<List<AdminPersonListItemDto>> GetPeopleAsync(CancellationToken cancellationToken = default);
    Task<List<AdminActivityDto>> GetActivityAsync(
        int days = 30,
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<List<PersonVersionDto>> GetPersonHistoryAsync(
        int personId,
        CancellationToken cancellationToken = default);
    Task<byte[]> ExportPersonHistoryPdfAsync(
        int personId,
        CancellationToken cancellationToken = default);
}
