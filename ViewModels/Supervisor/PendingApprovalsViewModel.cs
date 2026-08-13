using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;

namespace Sati.ViewModels.Supervisor
{
    public partial class PendingApprovalsViewModel : ObservableObject
    {
        private readonly ISupervisorService _supervisorService;
        private readonly ISessionService _sessionService;

        public PendingApprovalsViewModel(
            ISupervisorService supervisorService,
            ISessionService sessionService)
        {
            _supervisorService = supervisorService;
            _sessionService = sessionService;
        }

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        // Notes whose consumers pass the compliance gate — ready for content review.
        public ObservableCollection<PendingNoteViewModel> PendingNotes { get; } = [];

        // Notes whose consumers fail the compliance gate — waiting for compliance
        // to be met, or for a supervisor override with written justification.
        public ObservableCollection<PendingNoteViewModel> NonCompliantNotes { get; } = [];

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private PendingNoteViewModel? selectedNote;
        [ObservableProperty] private string? returnReason;
        [ObservableProperty] private bool isReturnDialogVisible;

        // Override dialog state
        [ObservableProperty] private PendingNoteViewModel? overrideNote;
        [ObservableProperty] private string? overrideReason;
        [ObservableProperty] private bool isOverrideDialogVisible;

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasPending => PendingNotes.Count > 0;
        public bool HasNonCompliant => NonCompliantNotes.Count > 0;
        public string EmptyStateMessage => "No notes pending approval.";
        public string NonCompliantEmptyMessage => "No notes held for compliance.";

        // -------------------------------------------------------------------------
        // Load
        // -------------------------------------------------------------------------

        public async Task LoadAsync(int? filterByUserId = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                PendingNotes.Clear();
                NonCompliantNotes.Clear();
                Debug.WriteLine($"[{sw.ElapsedMilliseconds}ms] Cleared");

                var supervisor = _sessionService.CurrentUser!;
                var allSupervisees = filterByUserId is null;

                var pending = await _supervisorService.GetPendingNotesAsync(
                    supervisor.Id, allSupervisees);
                var nonCompliant = await _supervisorService.GetNonCompliantNotesAsync(
                    supervisor.Id, allSupervisees);

                Debug.WriteLine($"[{sw.ElapsedMilliseconds}ms] Fetched {pending.Count()} pending, {nonCompliant.Count()} non-compliant");

                var filteredPending = filterByUserId is null
                    ? pending
                    : pending.Where(n => n.Person.UserId == filterByUserId);

                var filteredNonCompliant = filterByUserId is null
                    ? nonCompliant
                    : nonCompliant.Where(n => n.Person.UserId == filterByUserId);

                foreach (var note in filteredPending)
                    PendingNotes.Add(new PendingNoteViewModel(note));

                foreach (var note in filteredNonCompliant)
                    NonCompliantNotes.Add(new PendingNoteViewModel(note));

                Debug.WriteLine($"[{sw.ElapsedMilliseconds}ms] Added {PendingNotes.Count} pending, {NonCompliantNotes.Count} non-compliant");

                OnPropertyChanged(nameof(HasPending));
                OnPropertyChanged(nameof(HasNonCompliant));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAsync failed: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Approval commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task Approve(PendingNoteViewModel note)
        {
            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveNoteAsync(note.NoteId, supervisor.Id, note.Revision);
                PendingNotes.Remove(note);
                OnPropertyChanged(nameof(HasPending));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Approve failed: {ex.Message}");
            }
        }

        // Opens the override dialog for a non-compliant note.
        // Supervisor must provide written justification before the override
        // is submitted — the dialog enforces this via ConfirmOverride.
        [RelayCommand]
        private void OpenOverrideDialog(PendingNoteViewModel note)
        {
            OverrideNote = note;
            OverrideReason = string.Empty;
            IsOverrideDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmOverride()
        {
            if (OverrideNote is null || string.IsNullOrWhiteSpace(OverrideReason))
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ApproveWithOverrideAsync(
                    OverrideNote.NoteId,
                    supervisor.Id,
                    OverrideReason,
                    OverrideNote.Revision);

                NonCompliantNotes.Remove(OverrideNote);
                IsOverrideDialogVisible = false;
                OverrideNote = null;
                OverrideReason = string.Empty;
                OnPropertyChanged(nameof(HasNonCompliant));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Override failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelOverride()
        {
            IsOverrideDialogVisible = false;
            OverrideNote = null;
            OverrideReason = string.Empty;
        }

        // -------------------------------------------------------------------------
        // Return commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void OpenReturnDialog(PendingNoteViewModel note)
        {
            SelectedNote = note;
            ReturnReason = string.Empty;
            IsReturnDialogVisible = true;
        }

        [RelayCommand]
        private async Task ConfirmReturn()
        {
            if (SelectedNote is null || string.IsNullOrWhiteSpace(ReturnReason))
                return;

            try
            {
                var supervisor = _sessionService.CurrentUser!;
                await _supervisorService.ReturnNoteAsync(
                    SelectedNote.NoteId,
                    supervisor.Id,
                    ReturnReason,
                    SelectedNote.Revision);

                PendingNotes.Remove(SelectedNote);
                IsReturnDialogVisible = false;
                SelectedNote = null;
                ReturnReason = string.Empty;
                OnPropertyChanged(nameof(HasPending));
            }
            catch (NoteConcurrencyException)
            {
                await HandleNoteConflictAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Return failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void CancelReturn()
        {
            IsReturnDialogVisible = false;
            SelectedNote = null;
            ReturnReason = string.Empty;
        }

        private async Task HandleNoteConflictAsync()
        {
            IsOverrideDialogVisible = false;
            IsReturnDialogVisible = false;
            OverrideNote = null;
            SelectedNote = null;
            await LoadAsync();
            System.Windows.MessageBox.Show(
                "This note changed after you opened the approval queue. The queue has been refreshed; review the latest copy before acting.",
                "Note Updated",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    // -------------------------------------------------------------------------
    // Row view-model
    // -------------------------------------------------------------------------

    public class PendingNoteViewModel
    {
        public int NoteId { get; }
        public int Revision { get; }
        public string ClientName { get; }
        public int PersonId { get; }
        public int CaseManagerUserId { get; }
        public DateTime? EventDate { get; }
        public NoteType? NoteType { get; }
        public decimal? Units { get; }
        public string Narrative { get; }
        public bool IsComplianceException => false; // set by non-compliant queue context

        public PendingNoteViewModel(Note note)
        {
            NoteId = note.Id;
            Revision = note.Revision;
            ClientName = note.Person.FullName;
            PersonId = note.PersonId;
            CaseManagerUserId = note.Person.UserId;
            EventDate = note.EventDate;
            NoteType = note.NoteType;
            Units = note.Units;
            Narrative = note.Narrative;
        }
    }
}
