using Sati.Contracts.V1;

namespace Sati.Data.Cloud;

public sealed class CloudChatService(CloudApiClient api) : IChatService
{
    public bool IsAvailableHere => true;
    private const string Root = "/api/v1/chat";
    public Task<ChatAvailabilityDto> GetAvailabilityAsync(CancellationToken cancellationToken = default) => api.GetWithoutRenewalAsync<ChatAvailabilityDto>($"{Root}/availability", cancellationToken);
    public async Task<IReadOnlyList<ChatRoomDto>> GetRoomsAsync(CancellationToken cancellationToken = default) => await api.GetWithoutRenewalAsync<List<ChatRoomDto>>($"{Root}/rooms", cancellationToken);
    public async Task<IReadOnlyList<ChatCandidateDto>> GetCandidatesAsync(int? personId, CancellationToken cancellationToken = default) => await api.GetWithoutRenewalAsync<List<ChatCandidateDto>>($"{Root}/candidates" + (personId is int id ? $"?personId={id}" : ""), cancellationToken);
    public async Task<IReadOnlyList<ChatMemberDto>> GetMembersAsync(int roomId, CancellationToken cancellationToken = default) => await api.GetWithoutRenewalAsync<List<ChatMemberDto>>($"{Root}/rooms/{roomId}/members", cancellationToken);
    public Task<ChatRoomDto> CreateRoomAsync(CreateChatRoomRequest request, CancellationToken cancellationToken = default) => api.PostAsync<CreateChatRoomRequest, ChatRoomDto>($"{Root}/rooms", request, cancellationToken);
    public Task<ChatRoomDto> UpdateRoomAsync(int roomId, UpdateChatRoomRequest request, CancellationToken cancellationToken = default) => api.PutAsync<UpdateChatRoomRequest, ChatRoomDto>($"{Root}/rooms/{roomId}", request, cancellationToken);
    public Task<ChatRoomDto> ArchiveRoomAsync(int roomId, ChatRevisionRequest request, CancellationToken cancellationToken = default) => api.PostAsync<ChatRevisionRequest, ChatRoomDto>($"{Root}/rooms/{roomId}/archive", request, cancellationToken);
    public Task<ChatRoomDto> AddMemberAsync(int roomId, AddChatMemberRequest request, CancellationToken cancellationToken = default) => api.PostAsync<AddChatMemberRequest, ChatRoomDto>($"{Root}/rooms/{roomId}/members", request, cancellationToken);
    public Task<ChatRoomDto> RemoveMemberAsync(int roomId, int userId, long expectedRevision, CancellationToken cancellationToken = default) => api.DeleteAsync<ChatRoomDto>($"{Root}/rooms/{roomId}/members/{userId}?expectedRevision={expectedRevision}", cancellationToken);
    public Task<ChatPageDto> GetMessagesAsync(int roomId, long afterSequence, CancellationToken cancellationToken = default) => api.GetWithoutRenewalAsync<ChatPageDto>($"{Root}/rooms/{roomId}/messages?afterSequence={afterSequence}&take={ChatLimits.MaxPageSize}", cancellationToken);
    public Task<ChatPageDto> GetHistoryAsync(int roomId, long beforeSequence, CancellationToken cancellationToken = default) => api.GetWithoutRenewalAsync<ChatPageDto>($"{Root}/rooms/{roomId}/messages?beforeSequence={beforeSequence}&take={ChatLimits.MaxPageSize}", cancellationToken);
    public Task<ChatMessageDto> PostMessageAsync(int roomId, PostChatMessageRequest request, CancellationToken cancellationToken = default) => api.PostAsync<PostChatMessageRequest, ChatMessageDto>($"{Root}/rooms/{roomId}/messages", request, cancellationToken);
    public Task<ChatMessageDto> RedactMessageAsync(long messageId, RedactChatMessageRequest request, CancellationToken cancellationToken = default) => api.PostAsync<RedactChatMessageRequest, ChatMessageDto>($"{Root}/messages/{messageId}/redact", request, cancellationToken);
    public Task<ChatRoomDto> MarkSeenAsync(int roomId, long sequence, CancellationToken cancellationToken = default) => api.PostAsync<ChatSeenRequest, ChatRoomDto>($"{Root}/rooms/{roomId}/read", new(sequence), cancellationToken);
    public Task ListenForChangesAsync(Action changed, CancellationToken cancellationToken) => new ChatStreamConnection(api).RunAsync(changed, cancellationToken);
}
