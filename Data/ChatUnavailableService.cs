using Sati.Contracts.V1;

namespace Sati.Data;

public sealed class ChatUnavailableService : IChatService
{
    public const string Explanation = "Team chat is available only in the enabled synthetic Demo. It is unavailable in local work.";
    public bool IsAvailableHere => false;
    private static NotSupportedException Unavailable() => new(Explanation);
    public Task<ChatAvailabilityDto> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ChatAvailabilityDto(false, Explanation));
    public Task<IReadOnlyList<ChatRoomDto>> GetRoomsAsync(CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<IReadOnlyList<ChatCandidateDto>> GetCandidatesAsync(int? personId, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<IReadOnlyList<ChatMemberDto>> GetMembersAsync(int roomId, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatRoomDto> CreateRoomAsync(CreateChatRoomRequest request, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatRoomDto> UpdateRoomAsync(int roomId, UpdateChatRoomRequest request, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatRoomDto> ArchiveRoomAsync(int roomId, ChatRevisionRequest request, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatRoomDto> AddMemberAsync(int roomId, AddChatMemberRequest request, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatRoomDto> RemoveMemberAsync(int roomId, int userId, long expectedRevision, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatPageDto> GetMessagesAsync(int roomId, long afterSequence, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatPageDto> GetHistoryAsync(int roomId, long beforeSequence, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatMessageDto> PostMessageAsync(int roomId, PostChatMessageRequest request, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatMessageDto> RedactMessageAsync(long messageId, RedactChatMessageRequest request, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<ChatRoomDto> MarkSeenAsync(int roomId, long sequence, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task ListenForChangesAsync(Action changed, CancellationToken cancellationToken) => throw Unavailable();
}
