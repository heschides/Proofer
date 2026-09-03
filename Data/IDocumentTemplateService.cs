using Sati.Contracts.V1;

namespace Sati.Data;

public interface IDocumentTemplateService
{
    Task<IReadOnlyList<DocumentTemplateDto>> GetVersionsAsync(AnnualDocumentKind kind);
    Task<DocumentTemplateDto> PublishAsync(AnnualDocumentKind kind, string body);
    Task<AgencyReleaseResult> GeneratePrivacyPracticesAsync(int personId, DateTime? cycleStart = null);
}
