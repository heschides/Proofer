using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

public sealed class ChatAccessTests
{
    private static readonly AgencyActor Worker = new(10, 1, UserPermissions.CaseManagement);
    private static readonly ChatRoomScope Room = new(20, 1, false);
    private static readonly ChatMembership Member = new(20, 10, 1, true);

    [Fact]
    public void ActiveMemberCanReadAndPostButCannotAdminister()
    {
        Assert.True(ChatAccess.CanReadRoom(Worker, Room, Member));
        Assert.True(ChatAccess.CanPostToRoom(Worker, Room, Member));
        Assert.False(ChatAccess.CanAdministerRoom(Worker, Room));
        Assert.False(ChatAccess.CanRedact(Worker, Room, Member));
    }

    [Theory]
    [InlineData(21, 10, 1, true)]
    [InlineData(20, 11, 1, true)]
    [InlineData(20, 10, 2, true)]
    [InlineData(20, 10, 1, false)]
    public void EveryMembershipBindingIsRequired(int roomId, int userId, int agencyId, bool active)
    {
        var membership = new ChatMembership(roomId, userId, agencyId, active);
        Assert.False(ChatAccess.CanReadRoom(Worker, Room, membership));
        Assert.False(ChatAccess.CanPostToRoom(Worker, Room, membership));
    }

    [Theory]
    [InlineData(UserPermissions.None)]
    [InlineData(UserPermissions.Billing)]
    [InlineData((UserPermissions)1025)]
    public void KnownBitsAloneAreNotChatPermission(UserPermissions permissions)
    {
        var actor = Worker with { Permissions = permissions };
        Assert.False(ChatAccess.IsEligible(actor));
        Assert.False(ChatAccess.CanReadRoom(actor, Room, Member));
    }

    [Fact]
    public void AdministrationNeverGrantsAutomaticReadOrCrossAgencyAccess()
    {
        var admin = Worker with { Permissions = UserPermissions.AllAgencyPermissions };
        Assert.True(ChatAccess.CanAdministerRoom(admin, Room));
        Assert.False(ChatAccess.CanReadRoom(admin, Room, null));
        Assert.False(ChatAccess.CanRedact(admin, Room, null));
        Assert.False(ChatAccess.CanAdministerRoom(admin, Room with { AgencyId = 2 }));
        Assert.False(ChatAccess.CanReadRoom(admin, Room with { AgencyId = 2 }, Member));
    }

    [Fact]
    public void ArchivedRoomRetainsReadsAndRedactionButRejectsPosting()
    {
        var room = Room with { IsArchived = true };
        var supervisor = Worker with { Permissions = UserPermissions.Supervision };
        Assert.True(ChatAccess.CanReadRoom(supervisor, room, Member));
        Assert.False(ChatAccess.CanPostToRoom(supervisor, room, Member));
        Assert.True(ChatAccess.CanRedact(supervisor, room, Member));
        Assert.False(ChatAccess.CanAdministerRoom(supervisor, room));
    }
}
