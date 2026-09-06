using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Cloud;
using Sati.Services;

namespace Sati.ViewModels.Children;

public sealed partial class ChatRoomItem(ChatRoomDto room) : ObservableObject
{
    public ChatRoomDto Room { get; private set; } = room;
    public int Id => Room.Id;
    public string Name => Room.Name;
    public string ConsumerIdentity => Room.PersonId is int id ? $"{Room.ConsumerDisplayName ?? "Consumer"} · record {id}" : "General coordination";
    public string PickerLabel => $"{Name} · {ConsumerIdentity}";
    public string Summary => Room.IsArchived ? "Archived · read only" : Room.UnreadCount > 0 ? "Unread messages" : "Up to date";
    public string AccessibleName => $"{PickerLabel}. {Summary}";
    public void Update(ChatRoomDto value)
    {
        Room = value;
        OnPropertyChanged(string.Empty);
    }
}

public sealed record ChatMessageItem(ChatMessageDto Message)
{
    public long Id => Message.Id;
    public string Header => $"{Message.AuthorDisplayName} · {Message.PostedAtUtc.ToLocalTime():g}";
    public string Body => Message.RedactedAtUtc is null ? Message.Body ?? "" : $"Message hidden on {Message.RedactedAtUtc.Value.ToLocalTime():g}. Original retained for authorized review.";
    public string AccessibleName => $"{Header}. {Body}";
}

public sealed partial class ChatCandidateItem(ChatCandidateDto candidate) : ObservableObject
{
    public int UserId => candidate.UserId;
    public string DisplayName => candidate.DisplayName;
    [ObservableProperty] private bool isSelected;
}

public sealed record ChatConsumerChoice(int? Id, string Name);

/// <summary>
/// All content loads are scoped to one account, visible workspace and selection.
/// The signal loop carries no content; the only content source is the gated service.
/// Drafts and pending sends live only in bounded memory and are erased at account boundaries.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _service;
    private readonly ISessionService _session;
    private readonly IAdminService _admin;
    private readonly LatestRequestTracker _roomLoads = new();
    private readonly LatestRequestTracker _messageLoads = new();
    private readonly LatestRequestTracker _candidateLoads = new();
    private readonly LatestRequestTracker _mutations = new();
    private readonly Dictionary<int, string> _drafts = [];
    private readonly Dictionary<int, PostChatMessageRequest> _pending = [];
    private CancellationTokenSource _visibility = new();
    private readonly List<Task> _finishingLoops = [];
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private Task _backgroundTask = Task.CompletedTask;
    private long _sequence;
    private long _oldestSequence;
    private int _accountId;
    private int _agencyId;
    private bool _active;
    private bool _changingSelection;
    private bool _suspended;
    private int _accountEpoch;
    private long? _editingRevision;
    private bool _loadingRoomDetails;

    public ChatViewModel(IChatService service, ISessionService session, IAdminService admin)
    {
        _service = service;
        _session = session;
        _admin = admin;
    }

    public ObservableCollection<ChatRoomItem> Rooms { get; } = [];
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];
    public ObservableCollection<ChatMemberDto> Members { get; } = [];
    public ObservableCollection<ChatCandidateDto> MemberCandidates { get; } = [];
    public ObservableCollection<ChatCandidateItem> NewRoomCandidates { get; } = [];
    public ObservableCollection<ChatConsumerChoice> Consumers { get; } = [];
    public Task SelectionLoadTask { get; private set; } = Task.CompletedTask;
    public Task CandidateLoadTask { get; private set; } = Task.CompletedTask;
    public Task BackgroundTask => _backgroundTask;
    public bool IsAvailableHere => _service.IsAvailableHere && _session.CurrentUser is { } user && ChatAccess.IsEligible(user.ToAgencyActor());
    public bool IsAdministrator => _session.CurrentUser?.HasAdminPermissions == true;
    public bool CanManageSelectedRoom => IsAdministrator && SelectedRoom is not null;
    public bool CanRedact => SelectedRoom is not null && (_session.CurrentUser?.HasSupervisorPermissions == true || IsAdministrator);
    public bool CanCompose => IsEnabled && _active && !IsBrowsingHistory && SelectedRoom?.Room.IsArchived == false;
    public bool HasPendingSend => SelectedRoom is { } room && _pending.ContainsKey(room.Id);
    public string SendLabel => HasPendingSend ? "Retry this message" : "Send";
    public string RoomNotice => SelectedRoom?.Room.PersonId is null ? "General coordination only — no client details." : $"{SelectedRoom.ConsumerIdentity} — approved members only.";
    public string RoomGuidance => SelectedRoom?.Room.PersonId is null ? ChatLimits.GeneralRoomNotice : "Only approved members with current access to this consumer can read this room. Newly added members see later messages only.";
    public string RecordNotice => ChatLimits.RecordNotice;
    public string NavigationLabel => UnreadRoomCount > 0 ? $"Team chat ({UnreadRoomCount} unread rooms)" : "Team chat";
    public event EventHandler? MessageSent;

    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isBrowsingHistory;
    [ObservableProperty] private bool hasOlderMessages;
    [ObservableProperty] private int unreadRoomCount;
    [ObservableProperty] private string status = "Open a room to view messages. Demo uses made-up information only.";
    [ObservableProperty] private string draft = string.Empty;
    [ObservableProperty] private ChatRoomItem? selectedRoom;
    [ObservableProperty] private ChatMessageItem? selectedMessage;
    [ObservableProperty] private ChatMemberDto? selectedMember;
    [ObservableProperty] private ChatCandidateDto? selectedCandidate;
    [ObservableProperty] private string roomName = string.Empty;
    [ObservableProperty] private string roomDescription = string.Empty;
    [ObservableProperty] private string redactionReason = string.Empty;
    [ObservableProperty] private bool showRoomEditor;
    [ObservableProperty] private string newRoomName = string.Empty;
    [ObservableProperty] private string newRoomDescription = string.Empty;
    [ObservableProperty] private ChatConsumerChoice? newRoomConsumer;

    partial void OnUnreadRoomCountChanged(int value) => OnPropertyChanged(nameof(NavigationLabel));
    partial void OnIsBrowsingHistoryChanged(bool value) => NotifyRoomProperties();
    partial void OnHasOlderMessagesChanged(bool value) => LoadOlderCommand.NotifyCanExecuteChanged();
    partial void OnIsEnabledChanged(bool value) => NotifyRoomProperties();
    partial void OnDraftChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnSelectedMessageChanged(ChatMessageItem? value) => RedactCommand.NotifyCanExecuteChanged();
    partial void OnSelectedMemberChanged(ChatMemberDto? value) => RemoveMemberCommand.NotifyCanExecuteChanged();
    partial void OnSelectedCandidateChanged(ChatCandidateDto? value) => AddMemberCommand.NotifyCanExecuteChanged();
    partial void OnRedactionReasonChanged(string value) => RedactCommand.NotifyCanExecuteChanged();
    partial void OnRoomNameChanged(string value) => CaptureRoomEditRevision();
    partial void OnRoomDescriptionChanged(string value) => CaptureRoomEditRevision();
    private void CaptureRoomEditRevision()
    {
        if (!_loadingRoomDetails && SelectedRoom is { } room) _editingRevision ??= room.Room.Revision;
    }
    private void ApplyRoomDetails(ChatRoomDto? room)
    {
        _loadingRoomDetails = true;
        RoomName = room?.Name ?? string.Empty;
        RoomDescription = room?.Description ?? string.Empty;
        _loadingRoomDetails = false;
        _editingRevision = null;
    }
    partial void OnNewRoomConsumerChanged(ChatConsumerChoice? value)
    {
        NewRoomCandidates.Clear();
        CandidateLoadTask = LoadNewRoomCandidatesAsync();
    }

    partial void OnSelectedRoomChanging(ChatRoomItem? value)
    {
        if (SelectedRoom is { } previous && !_changingSelection)
            _drafts[previous.Id] = Draft;
    }

    partial void OnSelectedRoomChanged(ChatRoomItem? value)
    {
        _messageLoads.Invalidate();
        ClearRoomContent();
        Draft = value is not null && _drafts.TryGetValue(value.Id, out var saved) ? saved : string.Empty;
        ApplyRoomDetails(value?.Room);
        NotifyRoomProperties();
        SelectionLoadTask = LoadSelectedRoomAsync();
    }

    private void NotifyRoomProperties()
    {
        OnPropertyChanged(nameof(CanManageSelectedRoom));
        OnPropertyChanged(nameof(CanRedact));
        OnPropertyChanged(nameof(CanCompose));
        OnPropertyChanged(nameof(HasPendingSend));
        OnPropertyChanged(nameof(SendLabel));
        OnPropertyChanged(nameof(RoomNotice));
        OnPropertyChanged(nameof(RoomGuidance));
        SendCommand.NotifyCanExecuteChanged();
        UpdateRoomCommand.NotifyCanExecuteChanged();
        ReloadRoomDetailsCommand.NotifyCanExecuteChanged();
        ArchiveRoomCommand.NotifyCanExecuteChanged();
        AddMemberCommand.NotifyCanExecuteChanged();
        RemoveMemberCommand.NotifyCanExecuteChanged();
        RedactCommand.NotifyCanExecuteChanged();
        MarkShownMessagesSeenCommand.NotifyCanExecuteChanged();
        NewRoomCommand.NotifyCanExecuteChanged();
        CreateRoomCommand.NotifyCanExecuteChanged();
        LoadOlderCommand.NotifyCanExecuteChanged();
        ReturnToLatestCommand.NotifyCanExecuteChanged();
        LeaveRoomCommand.NotifyCanExecuteChanged();
    }

    private bool SameAccount() => !_suspended && _session.CurrentUser?.Id == _accountId && _session.CurrentUser?.AgencyId == _agencyId;
    private bool CanPublish(int request, LatestRequestTracker tracker, CancellationToken token) =>
        _active && !token.IsCancellationRequested && SameAccount() && tracker.IsCurrent(request);

    /// <summary>Called before credential replacement and on session expiry, even when chat is hidden.</summary>
    public void SuspendAndClear()
    {
        _accountEpoch++;
        _mutations.Invalidate();
        IsBusy = false;
        _suspended = true;
        SetSurfaceState(false, true);
        _changingSelection = true;
        SelectedRoom = null;
        _changingSelection = false;
        Rooms.Clear();
        Consumers.Clear();
        _drafts.Clear();
        _pending.Clear();
        Draft = NewRoomName = NewRoomDescription = string.Empty;
        NewRoomConsumer = null;
        ShowRoomEditor = false;
        IsEnabled = false;
        UnreadRoomCount = 0;
        Status = "Sign in to use team chat.";
    }

    public void ResumeAccount()
    {
        SuspendAndClear();
        _accountId = _session.CurrentUser?.Id ?? 0;
        _agencyId = _session.CurrentUser?.AgencyId ?? 0;
        _suspended = false;
        OnPropertyChanged(nameof(IsAvailableHere));
        OnPropertyChanged(nameof(IsAdministrator));
    }

    /// <summary>The shell calls this synchronously, before hidden content can accept a late response.</summary>
    public void SetSurfaceState(bool visible, bool obscured)
    {
        var active = visible && !obscured && !_suspended && IsAvailableHere;
        if (_active == active) return;
        _active = active;
        _visibility.Cancel();
        _roomLoads.Invalidate();
        _messageLoads.Invalidate();
        _candidateLoads.Invalidate();
        ClearRoomContent();
        MemberCandidates.Clear();
        NewRoomCandidates.Clear();
        Consumers.Clear();
        NotifyRoomProperties();
        if (!active) return;
        _finishingLoops.RemoveAll(task => task.IsCompleted);
        _finishingLoops.Add(_backgroundTask);
        _visibility = new CancellationTokenSource();
        _backgroundTask = RunVisibleAsync(_visibility.Token);
    }

    private void ClearRoomContent()
    {
        Messages.Clear();
        Members.Clear();
        MemberCandidates.Clear();
        SelectedMessage = null;
        SelectedMember = null;
        SelectedCandidate = null;
        RedactionReason = string.Empty;
        _sequence = 0;
        _oldestSequence = 0;
        IsBrowsingHistory = false;
        HasOlderMessages = false;
    }

    private async Task RunVisibleAsync(CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var signals = ListenSafelyAsync(lifetime.Token);
        try
        {
            await RefreshAsync();
            while (!token.IsCancellationRequested && SameAccount())
            {
                await _refreshSignal.WaitAsync(TimeSpan.FromSeconds(30), token);
                if (!token.IsCancellationRequested) await RefreshAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_active && SameAccount()) Status = "Chat could not refresh. Open it again to retry."; }
        finally { lifetime.Cancel(); await signals; }
    }

    private async Task ListenSafelyAsync(CancellationToken token)
    {
        try
        {
            await _service.ListenForChangesAsync(() =>
            {
                try { _refreshSignal.Release(); } catch (SemaphoreFullException) { }
            }, token);
        }
        catch (Exception) { /* Polling continues; this hint never carries content. */ }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_active || !SameAccount()) return;
        var token = _visibility.Token;
        var request = _roomLoads.Begin();
        try
        {
            var availability = await _service.GetAvailabilityAsync(token);
            if (!CanPublish(request, _roomLoads, token)) return;
            IsEnabled = availability.Enabled;
            if (!IsEnabled)
            {
                Rooms.Clear(); SelectedRoom = null;
                _drafts.Clear(); _pending.Clear(); Draft = string.Empty;
                Status = availability.Explanation; return;
            }
            var rooms = await _service.GetRoomsAsync(token);
            if (!CanPublish(request, _roomLoads, token)) return;
            var allowed = rooms.Where(room => room.MembershipId is > 0).Take(ChatLimits.MaxRoomsPerUser).ToDictionary(room => room.Id);
            foreach (var old in Rooms.Where(room => !allowed.ContainsKey(room.Id)).ToArray())
            {
                if (SelectedRoom?.Id == old.Id) SelectedRoom = null;
                Rooms.Remove(old); _drafts.Remove(old.Id); _pending.Remove(old.Id);
            }
            foreach (var room in allowed.Values)
            {
                var item = Rooms.FirstOrDefault(current => current.Id == room.Id);
                if (item is null) Rooms.Add(new(room));
                else
                {
                    if (item.Room.MembershipId != room.MembershipId) ForgetMembershipContent(item);
                    item.Update(room);
                    if (SelectedRoom == item && _editingRevision is null) ApplyRoomDetails(room);
                }
            }
            UnreadRoomCount = Rooms.Count(room => room.Room.UnreadCount > 0);
            NotifyRoomProperties();
            Status = Rooms.Count == 0 ? "No rooms yet. An administrator can create a room and invite members." : "Connected. Messages refresh automatically while this workspace is visible.";
            if (SelectedRoom is not null && !IsBusy) await LoadSelectedRoomAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (CanPublish(request, _roomLoads, token)) HandleFailure(exception);
        }
    }

    private void ForgetMembershipContent(ChatRoomItem room)
    {
        _drafts.Remove(room.Id);
        _pending.Remove(room.Id);
        if (SelectedRoom != room) return;
        _messageLoads.Invalidate();
        _mutations.Invalidate();
        IsBusy = false;
        ClearRoomContent();
        Draft = string.Empty;
        ApplyRoomDetails(null);
        NotifyRoomProperties();
    }

    private bool PageBelongsToCurrentMembership(ChatPageDto page, ChatRoomItem room)
    {
        if (page.MembershipId is > 0 && page.MembershipId == room.Room.MembershipId) return true;
        // The page may be newer than the last room-list snapshot, or a delayed response
        // from an earlier membership. Do not merge either with the current cache.
        ForgetMembershipContent(room);
        SelectedRoom = null;
        _drafts.Remove(room.Id);
        Rooms.Remove(room);
        UnreadRoomCount = Rooms.Count(item => item.Room.UnreadCount > 0);
        Status = "Your access to this room changed. Refresh the room list and choose the room again.";
        return false;
    }

    private async Task LoadSelectedRoomAsync()
    {
        if (!_active || !SameAccount() || !IsEnabled || SelectedRoom is not { } selected) return;
        var token = _visibility.Token;
        var request = _messageLoads.Begin();
        try
        {
            var members = await _service.GetMembersAsync(selected.Id, token);
            if (!CanPublish(request, _messageLoads, token) || SelectedRoom != selected) return;
            Members.Clear(); foreach (var member in members.Take(ChatLimits.MaxMembers)) Members.Add(member);
            if (_sequence == 0)
            {
                var history = await _service.GetHistoryAsync(selected.Id, long.MaxValue, token);
                if (!CanPublish(request, _messageLoads, token) || SelectedRoom != selected) return;
                if (!PageBelongsToCurrentMembership(history, selected)) return;
                ApplyHistoryPage(history, selected.Id);
                // A history snapshot carries the room's committed change high-water mark.
                // Its NextSequence belongs to backward pagination, never the live feed.
                _sequence = history.RoomRevision;
                selected.Update(selected.Room with { Revision = history.RoomRevision });
            }
            // Pages are applied in durable sequence order; content rows are deduplicated by identity.
            // Older history has its own cursor and navigation; it never resets the live cursor.
            for (var pageNumber = 0; pageNumber < 20; pageNumber++)
            {
                var page = await _service.GetMessagesAsync(selected.Id, _sequence, token);
                if (!CanPublish(request, _messageLoads, token) || SelectedRoom != selected) return;
                if (!PageBelongsToCurrentMembership(page, selected)) return;
                if (page.NextSequence < _sequence) throw new InvalidOperationException();
                foreach (var change in page.Changes.OrderBy(change => change.Sequence))
                {
                    if (change.Message.RoomId != selected.Id) throw new InvalidOperationException();
                    var prior = Messages.FirstOrDefault(message => message.Id == change.Message.Id);
                    if (prior is not null) Messages[Messages.IndexOf(prior)] = new(change.Message);
                    else if (change.Kind == "message" && !IsBrowsingHistory) Messages.Add(new(change.Message));
                }
                while (Messages.Count > 300) { Messages.RemoveAt(0); HasOlderMessages = true; }
                if (!IsBrowsingHistory && Messages.Count > 0) _oldestSequence = Messages.Min(message => message.Message.Sequence);
                var advanced = page.NextSequence > _sequence;
                _sequence = page.NextSequence;
                selected.Update(selected.Room with { Revision = page.RoomRevision });
                if (!page.HasMore) break;
                if (!advanced) throw new InvalidOperationException();
                if (pageNumber == 19) Status = "More history remains. Refresh to continue loading it.";
            }
            if (IsAdministrator)
            {
                var candidates = await _service.GetCandidatesAsync(selected.Room.PersonId, token);
                if (!CanPublish(request, _messageLoads, token) || SelectedRoom != selected) return;
                MemberCandidates.Clear();
                foreach (var candidate in candidates.Where(candidate => Members.All(member => member.UserId != candidate.UserId)).Take(ChatLimits.MaxMembers)) MemberCandidates.Add(candidate);
            }
            NotifyRoomProperties();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (CanPublish(request, _messageLoads, token) && SelectedRoom == selected) HandleFailure(exception);
        }
    }

    private bool CanSend() => CanCompose && !IsBusy && (HasPendingSend || !string.IsNullOrWhiteSpace(Draft));

    private void ApplyHistoryPage(ChatPageDto page, int roomId)
    {
        if (page.Changes.Any(change => change.Message.RoomId != roomId)) throw new InvalidOperationException();
        Messages.Clear();
        foreach (var message in page.Changes.Select(change => change.Message).DistinctBy(message => message.Id).OrderBy(message => message.Sequence).Take(ChatLimits.MaxPageSize)) Messages.Add(new(message));
        _oldestSequence = page.NextSequence;
        HasOlderMessages = page.HasMore;
    }

    private bool CanLoadOlder() => _active && IsEnabled && SelectedRoom is not null && HasOlderMessages && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanLoadOlder))]
    private async Task LoadOlderAsync()
    {
        if (!CanLoadOlder() || SelectedRoom is not { } room) return;
        var token = _visibility.Token;
        var request = _messageLoads.Begin();
        try
        {
            var history = await _service.GetHistoryAsync(room.Id, _oldestSequence, token);
            if (!CanPublish(request, _messageLoads, token) || SelectedRoom != room) return;
            if (!PageBelongsToCurrentMembership(history, room)) return;
            ApplyHistoryPage(history, room.Id);
            IsBrowsingHistory = true;
            Status = "Viewing older messages. Return to latest to write a new message.";
        }
        catch (Exception exception) { if (CanPublish(request, _messageLoads, token)) HandleFailure(exception); }
    }

    private bool CanReturnToLatest() => _active && IsEnabled && SelectedRoom is not null && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanReturnToLatest))]
    private async Task ReturnToLatestAsync()
    {
        if (!CanReturnToLatest()) return;
        ClearRoomContent();
        await LoadSelectedRoomAsync();
    }

    private bool CanLeaveRoom() => _active && IsEnabled && SelectedRoom is not null && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanLeaveRoom))]
    private Task LeaveRoomAsync() => RunRoomMutationAsync(async (room, token) => await _service.RemoveMemberAsync(room.Id, _accountId, room.Room.Revision, token));

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!CanSend() || SelectedRoom is not { } room) return;
        var token = _visibility.Token;
        var account = _accountId;
        var epoch = _accountEpoch;
        var membership = room.Room.MembershipId;
        var mutation = _mutations.Begin();
        var body = Draft.Trim();
        if (body.Length > ChatLimits.MaxBodyLength) { Status = "Keep messages to 4,000 characters."; return; }
        var pending = _pending.TryGetValue(room.Id, out var existing) ? existing : new PostChatMessageRequest(room.Room.Revision, Guid.NewGuid(), body);
        _pending[room.Id] = pending;
        _drafts[room.Id] = pending.Body;
        Draft = pending.Body;
        IsBusy = true; NotifyRoomProperties();
        try
        {
            await _service.PostMessageAsync(room.Id, pending, token);
            if (!SameAccount() || account != _accountId || epoch != _accountEpoch || room.Room.MembershipId != membership || !_mutations.IsCurrent(mutation)) return;
            _pending.Remove(room.Id); _drafts.Remove(room.Id);
            if (SelectedRoom == room) Draft = string.Empty;
            if (_active && SelectedRoom == room && !token.IsCancellationRequested)
            {
                Draft = string.Empty;
                Status = "Message sent.";
                await LoadSelectedRoomAsync();
                MessageSent?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            if (!SameAccount() || account != _accountId || epoch != _accountEpoch || room.Room.MembershipId != membership || !_mutations.IsCurrent(mutation)) return;
            // Definitive client errors reject this request. A timeout, 5xx or cancellation
            // cannot tell us whether it committed: preserve both identifier AND body.
            if (exception is CloudApiException api && (int)api.StatusCode is >= 400 and < 500 && api.StatusCode is not HttpStatusCode.RequestTimeout)
                _pending.Remove(room.Id);
            if (_active && SelectedRoom == room && !token.IsCancellationRequested)
            {
                if (_pending.ContainsKey(room.Id)) Status = "Delivery is uncertain. Retry this same message to check safely; do not send a second copy.";
                else
                {
                    HandleFailure(exception);
                    var explanation = Status;
                    await RefreshAsync();
                    if (_active && SameAccount() && !token.IsCancellationRequested) Status = explanation;
                }
            }
        }
        finally { if (_mutations.IsCurrent(mutation)) { IsBusy = false; NotifyRoomProperties(); } }
    }

    // This is an explicit user acknowledgment, never a side effect of receiving a page.
    private bool CanMarkSeen() => _active && IsEnabled && !IsBrowsingHistory && SelectedRoom is not null && Messages.Count > 0 && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanMarkSeen))]
    private Task MarkShownMessagesSeenAsync() => RunRoomMutationAsync(async (room, token) => await _service.MarkSeenAsync(room.Id, _sequence, token));

    private bool CanAdminister() => _active && IsEnabled && CanManageSelectedRoom && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanAdminister))]
    private Task UpdateRoomAsync()
    {
        var expected = _editingRevision ?? SelectedRoom?.Room.Revision ?? 0;
        var name = RoomName.Trim();
        var description = RoomDescription.Trim();
        var epoch = _accountEpoch;
        var membership = SelectedRoom?.Room.MembershipId;
        return RunRoomMutationAsync(async (room, token) =>
        {
            var result = await _service.UpdateRoomAsync(room.Id, new(expected, name, description), token);
            if (!token.IsCancellationRequested && epoch == _accountEpoch && SameAccount() && SelectedRoom == room && room.Room.MembershipId == membership) ApplyRoomDetails(result);
            return result;
        });
    }

    [RelayCommand(CanExecute = nameof(CanAdminister))]
    private async Task ReloadRoomDetailsAsync()
    {
        if (!CanAdminister() || SelectedRoom is not { } room) return;
        var token = _visibility.Token;
        var epoch = _accountEpoch;
        await RefreshAsync();
        if (!token.IsCancellationRequested && epoch == _accountEpoch && SameAccount() && SelectedRoom == room) ApplyRoomDetails(room.Room);
    }
    [RelayCommand(CanExecute = nameof(CanAdminister))]
    private Task ArchiveRoomAsync() => RunRoomMutationAsync(async (room, token) => await _service.ArchiveRoomAsync(room.Id, new(room.Room.Revision), token));
    private bool CanAddMember() => CanAdminister() && SelectedCandidate is not null;
    [RelayCommand(CanExecute = nameof(CanAddMember))]
    private Task AddMemberAsync()
    {
        var candidate = SelectedCandidate;
        return candidate is null ? Task.CompletedTask : RunRoomMutationAsync(async (room, token) => await _service.AddMemberAsync(room.Id, new(room.Room.Revision, candidate.UserId), token));
    }
    private bool CanRemoveMember() => CanAdminister() && SelectedMember is not null;
    [RelayCommand(CanExecute = nameof(CanRemoveMember))]
    private Task RemoveMemberAsync()
    {
        var member = SelectedMember;
        return member is null ? Task.CompletedTask : RunRoomMutationAsync(async (room, token) => await _service.RemoveMemberAsync(room.Id, member.UserId, room.Room.Revision, token));
    }
    private bool CanRedactSelected() => _active && CanRedact && !IsBusy && SelectedMessage?.Message.RedactedAtUtc is null && SelectedMessage is not null && RedactionReason.Trim().Length is >= 10 and <= 240;
    [RelayCommand(CanExecute = nameof(CanRedactSelected))]
    private async Task RedactAsync()
    {
        if (!CanRedactSelected() || SelectedMessage is not { } message) return;
        var reason = RedactionReason.Trim();
        var epoch = _accountEpoch;
        await RunRoomMutationAsync(async (room, token) =>
        {
            await _service.RedactMessageAsync(message.Id, new(room.Room.Revision, reason), token);
            return null;
        });
        if (epoch == _accountEpoch && SelectedMessage?.Id == message.Id) RedactionReason = string.Empty;
    }

    private async Task RunRoomMutationAsync(Func<ChatRoomItem, CancellationToken, Task<ChatRoomDto?>> operation)
    {
        if (!_active || !SameAccount() || IsBusy || SelectedRoom is not { } room) return;
        var token = _visibility.Token;
        var mutation = _mutations.Begin();
        IsBusy = true; NotifyRoomProperties();
        try
        {
            var updated = await operation(room, token);
            if (!_active || token.IsCancellationRequested || !SameAccount() || SelectedRoom != room || !_mutations.IsCurrent(mutation)) return;
            if (updated is not null) room.Update(updated);
            Status = "Room updated.";
            IsBusy = false;
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_active && !token.IsCancellationRequested && SameAccount() && SelectedRoom == room && _mutations.IsCurrent(mutation))
            {
                HandleFailure(exception);
                if (exception is CloudApiException { StatusCode: HttpStatusCode.Conflict })
                {
                    var explanation = Status;
                    await RefreshAsync();
                    if (_active && SameAccount() && !token.IsCancellationRequested) Status = explanation;
                }
            }
        }
        finally { if (_mutations.IsCurrent(mutation)) { IsBusy = false; NotifyRoomProperties(); } }
    }

    private bool CanCreateRoom() => _active && IsEnabled && IsAdministrator && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanCreateRoom))]
    private async Task NewRoomAsync()
    {
        ShowRoomEditor = true;
        var request = _candidateLoads.Begin();
        var token = _visibility.Token;
        try
        {
            var people = await _admin.GetPeopleAsync(token);
            if (!CanPublish(request, _candidateLoads, token)) return;
            Consumers.Clear();
            Consumers.Add(new(null, "General coordination — no client details"));
            foreach (var person in people.OrderBy(person => person.DisplayName).Take(1000)) Consumers.Add(new(person.PersonId, $"{person.DisplayName} · record {person.PersonId}"));
            NewRoomConsumer = Consumers[0];
            await CandidateLoadTask;
        }
        catch (Exception exception) { if (CanPublish(request, _candidateLoads, token)) HandleFailure(exception); }
    }

    private async Task LoadNewRoomCandidatesAsync()
    {
        if (!CanCreateRoom() || !ShowRoomEditor) return;
        var request = _candidateLoads.Begin();
        var token = _visibility.Token;
        try
        {
            var candidates = await _service.GetCandidatesAsync(NewRoomConsumer?.Id, token);
            if (!CanPublish(request, _candidateLoads, token)) return;
            NewRoomCandidates.Clear();
            foreach (var candidate in candidates.Take(ChatLimits.MaxMembers)) NewRoomCandidates.Add(new(candidate) { IsSelected = candidate.UserId == _accountId });
        }
        catch (Exception exception) { if (CanPublish(request, _candidateLoads, token)) HandleFailure(exception); }
    }

    [RelayCommand(CanExecute = nameof(CanCreateRoom))]
    private async Task CreateRoomAsync()
    {
        if (!CanCreateRoom() || string.IsNullOrWhiteSpace(NewRoomName) || NewRoomConsumer is null) return;
        var token = _visibility.Token;
        var mutation = _mutations.Begin();
        IsBusy = true; NotifyRoomProperties();
        try
        {
            var room = await _service.CreateRoomAsync(new(NewRoomName.Trim(), NewRoomDescription.Trim(), NewRoomConsumer.Id, NewRoomCandidates.Where(member => member.IsSelected).Select(member => member.UserId).ToArray()), token);
            if (!_active || token.IsCancellationRequested || !SameAccount()) return;
            ShowRoomEditor = false; NewRoomName = NewRoomDescription = string.Empty;
            await RefreshAsync();
            SelectedRoom = Rooms.FirstOrDefault(candidate => candidate.Id == room.Id);
            await SelectionLoadTask;
        }
        catch (Exception exception) { if (_active && !token.IsCancellationRequested && SameAccount()) HandleFailure(exception); }
        finally { if (_mutations.IsCurrent(mutation)) { IsBusy = false; NotifyRoomProperties(); } }
    }

    [RelayCommand] private void CloseRoomEditor() { ShowRoomEditor = false; NewRoomCandidates.Clear(); }

    public async Task StopAsync()
    {
        SuspendAndClear();
        await Task.WhenAll(_finishingLoops.Append(_backgroundTask));
    }

    private void HandleFailure(Exception exception)
    {
        if (exception is CloudApiException { StatusCode: HttpStatusCode.Unauthorized }) { SuspendAndClear(); return; }
        if (exception is CloudApiException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound })
        {
            var roomId = SelectedRoom?.Id;
            SelectedRoom = null;
            if (roomId is int id)
            {
                _drafts.Remove(id); _pending.Remove(id);
                var denied = Rooms.FirstOrDefault(room => room.Id == id);
                if (denied is not null) Rooms.Remove(denied);
            }
            else { Rooms.Clear(); _drafts.Clear(); _pending.Clear(); }
            UnreadRoomCount = Rooms.Count(room => room.Room.UnreadCount > 0);
            Status = "This room is no longer available to your account.";
            return;
        }
        Status = exception is CloudApiException { StatusCode: HttpStatusCode.Conflict }
            ? "The room changed while you were working. Review its current members and details, then try again."
            : exception is CloudApiException { StatusCode: HttpStatusCode.TooManyRequests }
                ? "Please wait before trying chat again."
                : exception is CloudApiException { StatusCode: HttpStatusCode.BadRequest } api ? api.Message
                : "Chat could not complete that action. Check the connection, refresh the room, and review before retrying.";
    }
}
