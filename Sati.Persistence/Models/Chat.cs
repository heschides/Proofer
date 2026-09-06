namespace Sati.Models;

/// <summary>Explicit agency room. A consumer link narrows access and is never reassigned.</summary>
public sealed class ChatRoom
{
    public int Id { get; set; }
    public int AgencyId { get; set; }
    public int? PersonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long Revision { get; set; } = 1;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public int? ArchivedByUserId { get; set; }
}

/// <summary>A membership episode; removal closes it and a later invitation creates another row.</summary>
public sealed class ChatRoomMember
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int AgencyId { get; set; }
    public int UserId { get; set; }
    public long VisibleAfterSequence { get; set; }
    public int AddedByUserId { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public int? RemovedByUserId { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
}

/// <summary>Original authored record. Corrections and concealment never overwrite these values.</summary>
public sealed class ChatMessage
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public int AgencyId { get; set; }
    public long Sequence { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorDisplayName { get; set; } = string.Empty;
    public Guid ClientMessageId { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public string Body { get; set; } = string.Empty;
}

/// <summary>Durable committed room order, including tombstones for earlier messages.</summary>
public sealed class ChatChange
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public int AgencyId { get; set; }
    public long Sequence { get; set; }
    public string Kind { get; set; } = string.Empty;
    public long? MessageId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public int? TargetUserId { get; set; }
    // Protected record content. Never copied into general audit or operational metadata.
    public string? RedactionReason { get; set; }
}

/// <summary>Presentation acknowledgment, never evidence that a human actually read a message.</summary>
public sealed class ChatReadMarker
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int AgencyId { get; set; }
    public int UserId { get; set; }
    public long LastSeenSequence { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
