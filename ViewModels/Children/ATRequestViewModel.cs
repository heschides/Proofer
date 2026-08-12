using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    // The AT Requests sub-tab: a queue of authorized-payment requests for the
    // logged-in CM's caseload, plus the live-preview editor.
    //
    // Save-persists model: creating a request builds an in-memory ATRequest via
    // CreateForClient; it only reaches the database on Save (slice 1d). No drafts.
    public partial class ATRequestViewModel : ObservableObject
    {
        private readonly IATRequestService _atRequestService;
        private readonly IPersonService _personService;
        private readonly IProviderService _providerService;
        private readonly ISessionService _sessionService;
        private readonly ISettingsService _settingsService;

        // Passthrough rate, loaded once in InitializeAsync and handed to each editor
        // VM (the entity's money math takes it as an argument). Defaults to 0 until
        // loaded: a NewRequest before load would show no passthrough, not crash. In
        // normal flow InitializeAsync runs on tab show, well before any NewRequest.
        private decimal _passthroughRate;
        private int? _defaultPassthroughProviderId;
        private readonly List<Provider> _passthroughProviders = [];

        public ATRequestViewModel(
            IATRequestService atRequestService,
            IPersonService personService,
            IProviderService providerService,
            ISessionService sessionService,
            ISettingsService settingsService)
        {
            _atRequestService = atRequestService;
            _personService = personService;
            _providerService = providerService;
            _sessionService = sessionService;
            _settingsService = settingsService;
        }

        public ObservableCollection<ATRequestListItem> Requests { get; } = [];
        public ObservableCollection<Person> Clients { get; } = [];

        [ObservableProperty] private Person? selectedClient;
        [ObservableProperty] private ATRequestEditorViewModel? currentEditor;

        public bool IsEditing => CurrentEditor is not null;

        partial void OnCurrentEditorChanged(ATRequestEditorViewModel? value)
        {
            OnPropertyChanged(nameof(IsEditing));
        }

        [RelayCommand]
        private void CloseEditor() => CurrentEditor = null;

        public async Task InitializeAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
                return;

            var settings = await _settingsService.LoadAsync();
            _passthroughRate = settings.PassthroughRate;
            _defaultPassthroughProviderId = settings.DefaultPassthroughProviderId;

            _passthroughProviders.Clear();
            _passthroughProviders.AddRange(await _providerService.GetPassthroughProvidersAsync());

            Requests.Clear();
            foreach (var row in await _atRequestService.GetAllForUserAsync(userId.Value))
                Requests.Add(row);

            Clients.Clear();
            foreach (var person in await _personService.GetAllPeopleAsync(userId.Value))
                Clients.Add(person);
        }

        [RelayCommand]
        private void NewRequest()
        {
            var caseManager = _sessionService.CurrentUser;
            if (SelectedClient is null || caseManager is null)
                return;

            var request = ATRequest.CreateForClient(SelectedClient, caseManager);
            var defaultProvider = ResolveDefaultProvider();
            CurrentEditor = new ATRequestEditorViewModel(
                request,
                _passthroughRate,
                _passthroughProviders,
                defaultProvider);
        }

        private Provider? ResolveDefaultProvider()
        {
            // The configured default is normally the seeded Maine AT Solutions
            // row. Keep the name fallback so a database whose Settings row has
            // not selected a default still behaves safely and predictably.
            return _passthroughProviders.FirstOrDefault(
                       provider => provider.Id == _defaultPassthroughProviderId)
                   ?? _passthroughProviders.FirstOrDefault(
                       provider => string.Equals(
                           provider.Name,
                           "Maine AT Solutions",
                           StringComparison.OrdinalIgnoreCase));
        }

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
