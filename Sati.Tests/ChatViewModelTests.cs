using System.Net;
using System.Reflection;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ChatViewModelTests
{
    [Fact]
    public async Task HiddenWorkspaceDoesNotFetchOrMarkReadAndLatePageCannotRepopulateIt()
    {
        var fixture = new ChatFixture();
        fixture.ViewModel.ResumeAccount();
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Service.RoomCalls);
        fixture.ViewModel.SetSurfaceState(true, false);
        var pendingPage = NewPageSource();
        fixture.Service.Page = (_, _) => pendingPage.Task;
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        var load = fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.SetSurfaceState(true, true);
        pendingPage.SetResult(Page(1, "Behind the privacy screen"));
        await load;
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.False(fixture.ViewModel.MarkShownMessagesSeenCommand.CanExecute(null));
        await fixture.ViewModel.MarkShownMessagesSeenCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Service.SeenCalls);
        var requests = fixture.Service.PageCalls;
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(requests, fixture.Service.PageCalls);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task AResponseFromAnEarlierRoomCannotReplaceTheSelectedConversation()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        var first = NewPageSource();
        fixture.Service.Page = (room, _) => room == 1 ? first.Task : Task.FromResult(Page(2, "Selected room"));
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        var firstLoad = fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[1];
        await fixture.ViewModel.SelectionLoadTask;
        first.SetResult(Page(1, "Old room"));
        await firstLoad;
        Assert.Equal("Selected room", Assert.Single(fixture.ViewModel.Messages).Body);
        Assert.Equal(2, fixture.ViewModel.SelectedRoom!.Id);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task AccountChangeRejectsLateResponseEvenBeforeTheShellResetsTheView()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        var first = NewPageSource();
        fixture.Service.Page = (_, _) => first.Task;
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        fixture.Session.SetUser(User.Create(2, "other", "Other staff", "", "", UserRole.CaseManager, null, 2));
        first.SetResult(Page(1, "Previous agency"));
        await fixture.ViewModel.SelectionLoadTask;
        Assert.Empty(fixture.ViewModel.Messages);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task UnknownSendRetainsIdentifierAndOriginalBodyForTheExplicitRetry()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.Draft = "  Original message  ";
        fixture.Service.Post = (_, request) => Task.FromException<ChatMessageDto>(new HttpRequestException("Connection lost"));
        await fixture.ViewModel.SendCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.HasPendingSend);
        Assert.Equal("Original message", fixture.ViewModel.Draft);
        Assert.Equal("Retry this message", fixture.ViewModel.SendLabel);
        // Even a non-UI caller cannot accidentally change the body behind the pending identity.
        fixture.ViewModel.Draft = "Different text";
        fixture.Service.Post = (room, request) => Task.FromResult(Message(room, request.Body));
        await fixture.ViewModel.SendCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.Service.Posted.Count);
        Assert.Equal(fixture.Service.Posted[0], fixture.Service.Posted[1]);
        Assert.False(fixture.ViewModel.HasPendingSend);
        Assert.Empty(fixture.ViewModel.Draft);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task EndingTheSessionErasesDraftPendingSendAndMessageText()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.Draft = "Private draft";
        fixture.Service.Post = (_, _) => Task.FromException<ChatMessageDto>(new HttpRequestException());
        await fixture.ViewModel.SendCommand.ExecuteAsync(null);
        fixture.ViewModel.SuspendAndClear();
        Assert.Empty(fixture.ViewModel.Rooms);
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.Empty(fixture.ViewModel.Draft);
        Assert.False(fixture.ViewModel.HasPendingSend);
        Assert.Null(fixture.ViewModel.SelectedRoom);
        Assert.False(fixture.ViewModel.SendCommand.CanExecute(null));
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task LosingMembershipClearsLoadedTextAndPendingDraft()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.Draft = "Private draft";
        fixture.Service.Rooms = [fixture.Service.Rooms[1]];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.Empty(fixture.ViewModel.Draft);
        Assert.Null(fixture.ViewModel.SelectedRoom);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task AForbiddenRefreshClearsTheConversation()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.Service.Page = (_, _) => Task.FromException<ChatPageDto>(new CloudApiException(HttpStatusCode.Forbidden, "Denied", null));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.Null(fixture.ViewModel.SelectedRoom);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task RedactionReplacesAnExistingMessageAndOverlappingPagesDoNotDuplicateIt()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Single(fixture.ViewModel.Messages);
        var hidden = Message(1, null) with { Sequence = 2, RedactedAtUtc = DateTime.UtcNow, RedactedByUserId = 1 };
        fixture.Service.Page = (_, _) => Task.FromResult(new ChatPageDto([new(2, "redaction", hidden)], 2, false, 2));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        var message = Assert.Single(fixture.ViewModel.Messages);
        Assert.Contains("Message hidden", message.Body);
        Assert.DoesNotContain("Example message", message.Body);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task FetchingMessagesDoesNotClaimTheyWereRead()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        Assert.Equal(0, fixture.Service.SeenCalls);
        await fixture.ViewModel.MarkShownMessagesSeenCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Service.SeenCalls);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task ACompletedOldSendCannotClearANewDraftAfterReauthentication()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        var response = new TaskCompletionSource<ChatMessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.Post = (_, _) => response.Task;
        fixture.ViewModel.Draft = "Old draft";
        var oldSend = fixture.ViewModel.SendCommand.ExecuteAsync(null);
        fixture.ViewModel.ResumeAccount();
        fixture.ViewModel.SetSurfaceState(true, false);
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.Draft = "New session draft";
        response.SetResult(Message(1, "Old draft"));
        await oldSend;
        Assert.Equal("New session draft", fixture.ViewModel.Draft);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task LocalProductionServiceCannotReadWriteOrConnect()
    {
        var service = new ChatUnavailableService();
        Assert.False(service.IsAvailableHere);
        Assert.False((await service.GetAvailabilityAsync()).Enabled);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetRoomsAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => service.PostMessageAsync(1, new(1, Guid.NewGuid(), "Text")));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.ListenForChangesAsync(() => { }, default));
    }

    [Fact]
    public async Task OlderHistoryKeepsItsOwnCursorWhileLiveRedactionsStillApply()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        var newest = Message(1, "Latest message") with { Id = 90, Sequence = 90 };
        var older = Message(1, "Older sensitive text") with { Id = 50, Sequence = 50 };
        fixture.Service.History = (_, before) => Task.FromResult(before == long.MaxValue
            ? new ChatPageDto([new(90, "message", newest)], 90, true, 100)
            : new ChatPageDto([new(50, "message", older)], 50, false, 100));
        fixture.Service.Page = (_, after) => Task.FromResult(new ChatPageDto([], after, false, after));
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        Assert.Equal(100, fixture.Service.LiveCursors.Last());
        await fixture.ViewModel.LoadOlderCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.IsBrowsingHistory);
        Assert.Equal("Older sensitive text", Assert.Single(fixture.ViewModel.Messages).Body);
        Assert.Equal(90, fixture.Service.HistoryCursors.Last());
        Assert.False(fixture.ViewModel.CanCompose);
        var tombstone = older with { Body = null, RedactedAtUtc = DateTime.UtcNow, RedactedByUserId = 1 };
        fixture.Service.Page = (_, _) => Task.FromResult(new ChatPageDto([new(101, "redaction", tombstone), new(102, "message", newest with { Id = 102, Sequence = 102 })], 102, false, 102));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(100, fixture.Service.LiveCursors.Last());
        Assert.Contains("Message hidden", Assert.Single(fixture.ViewModel.Messages).Body);
        Assert.False(fixture.ViewModel.MarkShownMessagesSeenCommand.CanExecute(null));
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task BackgroundRefreshCannotUpgradeTheVersionOfUnsavedRoomEdits()
    {
        var fixture = new ChatFixture();
        fixture.Session.SetUser(User.Create(1, "admin", "Example admin", "", "", UserRole.Admin, null, 1));
        fixture.Service.Page = (room, _) => Task.FromResult(new ChatPageDto([], fixture.Service.Rooms.First(item => item.Id == room).Revision, false, fixture.Service.Rooms.First(item => item.Id == room).Revision));
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.RoomName = "My unsaved edit";
        fixture.Service.Rooms = [fixture.Service.Rooms[0] with { Name = "Another administrator's edit", Revision = 2 }, fixture.Service.Rooms[1]];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("My unsaved edit", fixture.ViewModel.RoomName);
        await fixture.ViewModel.UpdateRoomCommand.ExecuteAsync(null);
        Assert.Equal(1, Assert.Single(fixture.Service.Updated).ExpectedRevision);
        Assert.Equal("Another administrator's edit", fixture.Service.Rooms[0].Name);
        Assert.Equal("My unsaved edit", fixture.ViewModel.RoomName);
        Assert.Contains("changed", fixture.ViewModel.Status);
        await fixture.ViewModel.ReloadRoomDetailsCommand.ExecuteAsync(null);
        Assert.Equal("Another administrator's edit", fixture.ViewModel.RoomName);
        fixture.ViewModel.RoomName = "Reviewed edit";
        await fixture.ViewModel.UpdateRoomCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.Service.Updated.Last().ExpectedRevision);
        Assert.Equal("Reviewed edit", fixture.Service.Rooms[0].Name);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task RejoiningBetweenRefreshesClearsEarlierMessagesAndPendingDraft()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.Draft = "Earlier membership draft";
        fixture.Service.Post = (_, _) => Task.FromException<ChatMessageDto>(new HttpRequestException());
        await fixture.ViewModel.SendCommand.ExecuteAsync(null);
        Assert.True(fixture.ViewModel.HasPendingSend);
        fixture.Service.Rooms = [fixture.Service.Rooms[0] with { MembershipId = 102, Revision = 3 }, fixture.Service.Rooms[1]];
        fixture.Service.Page = (_, _) => Task.FromResult(new ChatPageDto([], 3, false, 3));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.Empty(fixture.ViewModel.Draft);
        Assert.False(fixture.ViewModel.HasPendingSend);
        Assert.Equal(102, fixture.ViewModel.SelectedRoom!.Room.MembershipId);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task EarlierMembershipResponseCannotRepublishAfterRejoin()
    {
        var fixture = new ChatFixture();
        fixture.Start();
        var oldPage = NewPageSource();
        fixture.Service.History = (_, _) => oldPage.Task;
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        var earlierLoad = fixture.ViewModel.SelectionLoadTask;
        fixture.Service.Rooms = [fixture.Service.Rooms[0] with { MembershipId = 102, Revision = 3 }, fixture.Service.Rooms[1]];
        fixture.Service.History = (_, _) => Task.FromResult(new ChatPageDto([], 3, false, 3));
        fixture.Service.Page = (_, _) => Task.FromResult(new ChatPageDto([], 3, false, 3));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        oldPage.SetResult(Page(1, "Earlier membership secret"));
        await earlierLoad;
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.Equal(102, fixture.ViewModel.SelectedRoom!.Room.MembershipId);
        await fixture.ViewModel.StopAsync();
    }

    [Theory]
    [InlineData(102)]
    [InlineData(null)]
    public async Task APageFromAnUnrecognizedMembershipCannotMergeIntoTheCurrentConversation(int? membership)
    {
        var fixture = new ChatFixture();
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        await fixture.ViewModel.SelectionLoadTask;
        fixture.ViewModel.Draft = "Current draft";
        fixture.Service.StampMembership = false;
        fixture.Service.Page = (_, _) => Task.FromResult(Page(1, "Different membership") with { MembershipId = membership });
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(fixture.ViewModel.Messages);
        Assert.Empty(fixture.ViewModel.Draft);
        Assert.Null(fixture.ViewModel.SelectedRoom);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task RoomWithoutAMembershipIdentityCannotBeSelected()
    {
        var fixture = new ChatFixture();
        fixture.Service.Rooms = [fixture.Service.Rooms[0] with { MembershipId = null }];
        fixture.Start();
        Assert.Empty(fixture.ViewModel.Rooms);
        await fixture.ViewModel.StopAsync();
    }

    [Fact]
    public async Task ConsumerRoomsAlwaysShowTheLinkedPersonAndRecordNumber()
    {
        var fixture = new ChatFixture();
        fixture.Service.Rooms = [fixture.Service.Rooms[0] with { PersonId = 37, ConsumerDisplayName = "Morgan Example" }];
        fixture.Start();
        fixture.ViewModel.SelectedRoom = fixture.ViewModel.Rooms[0];
        Assert.Contains("Morgan Example", fixture.ViewModel.RoomNotice);
        Assert.Contains("record 37", fixture.ViewModel.RoomNotice);
        Assert.Contains("Morgan Example", fixture.ViewModel.SelectedRoom.PickerLabel);
        Assert.Contains("record 37", fixture.ViewModel.SelectedRoom.AccessibleName);
        await fixture.ViewModel.StopAsync();
    }

    private static TaskCompletionSource<ChatPageDto> NewPageSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static ChatMessageDto Message(int room, string? body) => new(room * 10, room, 1, 1, "Example staff", new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc), body, null, null);
    internal static ChatPageDto Page(int room, string body) => new([new(1, "message", Message(room, body))], 1, false, 1);

    internal sealed class ChatFixture
    {
        public FakeChatService Service { get; } = new();
        public SessionService Session { get; } = new();
        public ChatViewModel ViewModel { get; }
        public ChatFixture()
        {
            Session.SetUser(User.Create(1, "staff", "Example staff", "", "", UserRole.CaseManager, null, 1));
            ViewModel = new(Service, Session, DispatchProxy.Create<IAdminService, ChatPeopleProxy>());
        }
        public void Start() { ViewModel.ResumeAccount(); ViewModel.SetSurfaceState(true, false); }
    }

    public class ChatPeopleProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IAdminService.GetPeopleAsync)
                ? Task.FromResult(new List<AdminPersonListItemDto>()) : throw new NotSupportedException();
    }

    internal sealed class FakeChatService : IChatService
    {
        public bool IsAvailableHere => true;
        public IReadOnlyList<ChatRoomDto> Rooms { get; set; } = [new(1, "First room", null, null, 1, false, 0, 1, 101), new(2, "Second room", null, null, 1, false, 0, 0, 201)];
        public int RoomCalls { get; private set; }
        public int PageCalls { get; private set; }
        public int SeenCalls { get; private set; }
        public List<PostChatMessageRequest> Posted { get; } = [];
        public List<UpdateChatRoomRequest> Updated { get; } = [];
        public bool StampMembership { get; set; } = true;
        public List<long> LiveCursors { get; } = [];
        public List<long> HistoryCursors { get; } = [];
        public Func<int, long, Task<ChatPageDto>>? History { get; set; }
        public Func<int, long, Task<ChatPageDto>> Page { get; set; } = (room, _) => Task.FromResult(ChatViewModelTests.Page(room, "Example message"));
        public Func<int, PostChatMessageRequest, Task<ChatMessageDto>> Post { get; set; } = (room, request) => Task.FromResult(Message(room, request.Body));
        public Task<ChatAvailabilityDto> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ChatAvailabilityDto(true, "Synthetic Demo"));
        public Task<IReadOnlyList<ChatRoomDto>> GetRoomsAsync(CancellationToken cancellationToken = default) { RoomCalls++; return Task.FromResult(Rooms); }
        public Task<IReadOnlyList<ChatCandidateDto>> GetCandidatesAsync(int? personId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ChatCandidateDto>>([]);
        public Task<IReadOnlyList<ChatMemberDto>> GetMembersAsync(int roomId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ChatMemberDto>>([new(1, "Example staff", DateTime.UtcNow)]);
        public async Task<ChatPageDto> GetMessagesAsync(int roomId, long afterSequence, CancellationToken cancellationToken = default)
        {
            PageCalls++; LiveCursors.Add(afterSequence);
            var membership = Rooms.First(room => room.Id == roomId).MembershipId;
            var page = await Page(roomId, afterSequence);
            return StampMembership ? page with { MembershipId = page.MembershipId ?? membership } : page;
        }
        public async Task<ChatPageDto> GetHistoryAsync(int roomId, long beforeSequence, CancellationToken cancellationToken = default)
        {
            PageCalls++; HistoryCursors.Add(beforeSequence);
            var membership = Rooms.First(room => room.Id == roomId).MembershipId;
            var page = await (History ?? Page)(roomId, beforeSequence);
            return StampMembership ? page with { MembershipId = page.MembershipId ?? membership } : page;
        }
        public Task<ChatMessageDto> PostMessageAsync(int roomId, PostChatMessageRequest request, CancellationToken cancellationToken = default) { Posted.Add(request); return Post(roomId, request); }
        public Task<ChatRoomDto> MarkSeenAsync(int roomId, long sequence, CancellationToken cancellationToken = default) { SeenCalls++; return Task.FromResult(Rooms.First(room => room.Id == roomId)); }
        public Task ListenForChangesAsync(Action changed, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ChatRoomDto> CreateRoomAsync(CreateChatRoomRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatRoomDto> UpdateRoomAsync(int roomId, UpdateChatRoomRequest request, CancellationToken cancellationToken = default)
        {
            Updated.Add(request);
            var current = Rooms.First(room => room.Id == roomId);
            if (current.Revision != request.ExpectedRevision) return Task.FromException<ChatRoomDto>(new CloudApiException(HttpStatusCode.Conflict, "Changed", null));
            var updated = current with { Name = request.Name, Description = request.Description, Revision = current.Revision + 1 };
            Rooms = Rooms.Select(room => room.Id == roomId ? updated : room).ToList();
            return Task.FromResult(updated);
        }
        public Task<ChatRoomDto> ArchiveRoomAsync(int roomId, ChatRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatRoomDto> AddMemberAsync(int roomId, AddChatMemberRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatRoomDto> RemoveMemberAsync(int roomId, int userId, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatMessageDto> RedactMessageAsync(long messageId, RedactChatMessageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
