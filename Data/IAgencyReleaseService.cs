using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>
/// Generates the agency-owned release behind the same local/cloud seam as other
/// protected files. Implementations own authorization, identity derivation, and
/// the disclosure audit event.
/// </summary>
public interface IAgencyReleaseService
{
    Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default);
}
