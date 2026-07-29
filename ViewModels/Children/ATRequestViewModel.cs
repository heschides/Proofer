using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    // The AT Requests sub-tab: a queue of authorized-payment requests for the
    // logged-in CM's caseload, plus (in later slices) a live-preview editor.
    //
    // Save-persists model: creating a request builds an in-memory ATRequest via
    // CreateForClient; it only reaches the database on Save. No ghost drafts.
    //
    // BUILD STATE: slice 1a — queue + client picker + new-request creation only.
    // Editing surface (1b), item grid (1c), status/save/export (1d) land next.
    public partial class ATRequestViewModel : ObservableObject
    {
        private readonly IATRequestService _atRequestService;
        private readonly IPersonService _personService;
        private readonly ISessionService _sessionService;

        public ATRequestViewModel(
            IATRequestService atRequestService,
            IPersonService personService,
            ISessionService sessionService)
        {
            _atRequestService = atRequestService;
            _personService = personService;
            _sessionService = sessionService;
        }

        // The queue — metadata-only rows (no blob), from GetAllForUserAsync.
        public ObservableCollection<ATRequestListItem> Requests { get; } = [];

        // Clients the CM can create a request for — populated on load.
        public ObservableCollection<Person> Clients { get; } = [];

        // The client chosen for a NEW request. Selecting drives CreateForClient.
        [ObservableProperty] private Person? selectedClient;

        // The editor for the request currently open, or null = queue-only view,
        // no detail pane showing. Holds the in-memory ATRequest until saved. The
        // detail pane binds to this; the observable editor drives the live preview.
        [ObservableProperty] private ATRequestEditorViewModel? currentEditor;

        // Drives the queue/editor toggle in the view: false = show the queue,
        // true = show the editor pane. Derived from CurrentEditor's presence.
        public bool IsEditing => CurrentEditor is not null;

        partial void OnCurrentEditorChanged(ATRequestEditorViewModel? value)
        {
            OnPropertyChanged(nameof(IsEditing));
        }

        // Close the editor, return to the queue. Discards an unsaved in-memory
        // request (nothing was persisted). Save/confirm-on-dirty is a later concern.
        [RelayCommand]
        private void CloseEditor() => CurrentEditor = null;

        // Load the queue and the client list. Called by the host when the AT tab
        // is first shown (wiring added when the host gains its AT nav in 1d/DI).
        public async Task InitializeAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
                return;

            Requests.Clear();
            foreach (var row in await _atRequestService.GetAllForUserAsync(userId.Value))
                Requests.Add(row);

            Clients.Clear();
            foreach (var person in await _personService.GetAllPeopleAsync(userId.Value))
                Clients.Add(person);
        }

        // Start a new, unsaved request for the selected client. Builds the
        // in-memory ATRequest only — persistence waits for Save (1d). Guards on
        // no client selected and no current user.
        [RelayCommand]
        private void NewRequest()
        {
            var caseManager = _sessionService.CurrentUser;
            if (SelectedClient is null || caseManager is null)
                return;

            var request = ATRequest.CreateForClient(SelectedClient, caseManager);
            CurrentEditor = new ATRequestEditorViewModel(request);
        }

        // Reload the queue after a save/status change so row metadata (total,
        // status, HasSnapshot) reflects the latest. Used by later slices.
        public async Task RefreshQueueAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
                return;

            Requests.Clear();
            foreach (var row in await _atRequestService.GetAllForUserAsync(userId.Value))
                Requests.Add(row);
        }
    }
}