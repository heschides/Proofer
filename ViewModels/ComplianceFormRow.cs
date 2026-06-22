using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;

namespace Sati.ViewModels
{
    // One row in the compliance-review dialog. Wraps a real Form and holds the
    // in-flight UI state — checkbox + chosen completion date — without mutating the
    // entity until Confirm. The entity stays untouched so a cancelled dialog leaves
    // no trace; Commit() is the single write-back point.
    //
    // The invariant the whole windowed-gate redesign depends on lives in OnIsCompliantChanged:
    // compliant implies a date, not-compliant implies none. You cannot, through this
    // row, reach "compliant with no date" — checking the box defaults the date if the
    // user hasn't set one; unchecking clears it. That guarantee is the reason this
    // class exists rather than binding the checkbox straight to Form.IsCompliant.
    public partial class ComplianceFormRow : ObservableObject
    {
        private readonly Form _form;

        public ComplianceFormRow(Form form)
        {
            _form = form;
            // Seed the row from the entity's current state. A form arriving already
            // compliant (the dialog's default for in-force docs) shows checked with
            // its completion date, defaulting to the due date if none was stored.
            isCompliant = form.IsCompliant;
            completedDate = form.CompletedDate ?? form.DueDate;
        }

        public FormType Type => _form.Type;
        public DateTime DueDate => _form.DueDate;

        // The checkbox. When the [ObservableProperty] generator raises the change,
        // OnIsCompliantChanged enforces the date invariant — this is the one place
        // the check↔date rule is expressed.
        [ObservableProperty] private bool isCompliant;

        // The per-row date picker's value. Defaulted to the due date (the on-time
        // answer) and overridable later — the April-13 late case — so it is NOT
        // clamped to the due date. A null here while IsCompliant is true would be the
        // forbidden state, which is why the partial below refuses to leave it null.
        [ObservableProperty] private DateTime? completedDate;

        partial void OnIsCompliantChanged(bool value)
        {
            if (value)
                CompletedDate ??= DueDate;   // checking with no date yet → default on-time
            else
                CompletedDate = null;        // unchecking → genuinely outstanding, no date
        }

        // Writes the reconciled row state back onto the entity through the only
        // sanctioned door. Called once, on Confirm, for every row.
        public void Commit()
        {
            if (IsCompliant && CompletedDate is DateTime date)
                _form.MarkComplete(date);
            else
                _form.Reset();
        }
    }
}