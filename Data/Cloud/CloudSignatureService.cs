using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudSignatureService(CloudApiClient api) : ISignatureService
{
    public Task<SignatureAvailabilityDto> GetAvailabilityAsync() => api.GetAsync<SignatureAvailabilityDto>("/api/v1/signatures/availability");
    public Task<IReadOnlyList<SignatureSignerDto>> GetSignersAsync(int personId) => api.GetAsync<IReadOnlyList<SignatureSignerDto>>($"/api/v1/people/{personId}/signature-signers");
    public Task<IReadOnlyList<SignatureRequestDto>> GetRequestsAsync(int personId) => api.GetAsync<IReadOnlyList<SignatureRequestDto>>($"/api/v1/people/{personId}/signature-requests");
    public Task<FrozenSignatureDocumentDto> FreezeAsync(int personId, int artifactId, FreezeSignatureDocumentRequest request) =>
        api.PostAsync<FreezeSignatureDocumentRequest, FrozenSignatureDocumentDto>($"/api/v1/people/{personId}/documents/{artifactId}/freeze", request);
    public Task<SignatureRequestDto> CreateAsync(CreateSignatureRequest request) => api.PostAsync<CreateSignatureRequest, SignatureRequestDto>("/api/v1/signature-requests", request);
    public Task<SignatureRequestDto> ReplaceAsync(int requestId, ReplaceSignatureRequest request) =>
        api.PostAsync<ReplaceSignatureRequest, SignatureRequestDto>($"/api/v1/signature-requests/{requestId}/replace", request);
    public Task<SignatureRequestDto> RevokeAsync(int requestId, SignatureReasonRequest request) =>
        api.PostAsync<SignatureReasonRequest, SignatureRequestDto>($"/api/v1/signature-requests/{requestId}/revoke", request);
    public Task<SignatureRequestDto> WithdrawAuthorizationAsync(int requestId, SignatureReasonRequest request) =>
        api.PostAsync<SignatureReasonRequest, SignatureRequestDto>($"/api/v1/signature-requests/{requestId}/withdraw-authorization", request);
    public async Task<AgencyReleaseResult> GetOriginalAsync(int requestId) => new(await api.GetBytesAsync($"/api/v1/signature-requests/{requestId}/original.pdf"), $"Signature-{requestId}-original.pdf");
    public async Task<AgencyReleaseResult> GetSignedAsync(int requestId) => new(await api.GetBytesAsync($"/api/v1/signature-requests/{requestId}/signed.pdf"), $"Signature-{requestId}-signed.pdf");
}
