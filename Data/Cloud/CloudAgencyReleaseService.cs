using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

/// <summary>Demo and future cloud-Production generation through the authorized API.</summary>
public sealed class CloudAgencyReleaseService(CloudApiClient client) : IAgencyReleaseService
{
    public async Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        AgencyReleaseRules.EnsureValid(request);
        var pdf = await client.PostBytesAsync(
            $"/api/v1/people/{personId}/documents/{AnnualDocumentKind.ReleaseAgency}",
            new RenderAnnualDocumentRequest(Release: request),
            cancellationToken);
        return new AgencyReleaseResult(
            pdf,
            AgencyReleaseService.SuggestedFileName(
                personId, null, null, request.IsRevocation, draft: request.IsDraft));
    }

    public async Task<AgencyReleaseResult> GenerateMedicalAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        AgencyReleaseRules.EnsureValid(request);
        var pdf = await client.PostBytesAsync(
            $"/api/v1/people/{personId}/documents/{AnnualDocumentKind.ReleaseMedical}",
            new RenderAnnualDocumentRequest(Release: request),
            cancellationToken);
        return new AgencyReleaseResult(
            pdf,
            AgencyReleaseService.SuggestedFileName(
                personId, null, null, request.IsRevocation, AnnualDocumentKind.ReleaseMedical, request.IsDraft));
    }
}
