using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels
{
    public partial class ScratchpadHistoryViewModel : ObservableObject
    {
        private readonly IScratchpadService _scratchpadService;
        private readonly ISessionService _sessionService;
        private readonly List<Scratchpad> _allEntries = [];

        public ScratchpadHistoryViewModel(
            IScratchpadService scratchpadService,
            ISessionService sessionService)
        {
            _scratchpadService = scratchpadService;
            _sessionService = sessionService;
        }

        public event EventHandler? CommentSaved;

        [ObservableProperty] private DateTime? selectionStart;
        [ObservableProperty] private DateTime? selectionEnd;
        [ObservableProperty] private bool isEditorUnlocked;
        [ObservableProperty] private bool isSaving;
        [ObservableProperty] private string newComment = string.Empty;
        [ObservableProperty] private string? statusMessage;

        public ObservableCollection<Scratchpad> VisibleEntries { get; } = [];
        public DateTime LatestSelectableDate => DateTime.Today.AddDays(-1);
        public DateTime InitialDisplayDate { get; private set; } = DateTime.Today.AddDays(-1);
        public DateTime InitialSelectedDate { get; private set; } = DateTime.Today.AddDays(-1);

        public bool HasVisibleEntries => VisibleEntries.Count > 0;
        public bool IsSelectionEmpty => !HasVisibleEntries;
        public bool IsSingleDateSelection =>
            SelectionStart.HasValue && SelectionStart.Value.Date == SelectionEnd?.Date;
        public bool CanToggleEditor =>
            IsEditorUnlocked || (IsSingleDateSelection && VisibleEntries.Count == 1);
        public bool CanSaveComment =>
            IsEditorUnlocked && !IsSaving && !string.IsNullOrWhiteSpace(NewComment);

        public string SelectionSummary
        {
            get
            {
                if (!SelectionStart.HasValue)
                    return "Choose a date";

                if (SelectionStart.Value.Date == SelectionEnd?.Date)
                    return SelectionStart.Value.ToString("dddd, MMMM d, yyyy");

                return $"{SelectionStart.Value:MMM d, yyyy} – {SelectionEnd:MMM d, yyyy}";
            }
        }

        public string EntryCountSummary => VisibleEntries.Count switch
        {
            0 => "No scratchpad entries in this selection",
            1 => "1 scratchpad entry",
            _ => $"{VisibleEntries.Count} scratchpad entries"
        };

        public string EditorToolTip => IsEditorUnlocked
            ? "Lock retrospective comments"
            : CanToggleEditor
                ? "Unlock to add a clearly marked retrospective comment"
                : "Select one date with a scratchpad entry to add a comment";

        public async Task InitializeAsync()
        {
            var user = _sessionService.CurrentUser
                ?? throw new InvalidOperationException("A signed-in user is required to view scratchpad history.");

            _allEntries.Clear();
            _allEntries.AddRange(await _scratchpadService.GetHistoryAsync(user.Id));

            var initialDate = _allEntries.FirstOrDefault()?.Date.Date ?? LatestSelectableDate;
            InitialDisplayDate = initialDate;
            InitialSelectedDate = initialDate;
            SetSelectedDates([initialDate]);
        }

        public void SetSelectedDates(IEnumerable<DateTime> dates)
        {
            var selectedDates = dates
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            SelectionStart = selectedDates.FirstOrDefault();
            SelectionEnd = selectedDates.LastOrDefault();
            if (selectedDates.Count == 0)
            {
                SelectionStart = null;
                SelectionEnd = null;
            }

            IsEditorUnlocked = false;
            NewComment = string.Empty;
            StatusMessage = null;
            RefreshVisibleEntries();
        }

        [RelayCommand]
        private void ToggleEditor()
        {
            if (!CanToggleEditor)
                return;

            IsEditorUnlocked = !IsEditorUnlocked;
            StatusMessage = null;
            NotifyEditorStateChanged();
        }

        [RelayCommand]
        private void CancelComment()
        {
            NewComment = string.Empty;
            IsEditorUnlocked = false;
            StatusMessage = null;
            NotifyEditorStateChanged();
        }

        [RelayCommand]
        private async Task SaveCommentAsync()
        {
            if (!CanSaveComment || VisibleEntries.Count != 1)
                return;

            var user = _sessionService.CurrentUser;
            if (user is null)
                return;

            try
            {
                IsSaving = true;
                StatusMessage = null;
                var entry = VisibleEntries[0];
                await _scratchpadService.AddCommentAsync(
                    entry.Id,
                    user.Id,
                    user.DisplayName,
                    NewComment);

                // Existing comments render safely when the window opens. Injecting a
                // new nested data template while the containing ScrollViewer is in a
                // measure pass can make WPF recursively re-enter template creation.
                // Close after a confirmed save and let the next open load the durable
                // database state normally.
                CommentSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScratchpadHistoryViewModel.SaveCommentAsync failed: {ex.Message}");
                StatusMessage = "The retrospective comment could not be saved. Your original entry was not changed.";
            }
            finally
            {
                IsSaving = false;
                NotifyEditorStateChanged();
            }
        }

        partial void OnNewCommentChanged(string value) => NotifyEditorStateChanged();
        partial void OnIsEditorUnlockedChanged(bool value) => NotifyEditorStateChanged();
        partial void OnIsSavingChanged(bool value) => NotifyEditorStateChanged();

        private void RefreshVisibleEntries()
        {
            VisibleEntries.Clear();

            if (SelectionStart.HasValue && SelectionEnd.HasValue)
            {
                foreach (var entry in _allEntries.Where(entry =>
                             entry.Date.Date >= SelectionStart.Value.Date &&
                             entry.Date.Date <= SelectionEnd.Value.Date))
                {
                    VisibleEntries.Add(entry);
                }
            }

            OnPropertyChanged(nameof(HasVisibleEntries));
            OnPropertyChanged(nameof(IsSelectionEmpty));
            OnPropertyChanged(nameof(IsSingleDateSelection));
            OnPropertyChanged(nameof(CanToggleEditor));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(EntryCountSummary));
            OnPropertyChanged(nameof(EditorToolTip));
            NotifyEditorStateChanged();
        }

        private void NotifyEditorStateChanged()
        {
            OnPropertyChanged(nameof(CanToggleEditor));
            OnPropertyChanged(nameof(CanSaveComment));
            OnPropertyChanged(nameof(EditorToolTip));
        }
    }
}
