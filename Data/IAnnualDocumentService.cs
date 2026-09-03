using Sati.Contracts.V1;
namespace Sati.Data;

public interface IAnnualDocumentService
{
    Task<AnnualDocumentsStatusDto> GetStatusAsync(int personId, DateTime cycleStart);
    Task<DocumentAcknowledgmentDto> AcknowledgeAsync(int personId, AcknowledgeDocumentRequest request);
    Task<VerifyDocumentResult> VerifyAsync(int personId, VerifyDocumentRequest request);
    Task<AgencyReleaseResult> SavePacketAsync(int personId, DateTime cycleStart);
}
