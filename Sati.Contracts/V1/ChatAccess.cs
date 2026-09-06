namespace Sati.Contracts.V1;

// Every input is resolved from persisted state by the server. Room membership is
// necessary, never a substitute for the existing consumer-access check.
public readonly record struct ChatRoomScope(int RoomId, int AgencyId, bool IsArchived);
public readonly record struct ChatMembership(int RoomId, int UserId, int AgencyId, bool IsActive);

public static class ChatAccess
{
    public static bool IsEligible(AgencyActor actor) => actor.UserId > 0 && actor.AgencyId > 0 &&
        (UserPermissionRules.HasCaseManagerPermissions(actor.Permissions) ||
         UserPermissionRules.HasSupervisorPermissions(actor.Permissions) ||
         UserPermissionRules.HasAdminPermissions(actor.Permissions));

    public static bool CanReadRoom(AgencyActor actor, ChatRoomScope room, ChatMembership? membership) =>
        IsEligible(actor) && room.AgencyId == actor.AgencyId && membership is { IsActive: true } member &&
        member.RoomId == room.RoomId && member.UserId == actor.UserId && member.AgencyId == actor.AgencyId;

    public static bool CanPostToRoom(AgencyActor actor, ChatRoomScope room, ChatMembership? membership) =>
        CanReadRoom(actor, room, membership) && !room.IsArchived;

    // Administration does not grant a read bypass. Clinical scope is checked separately.
    public static bool CanAdministerRoom(AgencyActor actor, ChatRoomScope room) =>
        IsEligible(actor) && room.AgencyId == actor.AgencyId &&
        UserPermissionRules.HasAdminPermissions(actor.Permissions);

    public static bool CanRedact(AgencyActor actor, ChatRoomScope room, ChatMembership? membership) =>
        CanReadRoom(actor, room, membership) &&
        (UserPermissionRules.HasSupervisorPermissions(actor.Permissions) ||
         UserPermissionRules.HasAdminPermissions(actor.Permissions));
}
