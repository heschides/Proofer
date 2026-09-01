using Sati.Models;

namespace Sati.Helpers
{
    /// <summary>
    /// Pure timing → color logic for matrix cells. Given a form (or its absence)
    /// and today's date, returns the FormCellStatus that drives cell background.
    ///
    /// This is intentionally orthogonal to the open-form indicator: a cell can
    /// be DueNextMonth AND open. The open border is layered on top in XAML via
    /// a DataTrigger on IsOpen, not encoded as another status here.
    /// </summary>
    public static class FormCellStatusCalculator
    {
        public static FormCellStatus Compute(Form? form, DateTime today)
        {
            // No form record at all — treat as not yet on the radar.
            // (In practice this shouldn't happen for current-cycle forms once
            // EnsureCurrentCycleForms has run, but the matrix is defensive.)
            if (form is null)
                return FormCellStatus.NotYetOpen;

            // Complete trumps timing. A form completed at any point in this
            // cycle stays green regardless of where today falls relative to
            // the original due date.
            //
            // IsSatisfiedAsOf, not IsCompliant: a completion date that has not
            // arrived yet is recorded but not in force, and BillingComplianceGate
            // still blocks on it. A green cell over a blocking form is precisely the
            // disagreement this whole area was fixed to remove.
            if (form.IsSatisfiedAsOf(today))
                return FormCellStatus.Complete;

            var due = form.DueDate.Date;
            var t = today.Date;

            if (due < t)
                return FormCellStatus.Overdue;

            if (due.Year == t.Year && due.Month == t.Month)
                return FormCellStatus.DueThisMonth;

            var nextMonth = t.AddMonths(1);
            if (due.Year == nextMonth.Year && due.Month == nextMonth.Month)
                return FormCellStatus.DueNextMonth;

            return FormCellStatus.NotYetOpen;
        }
    }
}