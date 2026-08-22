using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Carika.Services;
using Sati.Contracts.V1;

namespace Carika.ViewModels;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly Window _window;
    private readonly EncryptedDraftStore _drafts = new();
    private readonly LocalWhisperTranscriber _transcriber = new();
    private CarikaApiClient? _api;
    private UserProfileDto? _user;
    private PersonItem? _selectedPerson;
    private string _status = "Carika connects only through the Sati API.";
    private string _narrative = string.Empty;
    private bool _isSignedIn;
    private int _draftLoadVersion;
    private CaseNoteEntryOption _selectedStatus = CaseNoteEntryOptions.CaseManagerStatuses
        .Single(option => option.Value == "Pending");
    private CaseNoteEntryOption _selectedNoteType = CaseNoteEntryOptions.NoteTypes
        .Single(option => option.Value == "Other");
    private CaseNoteEntryOption? _selectedFormType;

    public MainWindowViewModel(Window window)
    {
        _window = window;
        SignInCommand = new AsyncCommand(SignInAsync);
        SaveDraftCommand = new AsyncCommand(SaveDraftAsync);
        SaveNoteCommand = new AsyncCommand(SaveNoteAsync);
        TranscribeCommand = new AsyncCommand(TranscribeAsync);
    }

    public string ApiAddress { get; set; } = "https://sati-demo-api-satilogica.azurewebsites.net";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsSignedIn { get => _isSignedIn; private set => Set(ref _isSignedIn, value); }
    public ObservableCollection<PersonItem> People { get; } = [];
    public PersonItem? SelectedPerson
    {
        get => _selectedPerson;
        set
        {
            if (!Set(ref _selectedPerson, value)) return;
            var version = Interlocked.Increment(ref _draftLoadVersion);
            Narrative = string.Empty;
            if (value is not null) _ = LoadDraftAsync(value.Value.Id, version);
        }
    }
    public DateTimeOffset? EventDate { get; set; } = DateTimeOffset.Now;
    public decimal? Minutes { get; set; } = 15;
    public string Narrative { get => _narrative; set => Set(ref _narrative, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public IReadOnlyList<CaseNoteEntryOption> StatusOptions => CaseNoteEntryOptions.CaseManagerStatuses;
    public IReadOnlyList<CaseNoteEntryOption> NoteTypeOptions => CaseNoteEntryOptions.NoteTypes;
    public IReadOnlyList<CaseNoteEntryOption> FormTypeOptions => CaseNoteEntryOptions.FormTypes;
    public CaseNoteEntryOption SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (!Set(ref _selectedStatus, value)) return;
            OnPropertyChanged(nameof(StatusGuidance));
        }
    }
    public CaseNoteEntryOption SelectedNoteType
    {
        get => _selectedNoteType;
        set
        {
            if (!Set(ref _selectedNoteType, value)) return;
            if (!IsFormNote) SelectedFormType = null;
            OnPropertyChanged(nameof(IsFormNote));
            OnPropertyChanged(nameof(NoteTypeGuidance));
        }
    }
    public CaseNoteEntryOption? SelectedFormType
    {
        get => _selectedFormType;
        set => Set(ref _selectedFormType, value);
    }
    public bool IsFormNote => SelectedNoteType.Value == "Form";
    public string StatusGuidance => SelectedStatus.Guidance;
    public string NoteTypeGuidance => SelectedNoteType.Guidance;
    public ICommand SignInCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand TranscribeCommand { get; }

    private async Task SignInAsync()
    {
        try
        {
            Status = "Signing in…";
            _api?.Dispose();
            _api = new CarikaApiClient(new Uri(ApiAddress));
            var login = await _api.LoginAsync(Username, Password, CancellationToken.None);
            Password = string.Empty;
            OnPropertyChanged(nameof(Password));
            _user = login.User;
            var people = await _api.GetCaseloadAsync(CancellationToken.None);
            People.Clear();
            foreach (var person in people) People.Add(new PersonItem(person));
            IsSignedIn = true;
            SelectedPerson = People.FirstOrDefault();
            Status = $"Signed in as {login.User.DisplayName}.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task LoadDraftAsync(int personId, int version)
    {
        if (_user is null) return;
        try
        {
            var narrative = await _drafts.LoadAsync(_user.Id, personId, CancellationToken.None);
            if (version == _draftLoadVersion && SelectedPerson?.Value.Id == personId)
                Narrative = narrative;
        }
        catch (Exception ex)
        {
            if (version == _draftLoadVersion && SelectedPerson?.Value.Id == personId)
                Status = $"The encrypted draft could not be opened: {ex.Message}";
        }
    }

    private async Task SaveDraftAsync()
    {
        if (_user is null || SelectedPerson is null) { Status = "Select a client first."; return; }
        var personId = SelectedPerson.Value.Id;
        var narrative = Narrative;
        try
        {
            await _drafts.SaveAsync(_user.Id, personId, narrative, CancellationToken.None);
            if (SelectedPerson?.Value.Id == personId)
                Status = "Draft encrypted for this Windows user and selected client.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task SaveNoteAsync()
    {
        if (_api is null || _user is null || SelectedPerson is null) { Status = "Select a client first."; return; }
        if (string.IsNullOrWhiteSpace(Narrative)) { Status = "Enter a narrative before saving the note."; return; }
        if (SelectedNoteType is null || SelectedStatus is null) { Status = "Select a note type and status."; return; }
        if (IsFormNote && SelectedFormType is null) { Status = "Select the form documented by this note."; return; }
        var personId = SelectedPerson.Value.Id;
        var narrative = Narrative.Trim();
        try
        {
            var request = new SaveNoteRequest(
                narrative,
                EventDate?.LocalDateTime.Date,
                SelectedStatus.Value,
                (int?)Minutes,
                null,
                personId,
                SelectedFormType?.Value,
                SelectedNoteType.Value,
                null,
                null);
            var note = await _api.SaveNoteAsync(request, CancellationToken.None);
            _drafts.Delete(_user.Id, personId);
            if (SelectedPerson?.Value.Id == personId)
            {
                if (string.Equals(Narrative.Trim(), narrative, StringComparison.Ordinal))
                    Narrative = string.Empty;
                Status = SelectedStatus.Value == "Logged"
                    ? $"Note {note.Id} submitted to Sati for supervisor review."
                    : $"Note {note.Id} saved to Sati as {SelectedStatus.Label.ToLowerInvariant()}.";
            }
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task TranscribeAsync()
    {
        try
        {
            if (SelectedPerson is null) throw new InvalidOperationException("Select a client before adding a transcript.");
            if (!_transcriber.IsConfigured) throw new InvalidOperationException("Local transcription needs CARIKA_WHISPER_MODEL set to an approved local GGML model.");
            var personId = SelectedPerson.Value.Id;
            var sourceNarrative = Narrative;
            var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a temporary WAV recording", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] }]
            });
            if (files.Count == 0) return;
            Status = "Transcribing locally…";
            var transcript = await _transcriber.TranscribeAsync(files[0].Path.LocalPath, CancellationToken.None);
            if (SelectedPerson?.Value.Id != personId || !string.Equals(Narrative, sourceNarrative, StringComparison.Ordinal))
            {
                Status = "The selected client or narrative changed, so the transcript was not inserted.";
                return;
            }
            Narrative = string.IsNullOrWhiteSpace(Narrative) ? transcript : $"{Narrative.TrimEnd()} {transcript}";
            Status = "Local transcription added. Review it before saving; Carika does not retain the audio.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    internal sealed record PersonItem(PersonDto Value)
    {
        public string DisplayName => $"{Value.LastName}, {Value.FirstName}";
        public string Initials => string.Concat(
            Value.FirstName?.FirstOrDefault().ToString(),
            Value.LastName?.FirstOrDefault().ToString()).ToUpperInvariant();
        public string WaiverLabel => string.IsNullOrWhiteSpace(Value.Waiver)
            ? "Waiver not recorded"
            : Value.Waiver.Replace("Section", "Section ", StringComparison.Ordinal);
        public string ProfileSummary
        {
            get
            {
                var details = new List<string> { $"DOB {Value.BirthDate:d}" };
                if (!string.IsNullOrWhiteSpace(Value.PhoneNumber)) details.Add(Value.PhoneNumber);
                if (!string.IsNullOrWhiteSpace(Value.Address)) details.Add(Value.Address);
                return string.Join("  •  ", details);
            }
        }
    }
}

internal sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool _running;
    public bool CanExecute(object? parameter) => !_running;
    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); } finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CanExecuteChanged;
}
