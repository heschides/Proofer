namespace Sati.Contracts.V1;

public sealed record ChatAvailabilityDto(bool Enabled, string Explanation);
public sealed record ChatRoomDto(int Id, string Name, string? Description, int? PersonId,
    long Revision, bool IsArchived, long LastSeenSequence, int UnreadCount, int? MembershipId = null,
    string? ConsumerDisplayName = null);
public sealed record ChatMemberDto(int UserId, string DisplayName, DateTime AddedAtUtc);
public sealed record ChatCandidateDto(int UserId, string DisplayName);
public sealed record ChatMessageDto(long Id, int RoomId, long Sequence, int AuthorUserId,
    string AuthorDisplayName, DateTime PostedAtUtc, string? Body,
    DateTime? RedactedAtUtc, int? RedactedByUserId);
public sealed record ChatChangeDto(long Sequence, string Kind, ChatMessageDto Message);
/// <summary>
/// With afterSequence, NextSequence is the durable forward cursor. With beforeSequence,
/// it is the oldest post in this history page, for the next backward page. History reads
/// never overwrite an existing forward cursor; RoomRevision is the snapshot boundary.
/// </summary>
public sealed record ChatPageDto(IReadOnlyList<ChatChangeDto> Changes, long NextSequence,
    bool HasMore, long RoomRevision, int? MembershipId = null);
public sealed record CreateChatRoomRequest(string Name, string? Description, int? PersonId,
    IReadOnlyList<int> MemberUserIds);
public sealed record UpdateChatRoomRequest(long ExpectedRevision, string Name, string? Description);
public sealed record ChatRevisionRequest(long ExpectedRevision);
public sealed record AddChatMemberRequest(long ExpectedRevision, int UserId);
public sealed record PostChatMessageRequest(long ExpectedRevision, Guid ClientMessageId, string Body);
public sealed record RedactChatMessageRequest(long ExpectedRevision, string Reason);
public sealed record ChatSeenRequest(long Sequence);

public static class ChatLimits
{
    public const int MaxBodyLength = 4000;
    public const int MaxPageSize = 100;
    public const int MaxMembers = 250;
    public const int MaxRoomsPerUser = 50;
    public const string RecordNotice = "Chat does not replace service notes. Messages are retained and may be included in records requests.";
    public const string GeneralRoomNotice = "General coordination only. Use an authorized consumer room for client information. Do not include specially restricted records without privacy approval.";
}
