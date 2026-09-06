using Sati.Contracts.V1;

namespace Sati.Data;

public interface ISignatureService
{
    Task<SignatureAvailabilityDto> GetAvailabilityAsync();
    Task<IReadOnlyList<SignatureSignerDto>> GetSignersAsync(int personId);
    Task<IReadOnlyList<SignatureRequestDto>> GetRequestsAsync(int personId);
    Task<FrozenSignatureDocumentDto> FreezeAsync(int personId, int artifactId, FreezeSignatureDocumentRequest request);
    Task<SignatureRequestDto> CreateAsync(CreateSignatureRequest request);
    Task<SignatureRequestDto> ReplaceAsync(int requestId, ReplaceSignatureRequest request);
    Task<SignatureRequestDto> RevokeAsync(int requestId, SignatureReasonRequest request);
    Task<SignatureRequestDto> WithdrawAuthorizationAsync(int requestId, SignatureReasonRequest request);
    Task<AgencyReleaseResult> GetOriginalAsync(int requestId);
    Task<AgencyReleaseResult> GetSignedAsync(int requestId);
}
