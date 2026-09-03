using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudDocumentTemplateService(CloudApiClient client, ISessionService session) : IDocumentTemplateService
{
    private int AgencyId => session.CurrentUser?.AgencyId
        ?? throw new InvalidOperationException("Sign in before managing templates.");

    public async Task<IReadOnlyList<DocumentTemplateDto>> GetVersionsAsync(AnnualDocumentKind kind) =>
        await client.GetAsync<List<DocumentTemplateDto>>($"/api/v1/agencies/{AgencyId}/templates/{kind}");

    public Task<DocumentTemplateDto> PublishAsync(AnnualDocumentKind kind, string body) =>
        client.PostAsync<PublishDocumentTemplateRequest, DocumentTemplateDto>(
            $"/api/v1/agencies/{AgencyId}/templates/{kind}", new PublishDocumentTemplateRequest(body));

    public async Task<AgencyReleaseResult> GeneratePrivacyPracticesAsync(int personId, DateTime? cycleStart = null) =>
        new(await client.PostBytesAsync(
            $"/api/v1/people/{personId}/documents/{AnnualDocumentKind.PrivacyPractices}",
            new RenderAnnualDocumentRequest(CycleStart: cycleStart)), $"Privacy-Practices-{personId}.pdf");
}
