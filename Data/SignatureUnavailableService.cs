using Sati.Contracts.V1;

namespace Sati.Data;

/// <summary>Local work has no signing connection or separate database implementation.</summary>
public sealed class SignatureUnavailableService : ISignatureService
{
    public Task<SignatureAvailabilityDto> GetAvailabilityAsync() => Task.FromResult(new SignatureAvailabilityDto(false,
        "Electronic signing is unavailable in this environment. Continue with the paper or assisted process.", "Disabled"));
    private static Task<T> Unavailable<T>() => Task.FromException<T>(new InvalidOperationException("Electronic signing is unavailable in this environment."));
    public Task<IReadOnlyList<SignatureSignerDto>> GetSignersAsync(int personId) => Unavailable<IReadOnlyList<SignatureSignerDto>>();
    public Task<IReadOnlyList<SignatureRequestDto>> GetRequestsAsync(int personId) => Unavailable<IReadOnlyList<SignatureRequestDto>>();
    public Task<FrozenSignatureDocumentDto> FreezeAsync(int personId, int artifactId, FreezeSignatureDocumentRequest request) => Unavailable<FrozenSignatureDocumentDto>();
    public Task<SignatureRequestDto> CreateAsync(CreateSignatureRequest request) => Unavailable<SignatureRequestDto>();
    public Task<SignatureRequestDto> ReplaceAsync(int requestId, ReplaceSignatureRequest request) => Unavailable<SignatureRequestDto>();
    public Task<SignatureRequestDto> RevokeAsync(int requestId, SignatureReasonRequest request) => Unavailable<SignatureRequestDto>();
    public Task<SignatureRequestDto> WithdrawAuthorizationAsync(int requestId, SignatureReasonRequest request) => Unavailable<SignatureRequestDto>();
    public Task<AgencyReleaseResult> GetOriginalAsync(int requestId) => Unavailable<AgencyReleaseResult>();
    public Task<AgencyReleaseResult> GetSignedAsync(int requestId) => Unavailable<AgencyReleaseResult>();
}
