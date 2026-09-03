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
    Task<IncidentGroupDto> UpdateIncidentStatusAsync(
        long incidentId,
        string status,
        CancellationToken cancellationToken = default);
    Task<byte[]> ExportAuditCsvAsync(
        DateTime fromUtc,
        DateTime toUtc,
        string reason,
        CancellationToken cancellationToken = default);
    Task<List<AdminPersonListItemDto>> GetPeopleAsync(CancellationToken cancellationToken = default);
    Task<TestConsumerDeletionResultDto> DeleteTestConsumerAsync(
        int personId,
        int expectedRevision,
        string attestation,
        CancellationToken cancellationToken = default);
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

    // Legal holds — the fail-closed gate rule-3 deletion checks before removing a record. See
    // HANDOFF_CLIENT_DELETION_POLICY.md, A3. Deliberately narrower than OPERATIONS.md's full
    // record-class/scope hold model; release is single-admin for v1, a documented shortfall
    // against OPERATIONS.md's dual-control requirement.
    Task<LegalHoldDto> PlaceLegalHoldAsync(
        PlaceLegalHoldRequest request,
        CancellationToken cancellationToken = default);
    Task<LegalHoldDto> ReleaseLegalHoldAsync(
        int legalHoldId,
        string? releaseNote,
        CancellationToken cancellationToken = default);
    Task<List<LegalHoldDto>> GetLegalHoldsAsync(
        int personId,
        CancellationToken cancellationToken = default);

    // Rule-3 deletion: permanently deletes an ordinary consumer created within
    // ConsumerDeletionRules.DeletionWindowDays. See HANDOFF_CLIENT_DELETION_POLICY.md.
    Task<ConsumerDeletionResultDto> DeleteConsumerInWindowAsync(
        int personId,
        int expectedRevision,
        string attestation,
        string reason,
        CancellationToken cancellationToken = default);
}
