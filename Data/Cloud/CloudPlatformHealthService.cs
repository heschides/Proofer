using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudPlatformHealthService(CloudApiClient api) : IPlatformHealthService
{
    public Task<PlatformIncidentDashboardDto> GetDashboardAsync(
        int days = 30,
        int take = 500,
        CancellationToken cancellationToken = default) =>
        api.GetAsync<PlatformIncidentDashboardDto>(
            $"/api/v1/platform/incidents?days={days}&take={take}",
            cancellationToken);
}
