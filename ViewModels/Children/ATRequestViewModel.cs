using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Reporting;
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
        private readonly ATRequestPdfExporter _pdfExporter;

        // Passthrough rate, loaded once in InitializeAsync and handed to each editor
        // VM (the entity's money math takes it as an argument). Defaults to 0 until
        // loaded: a NewRequest before load would show no passthrough, not crash. In
        // normal flow InitializeAsync runs on tab show, well before any NewRequest.
        private decimal _passthroughRate;
        private decimal _salesTaxRate;
        private int? _defaultPassthroughProviderId;
        private readonly List<Provider> _passthroughProviders = [];

        public ATRequestViewModel(
            IATRequestService atRequestService,
            IPersonService personService,
            IProviderService providerService,
            ISessionService sessionService,
            ISettingsService settingsService,
            ATRequestPdfExporter pdfExporter)
        {
            _atRequestService = atRequestService;
            _personService = personService;
            _providerService = providerService;
            _sessionService = sessionService;
            _settingsService = settingsService;
            _pdfExporter = pdfExporter;
        }

        // ---- Interaction requests (see ATRequestPublishEvents) ----
        public event EventHandler<ATRequestPublishConfirmationEventArgs>? PublishConfirmationRequested;
        public event EventHandler<ATRequestPdfReadyEventArgs>? PdfReady;
        public event EventHandler<ATRequestProblemEventArgs>? ProblemOccurred;

        /// <summary>
        /// Supplies a rasterized PNG of the rendered form for the proof-of-document
        /// snapshot. Set by the view, which is the only party that can rasterize a
        /// visual. A byte-producing delegate rather than a View reference, so this
        /// view model still knows nothing about WPF; null in tests and headless
        /// callers, where publication simply records no snapshot.
        /// </summary>
        public Func<byte[]?>? FormImageProvider { get; set; }

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
            _salesTaxRate = settings.SalesTaxRate;
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
                _salesTaxRate,
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

        // Open a saved request from the queue. Loads the full record (items
        // included, blob excluded) rather than reusing the list projection, which
        // carries only enough to draw a row.
        [RelayCommand]
        private async Task OpenRequestAsync(ATRequestListItem? row)
        {
            if (row is null)
                return;

            var request = await _atRequestService.GetByIdAsync(row.Id);
            if (request is null)
            {
                Raise("Request Not Found", "This AT request is no longer available. The queue has been refreshed.");
                await RefreshQueueAsync();
                return;
            }

            CurrentEditor = new ATRequestEditorViewModel(
                request,
                _passthroughRate,
                _salesTaxRate,
                _passthroughProviders,
                MatchProvider(request));
        }

        // Persist without publishing. A draft can be saved and returned to; nothing
        // about it is asserted to anyone yet.
        [RelayCommand]
        private async Task SaveAsync()
        {
            var editor = CurrentEditor;
            if (editor is null)
                return;

            if (!await PersistAsync(editor))
                return;

            await RefreshQueueAsync();
        }

        // Publish: the attestation, the status transition, the lock, and the PDF.
        //
        // ORDER MATTERS. The attestation is stamped in memory, then persisted, and
        // only then is the PDF produced. Rendering first would risk handing the
        // case manager a signed-looking document for a publication that failed to
        // save.
        [RelayCommand]
        private async Task PublishAsync()
        {
            var editor = CurrentEditor;
            var caseManager = _sessionService.CurrentUser;
            if (editor is null || caseManager is null)
                return;

            if (!editor.CanPublish)
            {
                Raise("Cannot Publish Yet", editor.PublishBlockerSummary);
                return;
            }

            // Explicit human act. If no view is listening, Confirmed stays false
            // and nothing is published.
            var confirmation = new ATRequestPublishConfirmationEventArgs(
                editor.ClientName ?? "this client",
                AtRequestPublication.AttestationStatement,
                editor.TotalCost,
                editor.ScreenshotCount,
                editor.Items.Count);
            PublishConfirmationRequested?.Invoke(this, confirmation);
            if (!confirmation.Confirmed)
                return;

            var request = editor.Request;

            // Proof-of-document image of the form as published, captured before the
            // save so it travels with it. Best-effort: a failure to rasterize must
            // not cost the case manager the publication, since the PDF is
            // regenerable from the stored record either way.
            var snapshot = TryCaptureSnapshot();
            if (snapshot is not null)
                request.AttachSnapshot(snapshot);

            try
            {
                await _atRequestService.PublishAsync(request, caseManager);
            }
            catch (AtRequestLockedException ex)
            {
                Raise("Request Published", ex.Message);
                return;
            }
            catch (AtRequestConcurrencyException ex)
            {
                Raise("Request Changed", ex.Message);
                return;
            }

            editor.RaisePublicationChanged();
            await RefreshQueueAsync();
            ExportPdf(request);
        }

        // Re-export the PDF for a request already saved. Works for drafts too —
        // the exporter marks an unpublished copy as DRAFT on the page.
        [RelayCommand]
        private void ExportCurrentPdf()
        {
            if (CurrentEditor is not null)
                ExportPdf(CurrentEditor.Request);
        }

        // Reopen a published request for correction. Discards the attestation, so
        // the view confirms first — reusing the publish confirmation event would
        // show the wrong words, hence the plain problem/confirm split here: the
        // command is only reachable from a button the view already guards.
        [RelayCommand]
        private async Task ReopenAsync()
        {
            var editor = CurrentEditor;
            if (editor is null || !editor.IsPublished)
                return;

            try
            {
                await _atRequestService.ReopenAsync(editor.Request);
            }
            catch (AtRequestConcurrencyException ex)
            {
                Raise("Request Changed", ex.Message);
                return;
            }

            editor.RaisePublicationChanged();
            await RefreshQueueAsync();
        }

        // The single save path for both draft saves and publication. Returns false
        // when the caller should stop; the user has already been told why.
        private async Task<bool> PersistAsync(ATRequestEditorViewModel editor)
        {
            try
            {
                if (editor.Request.Id == 0)
                    await _atRequestService.AddAsync(editor.Request);
                else
                    await _atRequestService.UpdateAsync(editor.Request);
                return true;
            }
            catch (AtRequestLockedException ex)
            {
                Raise("Request Published", ex.Message);
                return false;
            }
            catch (AtRequestConcurrencyException ex)
            {
                Raise("Request Changed", ex.Message);
                return false;
            }
        }

        private void ExportPdf(ATRequest request)
        {
            var content = _pdfExporter.Generate(request, _passthroughRate, DateTime.UtcNow);
            PdfReady?.Invoke(this, new ATRequestPdfReadyEventArgs(
                content, ATRequestPdfExporter.SuggestedFileName(request)));
        }

        private byte[]? TryCaptureSnapshot()
        {
            try
            {
                return FormImageProvider?.Invoke();
            }
            catch (Exception)
            {
                // Snapshot capture is evidence, not the record. Losing it must not
                // abort a publication that is otherwise valid.
                return null;
            }
        }

        // Reconnect a saved request to the live provider row it was filed against,
        // matched on the frozen vendor name. Null when the provider was renamed or
        // removed — the request still renders from its own snapshot columns, which
        // is exactly why those columns exist.
        private Provider? MatchProvider(ATRequest request) =>
            _passthroughProviders.FirstOrDefault(provider =>
                string.Equals(provider.Name, request.VendorName, StringComparison.OrdinalIgnoreCase));

        private void Raise(string title, string message) =>
            ProblemOccurred?.Invoke(this, new ATRequestProblemEventArgs(title, message));

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
