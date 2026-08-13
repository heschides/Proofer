using Sati.Contracts.V1;

namespace Sati.Data;

public interface IPlatformHealthService
{
    Task<PlatformIncidentDashboardDto> GetDashboardAsync(
        int days = 30,
        int take = 500,
        CancellationToken cancellationToken = default);
}
