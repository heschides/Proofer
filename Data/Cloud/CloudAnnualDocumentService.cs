using Sati.Contracts.V1;
namespace Sati.Data.Cloud;

public sealed class CloudAnnualDocumentService(CloudApiClient api) : IAnnualDocumentService
{
    public Task<AnnualDocumentsStatusDto> GetStatusAsync(int personId, DateTime cycleStart) =>
        api.GetAsync<AnnualDocumentsStatusDto>($"/api/v1/people/{personId}/annual-documents?cycleStart={cycleStart:yyyy-MM-dd}");
    public Task<DocumentAcknowledgmentDto> AcknowledgeAsync(int personId, AcknowledgeDocumentRequest request) =>
        api.PostAsync<AcknowledgeDocumentRequest, DocumentAcknowledgmentDto>($"/api/v1/people/{personId}/documents/privacy-practices/acknowledgment", request);
    public Task<VerifyDocumentResult> VerifyAsync(int personId, VerifyDocumentRequest request) =>
        api.PostAsync<VerifyDocumentRequest, VerifyDocumentResult>($"/api/v1/people/{personId}/documents/verify", request);
    public async Task<AgencyReleaseResult> SavePacketAsync(int personId, DateTime cycleStart) =>
        new(await api.PostBytesAsync($"/api/v1/people/{personId}/annual-packet", new SaveAnnualPacketRequest(cycleStart)),
            $"Annual-Documents-{personId}-{cycleStart:yyyy-MM-dd}.zip");
}
