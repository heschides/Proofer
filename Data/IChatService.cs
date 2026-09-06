using Sati.Contracts.V1;

namespace Sati.Data;

public interface IChatService
{
    bool IsAvailableHere { get; }
    Task<ChatAvailabilityDto> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatRoomDto>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatCandidateDto>> GetCandidatesAsync(int? personId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMemberDto>> GetMembersAsync(int roomId, CancellationToken cancellationToken = default);
    Task<ChatRoomDto> CreateRoomAsync(CreateChatRoomRequest request, CancellationToken cancellationToken = default);
    Task<ChatRoomDto> UpdateRoomAsync(int roomId, UpdateChatRoomRequest request, CancellationToken cancellationToken = default);
    Task<ChatRoomDto> ArchiveRoomAsync(int roomId, ChatRevisionRequest request, CancellationToken cancellationToken = default);
    Task<ChatRoomDto> AddMemberAsync(int roomId, AddChatMemberRequest request, CancellationToken cancellationToken = default);
    Task<ChatRoomDto> RemoveMemberAsync(int roomId, int userId, long expectedRevision, CancellationToken cancellationToken = default);
    Task<ChatPageDto> GetMessagesAsync(int roomId, long afterSequence, CancellationToken cancellationToken = default);
    Task<ChatPageDto> GetHistoryAsync(int roomId, long beforeSequence, CancellationToken cancellationToken = default);
    Task<ChatMessageDto> PostMessageAsync(int roomId, PostChatMessageRequest request, CancellationToken cancellationToken = default);
    Task<ChatMessageDto> RedactMessageAsync(long messageId, RedactChatMessageRequest request, CancellationToken cancellationToken = default);
    Task<ChatRoomDto> MarkSeenAsync(int roomId, long sequence, CancellationToken cancellationToken = default);
    Task ListenForChangesAsync(Action changed, CancellationToken cancellationToken);
}
