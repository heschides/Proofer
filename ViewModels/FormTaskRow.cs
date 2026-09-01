using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels
{
    // One row on the task board. Wraps the real Form so the context-menu commands
    // can write straight back to the persisted entity, and exposes the display
    // fields the template binds to. Every display member is *computed* from Form —
    // nothing is stored — so a command mutates Form, persists it, then calls
    // Refresh() to make the view re-read. That's what drives the in-place
    // default -> amber -> green change without rebuilding the list.
    public sealed class FormTaskRow : ObservableObject
    {
        // 'today' is injected rather than read from DateTime.Today inside, matching
        // the asOf convention used across the codebase (GetCurrentCycleForm,
        // GenerateEvents) — one clock for the whole board build, and testable.
        private readonly DateTime _today;

        public FormTaskRow(Form form, string clientName, string typeLabel,
                           DateTime openByDate, DateTime today)
        {
            Form = form;
            ClientName = clientName;
            TypeLabel = typeLabel;
            OpenByDate = openByDate;
            _today = today;
        }

        public Form Form { get; }
        public string ClientName { get; }
        public string TypeLabel { get; }
        public DateTime OpenByDate { get; }
        public DateTime DueDate => Form.DueDate;

        // Priority order: a completed form is green even if it was also opened.
        // IsCompliant is deliberately ignored here — it's the billing flag and
        // defaults true for annual docs, so coloring off it would paint untouched
        // forms green. Color tracks what you actually did, not the billing state.
        public FormWorkflowState State =>
            Form.CompletedDate.HasValue ? FormWorkflowState.Completed
            : Form.OpenedDate.HasValue ? FormWorkflowState.Opened
            : FormWorkflowState.NotStarted;

        // Mirrors GetComplianceStatus's Overdue branch, but applied to THIS form
        // rather than the current-cycle form that method re-fetches — board rows
        // are often the next cycle's renewal, which that method can't see.
        public bool IsOverdue => !Form.IsSatisfiedAsOf(_today) && _today.Date > DueDate.Date;
        public int DaysOverdue => IsOverdue ? (_today.Date - DueDate.Date).Days : 0;
        public string OverdueTooltip => $"{DaysOverdue} day{(DaysOverdue == 1 ? "" : "s")} overdue";

        // Called by the board commands after they mutate and persist Form. Nothing
        // to store — just tell the view to re-read the computed members.
        public void Refresh()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(DaysOverdue));
            OnPropertyChanged(nameof(OverdueTooltip));
        }
    }
}